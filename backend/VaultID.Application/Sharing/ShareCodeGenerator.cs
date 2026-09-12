using System.Security.Cryptography;
using System.Text;

namespace VaultID.Application.Sharing;

/// <summary>
/// Generates and hashes share codes. A share code is a bearer credential: it is
/// the only thing standing between an organisation and a request against the
/// user's vault, so it is generated from a cryptographic RNG and only ever
/// persisted as a hash.
/// <para>
/// The alphabet deliberately omits the characters people confuse when reading a
/// code aloud or copying it off a screen (0/O, 1/I/L, 2/Z, 5/S, 8/B), which
/// removes a whole class of "my code doesn't work" support cases without
/// weakening the code meaningfully: 8 characters over a 25-symbol alphabet is
/// still ~37.1 bits of entropy, and redemption is rate-limited and scoped to a
/// single named organisation.
/// </para>
/// </summary>
public static class ShareCodeGenerator
{
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
    private const int CodeLength = 8;

    /// <summary>How long a generated code stays redeemable if nobody acts on it.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Produces a new code in its display form (<c>XXXX-XXXX</c>). This is the
    /// only moment the plaintext exists - it is returned to the user and then
    /// discarded; everything downstream works off <see cref="Hash"/>.
    /// </summary>
    public static string Generate()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    /// <summary>
    /// Hashes a code for storage and lookup. Codes are high-entropy random
    /// values rather than user-chosen secrets, so a single SHA-256 pass is the
    /// right primitive here - there is no dictionary for an attacker to run.
    /// </summary>
    public static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Normalise(code)));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Canonicalises what a person typed: trims, uppercases, and strips the
    /// display separators (spaces and dashes) so <c>k7m2-9qxb</c>,
    /// <c>K7M2 9QXB</c> and <c>K7M29QXB</c> all resolve to the same code.
    /// </summary>
    public static string Normalise(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var builder = new StringBuilder(CodeLength);
        foreach (var c in code)
        {
            if (c is '-' or ' ' or '\t')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the input could be a share code at all. Used to reject
    /// obvious junk before it reaches the lookup index.
    /// </summary>
    public static bool IsWellFormed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalised = Normalise(code);
        return normalised.Length == CodeLength && normalised.All(Alphabet.Contains);
    }
}
