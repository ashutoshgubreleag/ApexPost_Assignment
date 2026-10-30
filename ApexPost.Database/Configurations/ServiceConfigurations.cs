using ApexPost.Database.Conventions;
using ApexPost.Database.Records;
using ApexPost.Database.Seeding;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Configurations;

public sealed class ShippingZoneConfiguration : IEntityTypeConfiguration<ShippingZone>
{
    public void Configure(EntityTypeBuilder<ShippingZone> builder)
    {
        builder.ToTable("ShippingZones");
        builder.HasKey(z => z.Id).HasName("PK_ShippingZones");
        builder.Property(z => z.ZoneCode).HasMaxLength(5).IsUnicode(false);
        builder.HasAlternateKey(z => z.ZoneCode).HasName("UQ_ShippingZones_ZoneCode");
        builder.ToTable(t => t.HasCheckConstraint("CK_ShippingZones_ZoneCode_Format", "[ZoneCode] LIKE 'Z[0-9][0-9]'"));
        builder.Property(z => z.Name).HasMaxLength(60);
        builder.Ignore(z => z.LetterBaseRate);
        builder.Ignore(z => z.ParcelBaseRate);
        builder.Ignore(z => z.PerKgRate);

        builder.HasMany(z => z.Rates).WithOne()
            .HasForeignKey(r => r.ZoneCode).HasPrincipalKey(z => z.ZoneCode)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ShippingRates_ShippingZones");
        builder.Navigation(z => z.Rates).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasAuditColumns();

        builder.HasData(ReferenceData.Zones.Select(z => new
        {
            z.Id,
            z.ZoneCode,
            z.Name,
            z.IsInternational,
            z.IsRemote,
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        }));
    }
}

public sealed class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        builder.ToTable("ShippingRates", t =>
        {
            t.HasCheckConstraint("CK_ShippingRates_BaseRate_Positive", "[BaseRate] > 0");
            t.HasCheckConstraint("CK_ShippingRates_PerKgRate_NonNegative", "[PerKgRate] >= 0");
        });
        builder.HasKey(r => r.Id).HasName("PK_ShippingRates");
        builder.Property(r => r.ZoneCode).HasMaxLength(5).IsUnicode(false);
        builder.HasEnumText(r => r.ShippingType, "ShippingType", 10);
        builder.HasIndex(r => new { r.ZoneCode, r.ShippingType }).IsUnique().HasDatabaseName("UQ_ShippingRates_Zone_Type");
        builder.HasAuditColumns();

        var id = 0;
        builder.HasData(ReferenceData.Zones.SelectMany(z => new[]
        {
            new { Id = ++id, z.ZoneCode, ShippingType = ShippingType.Letter, BaseRate = new Money(z.LetterBaseRate), PerKgRate = Money.Zero, CreatedAtUtc = ReferenceData.SeededAtUtc, CreatedBy = "seed" },
            new { Id = ++id, z.ZoneCode, ShippingType = ShippingType.Parcel, BaseRate = new Money(z.ParcelBaseRate), PerKgRate = new Money(z.PerKgRate), CreatedAtUtc = ReferenceData.SeededAtUtc, CreatedBy = "seed" },
        }).ToList());
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", t =>
        {
            t.HasCheckConstraint("CK_Shipments_WeightKg_Positive", "[WeightKg] > 0");
            t.HasCheckConstraint("CK_Shipments_ChargeableWeightKg_Positive", "[ChargeableWeightKg] > 0");
            t.HasCheckConstraint("CK_Shipments_Price_Positive", "[Price] > 0");
            t.HasCheckConstraint("CK_Shipments_Dimensions_Positive", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");
        });
        builder.HasKey(s => s.Id).HasName("PK_Shipments");
        builder.Property(s => s.TrackingNumber).HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(s => s.TrackingNumber).IsUnique().HasDatabaseName("UQ_Shipments_TrackingNumber");
        builder.HasEnumText(s => s.ShippingType, "ShippingType", 10);
        builder.HasEnumText(s => s.ServiceLevel, "ServiceLevel", 12);
        builder.HasEnumText(s => s.Status, "Status", 10);
        builder.Property(s => s.ZoneCode).HasMaxLength(5).IsUnicode(false);
        builder.Property(s => s.Weight).HasColumnName("WeightKg");
        builder.Property(s => s.ChargeableWeight).HasColumnName("ChargeableWeightKg");
        builder.Property(s => s.CreatedAt).HasColumnName("QuotedAtUtc");

        // Dimensions is a readonly record struct, so it maps as a complex type (EF Core's value-type
        // equivalent of an owned type): three columns in the Shipments row, no key of its own.
        builder.ComplexProperty(s => s.Dimensions, d =>
        {
            d.Property(x => x.LengthCm).HasColumnName("LengthCm").HasPrecision(7, 1);
            d.Property(x => x.WidthCm).HasColumnName("WidthCm").HasPrecision(7, 1);
            d.Property(x => x.HeightCm).HasColumnName("HeightCm").HasPrecision(7, 1);
        });

        // Address is an owned type: recipient columns in the same row, optional as a whole.
        builder.OwnsOne(s => s.Recipient, a =>
        {
            a.Property(x => x.RecipientName).HasColumnName("RecipientName").HasMaxLength(80);
            a.Property(x => x.Line1).HasColumnName("RecipientLine1").HasMaxLength(120);
            a.Property(x => x.City).HasColumnName("RecipientCity").HasMaxLength(60);
            a.Property(x => x.State).HasColumnName("RecipientState").HasMaxLength(20).IsUnicode(false);
            a.Property(x => x.Postcode).HasColumnName("RecipientPostcode").HasMaxLength(12).IsUnicode(false);
        });

        builder.HasOne<CounterTransaction>().WithMany().HasForeignKey(s => s.TransactionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Shipments_CounterTransactions");
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Shipments_Branches");
        builder.HasOne<ShippingZone>().WithMany().HasForeignKey(s => s.ZoneCode).HasPrincipalKey(z => z.ZoneCode)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Shipments_ShippingZones");
        builder.HasIndex(s => s.TransactionId).HasDatabaseName("IX_Shipments_TransactionId");
        builder.HasIndex(s => s.BranchCode).HasDatabaseName("IX_Shipments_BranchCode");
        builder.HasIndex(s => s.ZoneCode).HasDatabaseName("IX_Shipments_ZoneCode");
        builder.HasAuditColumns();
    }
}

