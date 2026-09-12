namespace VaultID.Domain.Categories;

/// <summary>
/// The WHATWG HTML Living Standard <c>autocomplete</c> attribute token
/// vocabulary (dynamic-categories spec, decision 4). A <c>FieldDefinition</c>
/// may optionally map to one of these tokens so the corresponding rendered
/// <c>&lt;input&gt;</c>/<c>&lt;select&gt;</c> stays spec-valid and usable for
/// browser/autofill purposes.
/// <para>
/// This is not the complete grammar of the standard (which also allows
/// section-* / shipping / billing / contact-type prefixes and combinations) -
/// it is the fixed set of single "detail" and "field name" tokens a user is
/// offered when mapping one of their own fields, which is a reasonably
/// complete practical subset of the standard.
/// </para>
/// </summary>
public static class AutocompleteTokens
{
    public static readonly IReadOnlyList<string> All =
    [
        // Off / on
        "off", "on",

        // Name
        "name", "honorific-prefix", "given-name", "additional-name",
        "family-name", "honorific-suffix", "nickname",

        // Credentials / account
        "username", "new-password", "current-password", "one-time-code",

        // Organisation
        "organization-title", "organization",

        // Address
        "street-address", "address-line1", "address-line2", "address-line3",
        "address-level4", "address-level3", "address-level2", "address-level1",
        "country", "country-name", "postal-code",

        // Payment
        "cc-name", "cc-given-name", "cc-additional-name", "cc-family-name",
        "cc-number", "cc-exp", "cc-exp-month", "cc-exp-year", "cc-csc",
        "cc-type",

        // Misc identity / commerce
        "transaction-currency", "transaction-amount", "language", "bday",
        "bday-day", "bday-month", "bday-year", "sex", "url", "photo",

        // Contact
        "tel", "tel-country-code", "tel-national", "tel-area-code",
        "tel-local", "tel-extension", "email", "impp"
    ];

    private static readonly HashSet<string> Lookup = new(All, StringComparer.Ordinal);

    /// <summary>True if <paramref name="token"/> is a recognised autocomplete token.</summary>
    public static bool IsValid(string token) => Lookup.Contains(token);
}
