using ApexPost.Database.Conventions;
using ApexPost.Database.Seeding;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Configurations;

/*
 * Delete behaviour policy (ADR-W3-01):
 *  - Master data (branches, staff, products, zones) is never physically deleted: every FK that
 *    points at it is RESTRICT, so history (sales, shipments, audit) can never lose its parent.
 *    Closing a branch or deactivating staff/products is a status change.
 *  - Parts of an aggregate that cannot exist alone (counters of a branch, rates of a zone, lines
 *    and payments of a transaction, conflicts of an inbox row) CASCADE from their root.
 */

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id).HasName("PK_Roles");
        builder.Property(r => r.Id).HasConversion<int>().ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(30).IsUnicode(false);
        builder.Property(r => r.Description).HasMaxLength(200);
        builder.HasIndex(r => r.Name).IsUnique().HasDatabaseName("UQ_Roles_Name");

        builder.HasData(
            new Role(StaffRole.CounterClerk, "CounterClerk", "Sells at the counter of one branch."),
            new Role(StaffRole.BranchManager, "BranchManager", "Runs one branch: voids, refunds, stock and staff."),
            new Role(StaffRole.Auditor, "Auditor", "Read-only access to every branch and the audit log."),
            new Role(StaffRole.Administrator, "Administrator", "Manages branches, staff, prices and maintenance."));
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id).HasName("PK_Branches");
        builder.Property(b => b.Code).HasColumnName("BranchCode");

        // BranchCode is the immutable business key: UNIQUE, and the target of every branch FK
        // (offline terminals and reports speak in branch codes, not surrogate ids).
        builder.HasAlternateKey(b => b.Code).HasName("UQ_Branches_BranchCode");
        builder.ToTable(t => t.HasCheckConstraint("CK_Branches_BranchCode_Format", "[BranchCode] LIKE 'BR-[0-9][0-9][0-9]'"));

        builder.Property(b => b.Name).HasMaxLength(100);
        builder.Property(b => b.Region).HasMaxLength(60);
        builder.Property(b => b.OpeningHours).HasMaxLength(120);
        builder.HasEnumText(b => b.Status, "Status", 10).HasDefaultValue(BranchStatus.Active, "DF_Branches_Status").HasSentinel(BranchStatus.Active);
        builder.Ignore(b => b.IsOpen);

        builder.HasMany(b => b.Counters).WithOne()
            .HasForeignKey(c => c.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Counters_Branches");
        builder.Navigation(b => b.Counters).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasAuditColumns();

        builder.HasData(ReferenceData.Branches.Select(b => new
        {
            b.Id,
            Code = new BranchCode(b.BranchCode),
            b.Name,
            b.Region,
            b.OpeningHours,
            Status = Enum.Parse<BranchStatus>(b.Status),
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        }));
    }
}

public sealed class CounterConfiguration : IEntityTypeConfiguration<Counter>
{
    public void Configure(EntityTypeBuilder<Counter> builder)
    {
        builder.ToTable("Counters");
        builder.HasKey(c => c.Id).HasName("PK_Counters");

        // A counter is identified within its branch by number; transactions reference that pair.
        builder.HasAlternateKey(c => new { c.BranchCode, c.Number }).HasName("UQ_Counters_Branch_Number");
        builder.Property(c => c.Name).HasMaxLength(50);
        builder.Property(c => c.IsActive).HasDefaultValue(true, "DF_Counters_IsActive").HasSentinel(true);
        builder.ToTable(t => t.HasCheckConstraint("CK_Counters_Number_Positive", "[Number] BETWEEN 1 AND 50"));
        builder.HasAuditColumns();

        var id = 0;
        builder.HasData(ReferenceData.Branches.SelectMany(b => b.Counters.Select(c => new
        {
            Id = ++id,
            BranchCode = new BranchCode(b.BranchCode),
            Number = c.Id,
            c.Name,
            c.IsActive,
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        })).ToList());
    }
}

public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("Staff");
        builder.HasKey(s => s.Id).HasName("PK_Staff");
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.Email).HasMaxLength(254).IsUnicode(false);
        builder.HasIndex(s => s.Email).IsUnique().HasDatabaseName("UQ_Staff_Email");
        builder.ToTable(t => t.HasCheckConstraint("CK_Staff_Email_Format", "[Email] LIKE '%_@_%'"));
        builder.Property(s => s.PasswordHash).HasMaxLength(200).IsUnicode(false);
        builder.Property(s => s.IsActive).HasDefaultValue(true, "DF_Staff_IsActive").HasSentinel(true);

        // Role is a FK to the Roles lookup table (int key = enum value).
        builder.Property(s => s.Role).HasColumnName("RoleId").HasConversion<int>();
        builder.HasOne<Role>().WithMany().HasForeignKey(s => s.Role)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Staff_Roles");

        builder.HasOne<Branch>().WithMany()
            .HasForeignKey(s => s.HomeBranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Staff_Branches");
        builder.HasIndex(s => s.HomeBranchCode).HasDatabaseName("IX_Staff_HomeBranchCode");
        builder.HasAuditColumns();

        builder.HasData(ReferenceData.Staff.Select(s => new
        {
            s.Id,
            s.Name,
            s.Email,
            Role = Enum.Parse<StaffRole>(s.Role),
            HomeBranchCode = new BranchCode(s.HomeBranchCode),
            s.IsActive,
            s.PasswordHash,
            CreatedAtUtc = ReferenceData.SeededAtUtc,
            CreatedBy = "seed",
        }));
    }
}
