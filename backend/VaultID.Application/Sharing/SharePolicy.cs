using VaultID.Domain;

namespace VaultID.Application.Sharing;

/// <summary>
/// Business rules for sharing durations and renewal windows (blueprint 4.4 /
/// 4.6). Pure logic - no state, no I/O.
/// </summary>
public static class SharePolicy
{
    /// <summary>
    /// Computes the expiry instant for a duration starting now. "Indefinite"
    /// returns null (no hard expiry) but is still subject to periodic re-consent
    /// handled separately.
    /// </summary>
    public static DateTimeOffset? ComputeExpiry(ShareDuration duration, DateTimeOffset now) => duration switch
    {
        ShareDuration.ThirtyDays => now.AddDays(30),
        ShareDuration.NinetyDays => now.AddDays(90),
        ShareDuration.OneYear => now.AddYears(1),
        ShareDuration.Indefinite => null,
        ShareDuration.Custom => throw new ArgumentException(
            "A custom-duration share carries its own expiry date; it is not derived from the duration.",
            nameof(duration)),
        _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, null)
    };

    /// <summary>
    /// Number of days before expiry at which the user is notified to renew
    /// (blueprint 4.6: 30-day -> 3 days before; 1-year -> at 11 months).
    /// Custom expiries return 0: the user picked the exact end date and can move
    /// it whenever they like, so there is no preset window to nudge them about.
    /// </summary>
    public static int RenewalNoticeDays(ShareDuration duration) => duration switch
    {
        ShareDuration.ThirtyDays => 3,
        ShareDuration.NinetyDays => 7,
        ShareDuration.OneYear => 30,
        ShareDuration.Indefinite => 0,
        ShareDuration.Custom => 0,
        _ => 0
    };
}
