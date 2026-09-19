namespace VaultID.Domain;

/// <summary>
/// The data type of a field definition (dynamic-categories spec). Drives both
/// the string-value validation applied to a <c>FieldValue</c> and how the
/// presentation layer renders the corresponding control.
/// </summary>
public enum FieldType
{
    Text,
    LongText,
    Number,
    Date,
    Boolean,
    Choice,
    File,

    /// <summary>
    /// A container holding exactly one set of sub-fields (an address, say).
    /// Carries no value of its own - its children do.
    /// </summary>
    Group,

    /// <summary>
    /// A container whose children are a <em>template</em> rather than a single
    /// set of values: the user adds as many items as they need (bank accounts,
    /// vehicles, emergency contacts). Carries no value of its own; each item
    /// holds one value per child definition, keyed by the item's own id.
    /// </summary>
    Collection
}

/// <summary>Lifecycle state of a permission grant (blueprint 4.4).</summary>
public enum GrantStatus
{
    Active,
    Revoked,
    Expired,
    PendingRenewal
}

/// <summary>
/// User-selected sharing duration (blueprint 4.4 / 4.6). This is the user's own
/// control over how long the API window stays open, distinct from the
/// organisation's DPA retention terms.
/// </summary>
public enum ShareDuration
{
    ThirtyDays,
    NinetyDays,
    OneYear,
    Indefinite,

    /// <summary>
    /// The user picked an exact expiry date and time rather than a preset
    /// window - the expiry lives on the grant's <c>ExpiresAt</c> alone and can
    /// be changed at any point. Used by the share-code flow; the preset members
    /// above are retained for grants created before it existed.
    /// </summary>
    Custom
}

/// <summary>
/// Lifecycle of a share code - the one-time credential a user hands to an
/// organisation so it can request the fields the user pre-selected.
/// </summary>
public enum ShareCodeStatus
{
    /// <summary>Generated and waiting for the target organisation to redeem it.</summary>
    Pending,

    /// <summary>Redeemed by the organisation; waiting for the user to approve or reject.</summary>
    AwaitingApproval,

    /// <summary>The user approved; the grants are live and the code is spent.</summary>
    Approved,

    /// <summary>The user rejected the request; the code is spent.</summary>
    Rejected,

    /// <summary>The code was never redeemed within its validity window.</summary>
    Expired,

    /// <summary>The user cancelled the code before it was acted on.</summary>
    Revoked
}

/// <summary>Scope of access granted to an organisation.</summary>
public enum AccessScope
{
    /// <summary>Default: organisation may read field values.</summary>
    ReadOnly,

    /// <summary>Verification only: organisation receives true/false, never the value.</summary>
    ReadWithVerification
}

/// <summary>How the user expressed consent for a grant.</summary>
public enum ConsentMethod
{
    InAppConfirmation,
    Biometric
}
