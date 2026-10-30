using ApexPost.Models.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApexPost.Database.Conventions;

/// <summary>Money ↔ decimal(18,2). The value object keeps arithmetic and rounding in the domain.</summary>
public sealed class MoneyConverter() : ValueConverter<Money, decimal>(m => m.Amount, d => new Money(d));

/// <summary>BranchCode ↔ varchar(10). The constructor re-validates the format when reading.</summary>
public sealed class BranchCodeConverter() : ValueConverter<BranchCode, string>(c => c.Value, s => new BranchCode(s));

/// <summary>Sku ↔ varchar(20).</summary>
public sealed class SkuConverter() : ValueConverter<Sku, string>(s => s.Value, s => new Sku(s));

/// <summary>Weight ↔ decimal(9,3) kilograms.</summary>
public sealed class WeightConverter() : ValueConverter<Weight, decimal>(w => w.Kilograms, d => new Weight(d));

/// <summary>
/// All timestamps are stored as UTC datetime2 (the brief asks for UTC, not datetimeoffset).
/// Values are normalised to UTC on the way in and come back with a zero offset.
/// </summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    v => v.UtcDateTime,
    v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));

/// <summary>Model-wide type mapping so every Money/BranchCode/Sku/Weight/DateTimeOffset column is consistent.</summary>
public static class ApexPostConventions
{
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        configurationBuilder.Properties<BranchCode>().HaveConversion<BranchCodeConverter>().HaveMaxLength(10).AreUnicode(false);
        configurationBuilder.Properties<Sku>().HaveConversion<SkuConverter>().HaveMaxLength(20).AreUnicode(false);
        configurationBuilder.Properties<Weight>().HaveConversion<WeightConverter>().HavePrecision(9, 3);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>().HaveColumnType("datetime2(3)");

        // Codes and enums are ASCII; names and free text are nvarchar (set per property).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }
}
