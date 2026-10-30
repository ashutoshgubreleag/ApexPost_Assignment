using ApexPost.Database.Conventions;
using ApexPost.Database.Records;
using ApexPost.Database.Seeding;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("ProductCategories");
        builder.HasKey(c => c.Id).HasName("PK_ProductCategories");
        builder.Property(c => c.Id).HasConversion<int>().ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(50);
        builder.HasIndex(c => c.Name).IsUnique().HasDatabaseName("UQ_ProductCategories_Name");

        builder.HasData(
            new Category(ProductCategory.Packaging, "Packaging"),
            new Category(ProductCategory.Stationery, "Stationery"),
            new Category(ProductCategory.OfficeSupplies, "Office supplies"));
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id).HasName("PK_Products");

        // Sku is the business key shared with offline terminals: UNIQUE and the target of stock FKs.
        builder.HasAlternateKey(p => p.Sku).HasName("UQ_Products_Sku");
        builder.ToTable(t => t.HasCheckConstraint("CK_Products_Sku_Format", "[Sku] LIKE '[A-Z][A-Z][A-Z]-[0-9][0-9][0-9]'"));
        builder.Property(p => p.Name).HasMaxLength(120);
        builder.Ignore(p => p.UnitPrice);
        builder.Property<decimal>(ProductColumns.UnitPrice).HasColumnName("UnitPrice").HasPrecision(18, 2);
        builder.ToTable(t => t.HasCheckConstraint("CK_Products_UnitPrice_Positive", "[UnitPrice] > 0"));
        builder.HasEnumText(p => p.TaxCategory, "TaxCategory", 10);
        builder.Property(p => p.IsActive).HasDefaultValue(true, "DF_Products_IsActive").HasSentinel(true);

        builder.Property(p => p.Category).HasColumnName("CategoryId").HasConversion<int>();
        builder.HasOne<Category>().WithMany().HasForeignKey(p => p.Category)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Products_ProductCategories");
        builder.HasAuditColumns();

        builder.HasData(ReferenceData.Products.Select(p => new
        {
            p.Id,
            Sku = new Sku(p.Sku),
            p.Name,
            Category = Enum.Parse<ProductCategory>(p.Category),
            _unitPrice = p.UnitPrice,
            TaxCategory = Enum.Parse<TaxCategory>(p.TaxCategory),
            p.IsActive,
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        }));
    }
}

public sealed class BranchStockConfiguration : IEntityTypeConfiguration<BranchStock>
{
    public void Configure(EntityTypeBuilder<BranchStock> builder)
    {
        builder.ToTable("BranchStock");
        builder.HasKey(s => s.Id).HasName("PK_BranchStock");
        builder.HasAlternateKey(s => new { s.BranchCode, s.Sku }).HasName("UQ_BranchStock_Branch_Sku");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BranchStock_QuantityOnHand_NonNegative", "[QuantityOnHand] >= 0");
            t.HasCheckConstraint("CK_BranchStock_ReorderThreshold_NonNegative", "[ReorderThreshold] >= 0");
        });
        builder.Property(s => s.UpdatedAt).HasColumnName("UpdatedAtUtc");

        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_BranchStock_Branches");
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.Sku).HasPrincipalKey(p => p.Sku)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_BranchStock_Products");
        builder.HasIndex(s => s.Sku).HasDatabaseName("IX_BranchStock_Sku");

        // Two clerks selling the last unit: the second SaveChanges sees a changed rowversion.
        builder.HasRowVersion();
        builder.HasAuditColumns();

        builder.HasData(ReferenceData.Stock.Select(s => new
        {
            s.Id,
            BranchCode = new BranchCode(s.BranchCode),
            Sku = new Sku(s.Sku),
            s.QuantityOnHand,
            s.ReorderThreshold,
            s.UpdatedAt,
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        }));
    }
}

public sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustmentRecord>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentRecord> builder)
    {
        builder.ToTable("StockAdjustments", t =>
        {
            t.HasCheckConstraint("CK_StockAdjustments_Delta_NonZero", "[Delta] <> 0");
            t.HasCheckConstraint("CK_StockAdjustments_QuantityAfter_NonNegative", "[QuantityAfter] >= 0");
        });
        builder.HasKey(a => a.Id).HasName("PK_StockAdjustments");
        builder.Property(a => a.Reason).HasMaxLength(200);
        builder.Property(a => a.ChangedBy).HasMaxLength(50).IsUnicode(false);
        builder.Property(a => a.ChangedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()", "DF_StockAdjustments_ChangedAtUtc");

        // History follows its stock row; RESTRICT so adjustments are never silently lost.
        builder.HasOne<BranchStock>().WithMany()
            .HasForeignKey(a => new { a.BranchCode, a.Sku })
            .HasPrincipalKey(s => new { s.BranchCode, s.Sku })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_StockAdjustments_BranchStock");
        builder.HasIndex(a => new { a.BranchCode, a.Sku, a.ChangedAtUtc }).HasDatabaseName("IX_StockAdjustments_Branch_Sku_ChangedAt");
    }
}

/// <summary>Column names used with EF.Property in queries and ExecuteUpdate.</summary>
public static class ProductColumns
{
    /// <summary>EF property behind Product.UnitPrice: the private decimal field, mapped to column "UnitPrice".</summary>
    public const string UnitPrice = "_unitPrice";
}
