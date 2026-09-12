using System.Globalization;

namespace VaultID.Domain.Categories;

/// <summary>
/// Validates a field value string against its <see cref="FieldDefinition"/>'s
/// <see cref="FieldType"/> (dynamic-categories spec, "Validation service").
/// Every <c>FieldValue</c> is always stored as a string; this is where the
/// type-specific parsing rule for each <see cref="FieldType"/> lives. Pure
/// logic - no I/O - so the Application layer's validation service can wrap it
/// with the lookup of the relevant <see cref="FieldDefinition"/>.
/// </summary>
public static class FieldValueValidator
{
    /// <summary>
    /// Returns true when <paramref name="value"/> is a legal value for
    /// <paramref name="field"/>; otherwise false with a human-readable
    /// <paramref name="error"/>.
    /// </summary>
    public static bool TryValidate(FieldDefinition field, string? value, out string? error)
    {
        switch (field.FieldType)
        {
            case FieldType.Group:
                if (value is not null)
                {
                    error = $"'{field.Name}' is a Group field and can never hold a value directly.";
                    return false;
                }

                break;

            case FieldType.Number:
                if (value is null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    error = $"'{value}' is not a valid number for field '{field.Name}'.";
                    return false;
                }

                break;

            case FieldType.Date:
                if (value is null || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    error = $"'{value}' is not a valid date for field '{field.Name}'.";
                    return false;
                }

                break;

            case FieldType.Boolean:
                if (value is not ("true" or "false"))
                {
                    error = $"'{field.Name}' must be 'true' or 'false'.";
                    return false;
                }

                break;

            case FieldType.Choice:
                if (value is null || field.Choices is null || !field.Choices.Contains(value, StringComparer.Ordinal))
                {
                    error = $"'{value}' is not one of the allowed choices for field '{field.Name}'.";
                    return false;
                }

                break;

            case FieldType.Text:
            case FieldType.LongText:
            case FieldType.File:
                // Any string; no length limit beyond the Api's transport-level cap.
                break;
        }

        error = null;
        return true;
    }
}
