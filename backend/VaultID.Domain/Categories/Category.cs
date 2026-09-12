namespace VaultID.Domain.Categories;

/// <summary>
/// A vault data category (dynamic-categories spec). Categories are the
/// fundamental unit of sharing (blueprint 4.2): a user shares an entire
/// category, never individual fields. The 3 original categories
/// (Biographical, Health, Educational) are seeded per-vault as permanent
/// "system" categories; users may additionally create their own.
/// </summary>
public sealed class Category
{
    public required Guid Id { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// True for the 3 built-in categories seeded at vault creation. System
    /// categories can never be renamed or deleted (see
    /// <see cref="CategorySchemaGuard"/>).
    /// </summary>
    public required bool IsSystem { get; init; }
}
