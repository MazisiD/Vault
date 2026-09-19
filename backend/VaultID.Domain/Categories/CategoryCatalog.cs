namespace VaultID.Domain.Categories;

/// <summary>
/// Seed data for the permanent "system" categories and their fields
/// (dynamic-categories spec, "Migration / seeding"). The Application layer's
/// <c>VaultService</c> uses this to build the <see cref="Category"/> /
/// <see cref="FieldDefinition"/> entities (with fresh ids) that a new vault's
/// <c>VaultCreated</c> event seeds.
/// <para>
/// The shape here is what the vault screen renders: plain fields, Groups
/// holding one set of sub-fields (an address), and Collections the user adds
/// items to (bank accounts, vehicles, emergency contacts). A field holding a
/// number nobody should read over the owner's shoulder is marked secret, which
/// the UI renders masked behind a reveal toggle.
/// </para>
/// </summary>
public static class CategoryCatalog
{
    /// <summary>
    /// One seeded field. <paramref name="Children"/> holds the sub-fields of a
    /// Group, or the per-item template of a Collection.
    /// </summary>
    public sealed record SeedField(
        string Name,
        FieldType FieldType = FieldType.Text,
        bool Secret = false,
        string? ItemNoun = null,
        bool IsItemTitle = false,
        IReadOnlyList<string>? Choices = null,
        IReadOnlyList<SeedField>? Children = null);

    /// <summary>One seeded system category: a name and its ordered fields.</summary>
    public sealed record SeedCategory(string Name, IReadOnlyList<SeedField> Fields);

    private static SeedField Group(string name, params SeedField[] children) =>
        new(name, FieldType.Group, Children: children);

    private static SeedField Collection(string name, string itemNoun, params SeedField[] children) =>
        new(name, FieldType.Collection, ItemNoun: itemNoun, Children: children);

    /// <summary>A field whose value is masked until the owner reveals it.</summary>
    private static SeedField Secret(string name) => new(name, Secret: true);

    /// <summary>The child of a Collection whose value names an item in a collapsed list.</summary>
    private static SeedField Title(string name) => new(name, IsItemTitle: true);

    private static readonly string[] GenderChoices = ["Female", "Male", "Non-binary", "Prefer not to say", "Other"];
    private static readonly string[] NationalityChoices = [
        "South African", "Zimbabwean", "Kenyan", "Nigerian", "Ghanaian", "British", "American",
        "Canadian", "Australian", "French", "German", "Dutch", "Spanish", "Portuguese", "Indian",
        "Chinese", "Japanese", "Korean", "Arabic", "Italian", "Belgian", "Swiss", "Irish",
        "New Zealander", "Scottish", "Welsh", "Romanian", "Greek", "Turkish", "Egyptian", "Other"
    ];
    private static readonly string[] HomeLanguageChoices = [
        "English", "Afrikaans", "Zulu", "Xhosa", "Sotho", "Tswana", "Swati", "Venda", "Tsonga",
        "Ndebele", "French", "Arabic", "Portuguese", "Spanish", "Hindi", "Mandarin", "German",
        "Shona", "Swahili", "Other"
    ];
    private static readonly string[] MaritalStatusChoices = [
        "Single", "Married", "Separated", "Divorced", "Widowed", "Domestic partnership",
        "Prefer not to say", "Other"
    ];
    private static readonly string[] BloodTypeChoices = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-", "Unknown"]; 
    private static readonly string[] DisabilityStatusChoices = ["None", "Physical disability", "Sensory disability", "Cognitive disability", "Chronic illness", "Prefer not to say", "Other"];
    private static readonly string[] OrganDonorStatusChoices = ["Yes", "No", "Undecided", "Prefer not to say", "Other"];
    private static readonly string[] ReligiousAffiliationChoices = ["Christianity", "Islam", "Hinduism", "Judaism", "Buddhism", "Atheist", "Agnostic", "Prefer not to say", "Other"];

    /// <summary>An address: the same four parts wherever one appears.</summary>
    private static SeedField Address(string name) =>
        Group(name,
            new SeedField("StreetNumber"), new SeedField("Suburb"),
            new SeedField("Town"), new SeedField("PostalCode"));

    /// <summary>
    /// The system categories a new vault is seeded with. Listed alphabetically,
    /// which is also the order the vault screen shows them in.
    /// </summary>
    public static IReadOnlyList<SeedCategory> SystemCategories { get; } =
    [
        new("Biographical",
        [
            new("FullName"),
            Secret("IdOrPassportNumber"),
            new("DateOfBirth", FieldType.Date),
            new("Gender", FieldType.Choice, Choices: GenderChoices),
            Address("PhysicalAddress"),
            Address("PostalAddress"),
            Collection("PhoneNumbers", "phone number",
                Title("Label"),
                new SeedField("Number")),
            new("EmailAddress"),
            new("Nationality", FieldType.Choice, Choices: NationalityChoices),
            new("HomeLanguage", FieldType.Choice, Choices: HomeLanguageChoices),
            new("MaritalStatus", FieldType.Choice, Choices: MaritalStatusChoices),
            Collection("EmergencyContacts", "contact",
                Title("FullName"),
                new SeedField("Relationship"),
                new SeedField("PhoneNumber"))
        ]),
        new("Educational",
        [
            new("HighestQualification"),
            new("StudentNumber"),
            Collection("Qualifications", "qualification",
                Title("Qualification"),
                new SeedField("Institution"),
                new SeedField("YearCompleted")),
            Collection("ProfessionalCertifications", "certification",
                Title("Certification"),
                new SeedField("IssuingBody"),
                new SeedField("ExpiryDate")),
            new("SkillsOrShortCourses", FieldType.LongText),
            new("ProfessionalMemberships", FieldType.LongText),
            new("ResearchPublications", FieldType.LongText),
            new("CpdRecords", FieldType.LongText)
        ]),
        new("Financial",
        [
            Secret("TaxNumber"),
            Collection("BankAccounts", "bank account",
                Title("BankName"),
                new SeedField("AccountHolder"),
                Secret("AccountNumber"),
                new SeedField("BranchCode"),
                new SeedField("AccountType")),
            Collection("Vehicles", "vehicle",
                new SeedField("Make"),
                Title("Model"),
                new SeedField("RegistrationPlate"),
                Secret("Vin")),
            Collection("InsurancePolicies", "policy",
                Title("Provider"),
                new SeedField("PolicyType"),
                Secret("PolicyNumber"))
        ]),
        new("Health",
        [
            new("BloodType", FieldType.Choice, Choices: BloodTypeChoices),
            new("KnownAllergies", FieldType.LongText),
            new("ChronicConditions", FieldType.LongText),
            Collection("CurrentMedications", "medication",
                Title("Medicine"),
                new SeedField("Dosage"),
                new SeedField("Frequency")),
            Group("MedicalAid",
                Title("Provider"),
                Secret("MembershipNumber"),
                new SeedField("Plan")),
            new("VaccinationRecords", FieldType.LongText),
            new("DisabilityStatus", FieldType.Choice, Choices: DisabilityStatusChoices),
            new("PrimaryDoctor"),
            new("OrganDonorStatus", FieldType.Choice, Choices: OrganDonorStatusChoices)
        ]),
        new("Religious",
        [
            new("ReligiousAffiliation", FieldType.Choice, Choices: ReligiousAffiliationChoices),
            new("Denomination"),
            new("PlaceOfWorship"),
            new("DietaryRequirements", FieldType.LongText),
            new("ObservanceNotes", FieldType.LongText),
            new("EndOfLifeWishes", FieldType.LongText)
        ])
    ];
}
