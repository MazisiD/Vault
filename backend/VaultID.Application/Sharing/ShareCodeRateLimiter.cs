using System.Collections.Concurrent;

namespace VaultID.Application.Sharing;

/// <summary>
/// Throttles share-code redemption attempts per organisation.
/// <para>
/// Without this, the redemption endpoint is an online guessing oracle: an
/// organisation could walk the code space until it hit a valid code belonging
/// to some user. A fixed-window counter caps how fast that search can run,
/// which - combined with the code's entropy and its binding to a single
/// organisation - makes brute force impractical.
/// </para>
/// <para>
/// Only failed attempts count against the limit, so an organisation legitimately
/// redeeming many codes is never throttled.
/// </para>
/// </summary>
public sealed class ShareCodeRateLimiter
{
    /// <summary>Failed attempts an organisation may make within one window.</summary>
    public const int MaxFailedAttemptsPerWindow = 10;

    /// <summary>Length of the fixed counting window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the organisation has burned through its failure budget and
    /// should be refused before any lookup happens.
    /// </summary>
    public bool IsThrottled(string organisationId, DateTimeOffset now) =>
        _attempts.TryGetValue(organisationId, out var record) &&
        now - record.WindowStart < Window &&
        record.Failures >= MaxFailedAttemptsPerWindow;

    /// <summary>Records a failed redemption attempt, rolling the window if it has elapsed.</summary>
    public void RecordFailure(string organisationId, DateTimeOffset now) =>
        _attempts.AddOrUpdate(
            organisationId,
            _ => new Attempts(now, 1),
            (_, existing) => now - existing.WindowStart >= Window
                ? new Attempts(now, 1)
                : existing with { Failures = existing.Failures + 1 });

    /// <summary>Clears an organisation's failure budget after a successful redemption.</summary>
    public void RecordSuccess(string organisationId) => _attempts.TryRemove(organisationId, out _);

    private sealed record Attempts(DateTimeOffset WindowStart, int Failures);
}
