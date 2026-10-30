using ApexPost.Database.Configurations;
using ApexPost.Database.Conventions;
using ApexPost.Database.Records;
using ApexPost.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Database;

/// <summary>
/// The SQL Server system of record. Registered as a scoped service (one instance per request,
/// shared by every repository, query service and the unit of work in that request). Mapping lives
/// in one <c>IEntityTypeConfiguration</c> per entity (Configurations folder); this class only
/// declares the sets and the model-wide conventions.
/// </summary>
public sealed class ApexPostDbContext(DbContextOptions<ApexPostDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Category> ProductCategories => Set<Category>();

    public DbSet<StaffMember> Staff => Set<StaffMember>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<BranchStock> BranchStock => Set<BranchStock>();

    public DbSet<StockAdjustmentRecord> StockAdjustments => Set<StockAdjustmentRecord>();

    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();

    public DbSet<ShippingRate> ShippingRates => Set<ShippingRate>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<MoneyOrder> MoneyOrders => Set<MoneyOrder>();

    public DbSet<IdentityVerificationRecord> IdentityVerifications => Set<IdentityVerificationRecord>();

    public DbSet<CounterTransaction> CounterTransactions => Set<CounterTransaction>();

    public DbSet<TransactionLine> TransactionLines => Set<TransactionLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditEntryRecord> AuditEntries => Set<AuditEntryRecord>();

    public DbSet<SyncInboxEntry> SyncInbox => Set<SyncInboxEntry>();

    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ApexPostConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApexPostDbContext).Assembly);
        ReportingIndexes.Apply(modelBuilder);
    }
}
