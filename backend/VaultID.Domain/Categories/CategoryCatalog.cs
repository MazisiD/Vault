namespace VaultID.Domain.Categories;

/// <summary>
/// Seed data for the 3 permanent "system" categories and their fields
/// (dynamic-categories spec, "Migration / seeding"). This preserves today's
/// exact category/field list as data: every name here matches the original
/// compiled-in catalog exactly, seeded as <see cref="FieldType.Text"/> fields
/// with no silent type upgrades. The Application layer's <c>VaultService</c>
/// uses this to build the <c>Category</c>/<c>FieldDefinition</c> entities (with
/// fresh ids) that a new vault's <c>VaultCreated</c> event seeds.
/// </summary>
public static class CategoryCatalog
{
    /// <summary>One seeded field: a name and its (always Text) field type.</summary>
    public sealed record SeedField(string Name, FieldType FieldType = FieldType.Text);

    /// <summary>One seeded system category: a name and its ordered fields.</summary>
    public sealed record SeedCategory(string Name, IReadOnlyList<SeedField> Fields);

    /// <summary>The 3 system categories a new vault is seeded with, in order.</summary>
    public static IReadOnlyList<SeedCategory> SystemCategories { get; } =
    [
        new("Biographical",
        [
            new("FullName"), new("IdOrPassportNumber"), new("DateOfBirth"), new("Gender"),
            new("PhysicalAddress"), new("PostalAddress"), new("PhoneNumber"), new("EmailAddress"),
            new("Nationality"), new("HomeLanguage"), new("MaritalStatus"), new("BankAccountDetails"),
            new("TaxNumber"), new("NextOfKin")
        ]),
        new("Health",
        [
            new("BloodType"), new("KnownAllergies"), new("ChronicConditions"), new("CurrentMedications"),
            new("MedicalAidProvider"), new("MedicalAidNumber"), new("VaccinationRecords"),
            new("DisabilityStatus"), new("EmergencyContact"), new("PrimaryDoctor"),
            new("MentalHealthNotes"), new("OrganDonorStatus")
        ]),
        new("Educational",
        [
            new("HighestQualification"), new("InstitutionsAttended"), new("DegreesOrDiplomas"),
            new("AcademicTranscripts"), new("ProfessionalCertifications"), new("SkillsOrShortCourses"),
            new("StudentNumber"), new("ResearchPublications"), new("ProfessionalMemberships"),
            new("CpdRecords")
        ])
    ];
}
