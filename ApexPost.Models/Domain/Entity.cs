namespace ApexPost.Models.Domain;

/// <summary>
/// Base class for rich domain entities. The key is assigned by the database (identity column), so
/// it has a protected setter: no caller can invent or change an Id. Audit columns
/// (CreatedAtUtc, CreatedBy, ModifiedAtUtc) and rowversion are deliberately NOT here: they are
/// persistence concerns, mapped as EF Core shadow properties.
/// </summary>
public abstract class Entity
{
    public int Id { get; protected set; }

    /// <summary>True until the entity has been saved for the first time.</summary>
    public bool IsTransient => Id == 0;
}

/// <summary>Small guard helpers so every entity validates its own invariants the same way.</summary>
internal static class Guard
{
    public static string Text(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"{name} must be {maxLength} characters or fewer.", name);
    }

    public static string OptionalText(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"{name} must be {maxLength} characters or fewer.", name);
    }

    public static T Defined<T>(T value, string name) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(name, value, $"{name} is not a valid value.");
}
