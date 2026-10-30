using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Conventions;

/// <summary>Helpers shared by the entity configurations (audit shadow columns, enum CHECK constraints).</summary>
public static class ModelBuilderExtensions
{
    public const string CreatedAtUtc = "CreatedAtUtc";
    public const string CreatedBy = "CreatedBy";
    public const string ModifiedAtUtc = "ModifiedAtUtc";
    public const string RowVersion = "RowVersion";

    /// <summary>
    /// Audit columns as EF Core shadow properties: they exist in the table but not on the domain
    /// entity. <see cref="AuditColumnsInterceptor"/> fills them on SaveChanges; the DEFAULT
    /// constraints cover rows written by stored procedures and seed scripts.
    /// </summary>
    public static EntityTypeBuilder<T> HasAuditColumns<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        var table = builder.Metadata.GetTableName();
        builder.Property<DateTime>(CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()", $"DF_{table}_CreatedAtUtc");
        builder.Property<string>(CreatedBy).IsRequired().HasMaxLength(50).IsUnicode(false).HasDefaultValue("system", $"DF_{table}_CreatedBy");
        builder.Property<DateTime?>(ModifiedAtUtc).HasColumnType("datetime2(3)");
        return builder;
    }

    /// <summary>Optimistic concurrency token as a shadow rowversion column.</summary>
    public static EntityTypeBuilder<T> HasRowVersion<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();
        return builder;
    }

    /// <summary>Stores an enum as varchar and adds a named CHECK constraint listing the allowed values.</summary>
    public static PropertyBuilder<TEnum> HasEnumText<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, System.Linq.Expressions.Expression<Func<TEntity, TEnum>> property, string column, int maxLength)
        where TEntity : class
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(builder);
        var allowed = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        var table = builder.Metadata.GetTableName();
        builder.ToTable(t => t.HasCheckConstraint($"CK_{table}_{column}", $"[{column}] IN ({allowed})"));
        return builder.Property(property).HasColumnName(column).HasConversion<string>().HasMaxLength(maxLength).IsUnicode(false);
    }
}
