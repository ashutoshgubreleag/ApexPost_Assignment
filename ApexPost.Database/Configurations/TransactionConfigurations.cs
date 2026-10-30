using ApexPost.Database.Conventions;
using ApexPost.Database.Records;
using ApexPost.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Database.Configurations;

public sealed class CounterTransactionConfiguration : IEntityTypeConfiguration<CounterTransaction>
{
    public void Configure(EntityTypeBuilder<CounterTransaction> builder)
    {
        builder.ToTable("CounterTransactions", t =>
        {
            t.HasCheckConstraint("CK_CounterTransactions_Amounts_NonNegative", "[Subtotal] >= 0 AND [TaxAmount] >= 0 AND [PassThroughAmount] >= 0 AND [TotalAmount] >= 0");
            t.HasCheckConstraint("CK_CounterTransactions_Total", "[TotalAmount] = [Subtotal] + [TaxAmount] + [PassThroughAmount]");
            t.HasCheckConstraint("CK_CounterTransactions_Closed_HasDate", "[Status] = 'Open' OR [ClosedAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_CounterTransactions_Reversal_HasReason", "[Status] NOT IN ('Voided', 'Refunded') OR ([VoidReason] IS NOT NULL AND [AuthorisedByStaffId] IS NOT NULL)");
        });
        builder.HasKey(t => t.Id).HasName("PK_CounterTransactions");
        builder.HasEnumText(t => t.Status, "Status", 10).HasDefaultValue(TransactionStatus.Open, "DF_CounterTransactions_Status").HasSentinel(TransactionStatus.Open);
        builder.HasEnumText(t => t.Source, "Source", 16).HasDefaultValue(TransactionSource.Counter, "DF_CounterTransactions_Source").HasSentinel(TransactionSource.Counter);
        builder.Property(t => t.VoidReason).HasMaxLength(200);
        builder.Property(t => t.CreatedAt).HasColumnName("OpenedAtUtc");
        builder.Property(t => t.ClosedAt).HasColumnName("ClosedAtUtc");

        // Business date of the sale, computed and PERSISTED so it can be indexed: reports filter
        // "BusinessDate = @date" (SARGable) instead of CONVERT/YEAR on the datetime column.
        builder.Property<DateOnly?>("BusinessDate").HasColumnType("date")
            .HasComputedColumnSql("CONVERT(date, [ClosedAtUtc])", stored: true);

        builder.Ignore(t => t.PaymentMethod);
        builder.Ignore(t => t.IsOpen);
        builder.Ignore(t => t.ItemCount);
        builder.Ignore(t => t.RetailNet);
        builder.Ignore(t => t.ServicesNet);
        builder.Ignore(t => t.HasMoneyOrders);

        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CounterTransactions_Branches");
        builder.HasOne<Counter>().WithMany()
            .HasForeignKey(t => new { t.BranchCode, t.CounterNumber })
            .HasPrincipalKey(c => new { c.BranchCode, c.Number })
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CounterTransactions_Counters");
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(t => t.ClerkStaffId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CounterTransactions_Staff_Clerk");
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(t => t.AuthorisedByStaffId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CounterTransactions_Staff_AuthorisedBy");
        builder.HasIndex(t => new { t.BranchCode, t.CounterNumber }).HasDatabaseName("IX_CounterTransactions_Branch_Counter");
        builder.HasIndex(t => t.ClerkStaffId).HasDatabaseName("IX_CounterTransactions_ClerkStaffId");
        builder.HasIndex(t => t.AuthorisedByStaffId).HasDatabaseName("IX_CounterTransactions_AuthorisedByStaffId");

        // Lines and payments are part of the aggregate: loaded with it, deleted with it.
        builder.HasMany(t => t.LineItems).WithOne().HasForeignKey(l => l.TransactionId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_TransactionLines_CounterTransactions");
        builder.Navigation(t => t.LineItems).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(t => t.Payments).WithOne().HasForeignKey(p => p.TransactionId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_Payments_CounterTransactions");
        builder.Navigation(t => t.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasRowVersion();
        builder.HasAuditColumns();
    }
}

public sealed class TransactionLineConfiguration : IEntityTypeConfiguration<TransactionLine>
{
    public void Configure(EntityTypeBuilder<TransactionLine> builder)
    {
        builder.ToTable("TransactionLines", t =>
        {
            t.HasCheckConstraint("CK_TransactionLines_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_TransactionLines_Amounts_NonNegative", "[UnitPrice] >= 0 AND [TaxAmount] >= 0 AND [LineTotal] >= 0 AND [PassThroughAmount] >= 0");
        });

        // Natural composite key: the line number is only meaningful inside its transaction.
        builder.HasKey(l => new { l.TransactionId, l.LineNumber }).HasName("PK_TransactionLines");
        builder.Property(l => l.LineNumber).ValueGeneratedNever();
        builder.HasEnumText(l => l.LineType, "LineType", 24);
        builder.HasEnumText(l => l.TaxCategory, "TaxCategory", 10);
        builder.Property(l => l.ItemCode).HasMaxLength(30).IsUnicode(false);
        builder.Property(l => l.Description).HasMaxLength(200);

        // Supports "retail vs services" splits and "which baskets sold SKU x" without scanning lines.
        builder.HasIndex(l => new { l.LineType, l.ItemCode }).HasDatabaseName("IX_TransactionLines_LineType_ItemCode");
        builder.HasAuditColumns();
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0"));
        builder.HasKey(p => p.Id).HasName("PK_Payments");
        builder.HasEnumText(p => p.Method, "Method", 12);
        builder.Property(p => p.PaidAt).HasColumnName("PaidAtUtc");
        builder.HasIndex(p => p.TransactionId).HasDatabaseName("IX_Payments_TransactionId");
        builder.HasAuditColumns();
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id).HasName("PK_Notifications");
        builder.HasEnumText(n => n.Type, "Type", 24);
        builder.Property(n => n.Recipient).HasMaxLength(254).IsUnicode(false);
        builder.Property(n => n.Message).HasMaxLength(500);
        builder.Property(n => n.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.Property(n => n.CreatedAt).HasColumnName("RaisedAtUtc");
        builder.HasOne<Branch>().WithMany().HasForeignKey(n => n.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Notifications_Branches");
        builder.HasIndex(n => new { n.BranchCode, n.CreatedAt }).HasDatabaseName("IX_Notifications_Branch_RaisedAt");
        builder.HasAuditColumns();
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntryRecord>
{
    public void Configure(EntityTypeBuilder<AuditEntryRecord> builder)
    {
        builder.ToTable("AuditEntries", t => t.HasCheckConstraint("CK_AuditEntries_Outcome", "[Outcome] IN ('Succeeded', 'Rejected', 'Failed')"));

        // Append-only log: Guid identity (generated by the filter) but clustered on time, so inserts
        // go to the end of the table and "latest N entries" is a backward range scan.
        builder.HasKey(a => a.Id).HasName("PK_AuditEntries").IsClustered(false);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.TimestampUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(a => a.TimestampUtc).IsClustered().HasDatabaseName("CIX_AuditEntries_TimestampUtc");
        builder.Property(a => a.Action).HasMaxLength(60).IsUnicode(false);
        builder.Property(a => a.StaffId).HasMaxLength(10).IsUnicode(false);
        builder.Property(a => a.Role).HasMaxLength(30).IsUnicode(false);
        builder.Property(a => a.BranchCode).HasMaxLength(10).IsUnicode(false);
        builder.Property(a => a.Controller).HasMaxLength(60).IsUnicode(false);
        builder.Property(a => a.ControllerAction).HasMaxLength(60).IsUnicode(false);
        builder.Property(a => a.HttpMethod).HasMaxLength(10).IsUnicode(false);
        builder.Property(a => a.Path).HasMaxLength(400);
        builder.Property(a => a.RouteValuesJson).HasMaxLength(2000);
        builder.Property(a => a.FieldsJson).HasMaxLength(4000);
        builder.Property(a => a.Outcome).HasMaxLength(10).IsUnicode(false);
        builder.Property(a => a.Error).HasMaxLength(1000);
        builder.Property(a => a.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(a => new { a.BranchCode, a.TimestampUtc }).HasDatabaseName("IX_AuditEntries_Branch_Timestamp");
    }
}

public sealed class SyncInboxConfiguration : IEntityTypeConfiguration<SyncInboxEntry>
{
    public void Configure(EntityTypeBuilder<SyncInboxEntry> builder)
    {
        builder.ToTable("SyncInbox");
        builder.HasKey(s => s.Id).HasName("PK_SyncInbox");
        builder.Property(s => s.DeviceId).HasMaxLength(50).IsUnicode(false);

        // Idempotency: the same offline sale can be pushed any number of times; only one row can exist.
        builder.HasIndex(s => new { s.DeviceId, s.LocalTransactionId }).IsUnique().HasDatabaseName("UQ_SyncInbox_Device_LocalTransaction");
        builder.HasEnumText(s => s.Outcome, "Outcome", 24);
        builder.Property(s => s.Message).HasMaxLength(400);
        builder.Property(s => s.ReceivedAt).HasColumnName("ReceivedAtUtc");

        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchCode).HasPrincipalKey(b => b.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SyncInbox_Branches");
        builder.HasOne(s => s.LinkedTransaction).WithMany().HasForeignKey(s => s.ServerTransactionId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SyncInbox_CounterTransactions");
        builder.HasIndex(s => s.ServerTransactionId).HasDatabaseName("IX_SyncInbox_ServerTransactionId");
        builder.HasIndex(s => s.BranchCode).HasDatabaseName("IX_SyncInbox_BranchCode");

        builder.HasMany(s => s.Conflicts).WithOne().HasForeignKey(c => c.SyncInboxEntryId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_SyncConflicts_SyncInbox");
        builder.Navigation(s => s.Conflicts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasAuditColumns();
    }
}

public sealed class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("SyncConflicts");
        builder.HasKey(c => c.Id).HasName("PK_SyncConflicts");
        builder.HasEnumText(c => c.Type, "Type", 16);
        builder.HasEnumText(c => c.Status, "Status", 10);
        builder.Property(c => c.ItemCode).HasMaxLength(30).IsUnicode(false);
        builder.Property(c => c.Details).HasMaxLength(400);
        builder.Property(c => c.RaisedAt).HasColumnName("RaisedAtUtc");
        builder.HasIndex(c => c.SyncInboxEntryId).HasDatabaseName("IX_SyncConflicts_SyncInboxEntryId");
        builder.HasIndex(c => c.Status).HasFilter("[Status] = 'Open'").HasDatabaseName("IX_SyncConflicts_Open");
        builder.HasAuditColumns();
    }
}
