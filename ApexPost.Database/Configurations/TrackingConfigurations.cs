using ApexPost.Database.Conventions;
using ApexPost.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Configurations;

public sealed class TrackedShipmentConfiguration : IEntityTypeConfiguration<TrackedShipment>
{
    public void Configure(EntityTypeBuilder<TrackedShipment> builder)
    {
        builder.ToTable("TrackedShipments", t =>
        {
            t.HasCheckConstraint("CK_TrackedShipments_WeightKg_Range", "[WeightKg] >= 0.1 AND [WeightKg] <= 70");
            t.HasCheckConstraint("CK_TrackedShipments_TrackingNumber_Format", "[TrackingNumber] LIKE 'APX-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
        });
        builder.HasKey(s => s.Id).HasName("PK_TrackedShipments");
        builder.Property(s => s.TrackingNumber).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.HasIndex(s => s.TrackingNumber).IsUnique().HasDatabaseName("UQ_TrackedShipments_TrackingNumber");
        builder.Property(s => s.RecipientName).HasMaxLength(TrackedShipment.MaxText).IsRequired();
        builder.Property(s => s.RecipientPhone).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(s => s.AddressLine).HasMaxLength(TrackedShipment.MaxText).IsRequired();
        builder.Property(s => s.City).HasMaxLength(100).IsRequired();
        builder.Property(s => s.WeightKg).HasPrecision(9, 3);
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2(3)");
        builder.HasEnumText(s => s.Status, "Status", 20);
        builder.HasIndex(s => new { s.Status, s.City }).HasDatabaseName("IX_TrackedShipments_Status_City");

        builder.HasMany(s => s.Scans).WithOne()
            .HasForeignKey(x => x.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_ShipmentScans_TrackedShipments");
        builder.Navigation(s => s.Scans).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasAuditColumns();
        builder.HasRowVersion();
    }
}

public sealed class ShipmentScanConfiguration : IEntityTypeConfiguration<ShipmentScan>
{
    public void Configure(EntityTypeBuilder<ShipmentScan> builder)
    {
        builder.ToTable("ShipmentScans");
        builder.HasKey(s => s.Id).HasName("PK_ShipmentScans");
        builder.Property(s => s.ScanId).IsRequired();
        builder.HasIndex(s => s.ScanId).IsUnique().HasDatabaseName("UQ_ShipmentScans_ScanId");
        builder.HasEnumText(s => s.Type, "Type", 20);
        builder.Property(s => s.Location).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ScannedAt).HasColumnType("datetime2(3)");
        builder.Property(s => s.RecipientSignatureName).HasMaxLength(100);
        builder.HasIndex(s => new { s.ShipmentId, s.ScannedAt }).HasDatabaseName("IX_ShipmentScans_Shipment_ScannedAt");
        builder.HasAuditColumns();
    }
}
