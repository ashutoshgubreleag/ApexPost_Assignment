using ApexPost.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Database.Configurations;

/// <summary>
/// Indexes added by the AddReportingIndexes migration, each chosen from a measured workload
/// (PERFORMANCE.md has the before/after plans, logical reads and CPU time, and ADR-W3-02 the
/// key-order and INCLUDE reasoning). Kept apart from the entity configurations so the follow-up
/// migration is a reviewable, self-contained change.
/// </summary>
public static class ReportingIndexes
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // W1 low stock by branch: seek on BranchCode; every column the report reads is in the
        // index, so no key lookups into the clustered index.
        modelBuilder.Entity<BranchStock>()
            .HasIndex(s => s.BranchCode)
            .IncludeProperties(s => new { s.Sku, s.QuantityOnHand, s.ReorderThreshold })
            .HasDatabaseName("IX_BranchStock_Branch_Covering");

        // W2 daily sales by branch and date: equality on BranchCode then BusinessDate (both
        // equality predicates; BranchCode first because every report is scoped to a branch and
        // it is the more selective of the two in a multi-branch network). Covers the header totals.
        modelBuilder.Entity<CounterTransaction>()
            .HasIndex(["BranchCode", "BusinessDate"], "IX_CounterTransactions_Branch_BusinessDate")
            .IncludeProperties("Status", "Subtotal", "TaxAmount", "PassThroughAmount", "TotalAmount");

        // W2b yearly / date-range sales (the SARGable rewrite demo): a range seek on ClosedAtUtc.
        // Filtered to Paid rows because only paid sales are reported this way.
        modelBuilder.Entity<CounterTransaction>()
            .HasIndex(t => t.ClosedAt, "IX_CounterTransactions_ClosedAt_Paid")
            .HasFilter("[Status] = 'Paid'")
            .IncludeProperties(t => new { t.BranchCode, t.TotalAmount });

        // W3 shipment search by zone and service level, newest first.
        modelBuilder.Entity<Shipment>()
            .HasIndex(s => new { s.ZoneCode, s.ServiceLevel, s.CreatedAt }, "IX_Shipments_Zone_Level_QuotedAt")
            .IsDescending(false, false, true)
            .IncludeProperties(s => new { s.Status, s.Weight, s.Price, s.BranchCode });

        // W4 money-order search for auditors: only issued/cancelled orders are audited, so a
        // FILTERED index leaves the short-lived Pending rows out (smaller, cheaper to maintain).
        modelBuilder.Entity<MoneyOrder>()
            .HasIndex(m => new { m.BranchCode, m.CreatedAt }, "IX_MoneyOrders_Branch_RequestedAt_Audited")
            .HasFilter("[Status] IN ('Issued', 'Cancelled')")
            .IncludeProperties(m => new { m.Amount, m.Fee, m.Status, m.SerialNumber, m.IdentityVerificationId });
    }
}