public sealed class MoneyOrderConfiguration : IEntityTypeConfiguration<MoneyOrder>
{
    public void Configure(EntityTypeBuilder<MoneyOrder> builder)
    {
        builder.ToTable("MoneyOrders", t =>
        {
            t.HasCheckConstraint("CK_MoneyOrders_Amount_Positive", "[Amount] > 0");
            t.HasCheckConstraint("CK_MoneyOrders_Fee_NonNegative", "[Fee] >= 0");
            t.HasCheckConstraint("CK_MoneyOrders_Issued_HasDate", "[Status] <> 'Issued' OR [IssuedAtUtc] IS NOT NULL");
        });
        builder.HasKey(m => m.Id).HasName("PK_MoneyOrders");
        builder.Property(m => m.SerialNumber).HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(m => m.SerialNumber).IsUnique().HasDatabaseName("UQ_MoneyOrders_SerialNumber");
        builder.Property(m => m.Sender).HasMaxLength(80);
        builder.Property(m => m.Recipient).HasMaxLength(80);
        builder.HasEnumText(m => m.Status, "Status", 10);
        builder.Property(m => m.CreatedAt).HasColumnName("RequestedAtUtc");
        builder.Property(m => m.IssuedAt).HasColumnName("IssuedAtUtc");

        builder.HasOne(m => m.Transaction).WithMany().HasForeignKey(m => m.TransactionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_MoneyOrders_CounterTransactions");
        builder.HasOne<Branch>().WithMany().HasForeignKey(m => m.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_MoneyOrders_Branches");
        builder.HasOne<IdentityVerificationRecord>().WithMany().HasForeignKey(m => m.IdentityVerificationId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_MoneyOrders_IdentityVerifications");
        builder.HasIndex(m => m.TransactionId).HasDatabaseName("IX_MoneyOrders_TransactionId");
        builder.HasIndex(m => m.BranchCode).HasDatabaseName("IX_MoneyOrders_BranchCode");
        builder.HasIndex(m => m.IdentityVerificationId).HasDatabaseName("IX_MoneyOrders_IdentityVerificationId");
        builder.HasAuditColumns();
    }
}

public sealed class IdentityVerificationRecordConfiguration : IEntityTypeConfiguration<IdentityVerificationRecord>
{
    public void Configure(EntityTypeBuilder<IdentityVerificationRecord> builder)
    {
        builder.ToTable("IdentityVerifications", t =>
        {
            // The schema itself refuses anything but four characters: a full document number cannot be stored.
            t.HasCheckConstraint("CK_IdentityVerifications_DocumentLast4_Length", "LEN([DocumentLast4]) = 4");
            t.HasCheckConstraint("CK_IdentityVerifications_DocumentTypeCode", "[DocumentTypeCode] IN ('PP', 'DL', 'NI')");
            t.HasCheckConstraint("CK_IdentityVerifications_ResultCode", "[ResultCode] IN ('PASS', 'FAIL')");
        });
        builder.HasKey(v => v.Id).HasName("PK_IdentityVerifications");
        builder.Property(v => v.Reference).HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(v => v.Reference).IsUnique().HasDatabaseName("UQ_IdentityVerifications_Reference");
        builder.Property(v => v.CustomerName).HasMaxLength(80);
        builder.Property(v => v.DocumentTypeCode).HasMaxLength(2).IsUnicode(false).IsFixedLength();
        builder.Property(v => v.DocumentLast4).HasMaxLength(4).IsUnicode(false).IsFixedLength();
        builder.Property(v => v.ResultCode).HasMaxLength(4).IsUnicode(false);
        builder.Property(v => v.VerifiedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<CounterTransaction>().WithMany().HasForeignKey(v => v.TransactionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_IdentityVerifications_CounterTransactions");
        builder.HasOne<Branch>().WithMany().HasForeignKey(v => v.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_IdentityVerifications_Branches");
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(v => v.StaffId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_IdentityVerifications_Staff");
        builder.HasIndex(v => v.TransactionId).HasDatabaseName("IX_IdentityVerifications_TransactionId");
        builder.HasIndex(v => new { v.BranchCode, v.VerifiedAtUtc }).HasDatabaseName("IX_IdentityVerifications_Branch_VerifiedAt");
        builder.HasIndex(v => v.StaffId).HasDatabaseName("IX_IdentityVerifications_StaffId");
        builder.HasAuditColumns();
    }
}
