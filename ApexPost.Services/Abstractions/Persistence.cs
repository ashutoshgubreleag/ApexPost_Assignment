using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.Abstractions;

/*
 * Persistence ports owned by the application layer (dependency inversion): ApexPost.Services
 * defines what it needs, ApexPost.Repositories implements it with EF Core 10 and the web project
 * wires the two together. Rules every repository follows:
 *   - one repository per aggregate root, with intention-revealing methods;
 *   - repositories STAGE changes (Add / tracked edits) and never call SaveChanges;
 *   - no IQueryable leaves a repository, so EF Core never leaks into the application layer;
 *   - reads used for screens and reports go through query services (Queries/*), not repositories.
 */

/// <summary>One commit per use case. Repositories, the unit of work and the DbContext share one scope.</summary>
public interface IUnitOfWork
{
    /// <summary>Commits everything staged by the repositories in this scope as one database transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit transaction wrapped in the provider's
    /// execution strategy (retries on transient faults). Use it when a use case mixes
    /// SaveChangesAsync with a stored procedure or ExecuteUpdate, so all of it commits or none does.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Forgets every staged change (used before retrying after a concurrency conflict).</summary>
    void DiscardChanges();
}

public interface IBranchRepository
{
    Task<Models.Domain.Branch?> GetByCodeAsync(BranchCode code, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(BranchCode code, CancellationToken cancellationToken);

    void Add(Models.Domain.Branch branch);
}

public interface IStaffRepository
{
    Task<StaffMember?> GetByIdAsync(int staffId, CancellationToken cancellationToken);

    Task<StaffMember?> GetByEmailAsync(string normalisedEmail, CancellationToken cancellationToken);

    Task<bool> EmailInUseAsync(string normalisedEmail, int exceptStaffId, CancellationToken cancellationToken);

    void Add(StaffMember staff);
}

public interface IProductRepository
{
    Task<Product?> GetBySkuAsync(Sku sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetBySkusAsync(IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken);

    /// <summary>
    /// Set-based price revision (one UPDATE statement, no entities loaded). Returns the rows changed.
    /// Executes immediately, so callers run it inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>.
    /// </summary>
    Task<int> RevisePricesAsync(ProductCategory category, decimal factor, CancellationToken cancellationToken);
}

public interface IStockRepository
{
    /// <summary>Tracked stock rows for the given SKUs at a branch (rowversion-protected on save).</summary>
    Task<IReadOnlyList<BranchStock>> GetForUpdateAsync(BranchCode branch, IReadOnlyCollection<Sku> skus, CancellationToken cancellationToken);

    Task<int> GetQuantityAsync(BranchCode branch, Sku sku, CancellationToken cancellationToken);

    void Add(BranchStock stock);
}

/// <summary>Calls the transactional stock-adjustment stored procedure (it also writes the history row).</summary>
public interface IStockAdjustmentProcedure
{
    /// <summary>Returns the new quantity on hand. Throws a business rule violation if stock would go negative.</summary>
    Task<int> AdjustAsync(BranchCode branch, Sku sku, int delta, string reason, string changedBy, CancellationToken cancellationToken);
}

public interface ICounterTransactionRepository
{
    /// <summary>The whole aggregate (lines and payments), tracked for update.</summary>
    Task<CounterTransaction?> GetAsync(int transactionId, CancellationToken cancellationToken);

    void Add(CounterTransaction transaction);
}

public interface IShipmentRepository
{
    Task<IReadOnlyList<Shipment>> GetByTrackingNumbersAsync(IReadOnlyCollection<string> trackingNumbers, CancellationToken cancellationToken);

    void Add(Shipment shipment);
}

public interface IShippingZoneRepository
{
    Task<ShippingZone?> GetByCodeAsync(string zoneCode, CancellationToken cancellationToken);
}

public interface IMoneyOrderRepository
{
    Task<MoneyOrder?> GetByIdAsync(int moneyOrderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MoneyOrder>> GetBySerialNumbersAsync(IReadOnlyCollection<string> serialNumbers, CancellationToken cancellationToken);

    void Add(MoneyOrder moneyOrder);
}

/// <summary>
/// Identity verifications use a separate persistence model (masked storage); the repository maps
/// between the domain object and that record, so callers never see the storage shape.
/// </summary>
public interface IIdentityVerificationRepository
{
    Task<IReadOnlyList<IdentityVerification>> GetByReferencesAsync(IReadOnlyCollection<string> references, CancellationToken cancellationToken);

    void Add(IdentityVerification verification);
}

public interface INotificationRepository
{
    void Add(Notification notification);
}

public interface ISyncInboxRepository
{
    Task<SyncInboxEntry?> FindAsync(string deviceId, Guid localTransactionId, CancellationToken cancellationToken);

    void Add(SyncInboxEntry entry);
}

/// <summary>
/// A UNIQUE constraint or unique index rejected the change (SQL Server errors 2601/2627): for
/// example two requests creating the same staff e-mail at the same moment, or the same offline sale
/// pushed twice. Raised by the persistence layer instead of the SqlException, so nothing above it
/// depends on SqlClient. It is a <see cref="DomainException"/>, so the web layer answers
/// <b>409 Conflict</b> (API) or shows the message on the form, never a 500. The message names the
/// constraint only, never the duplicate value (which may be personal data such as an e-mail).
/// The sync path catches it and returns the earlier result instead (idempotent push).
/// </summary>
public sealed class DuplicateKeyException : DomainException
{
    public DuplicateKeyException(string constraintName, Exception? innerException = null)
        : base($"A record with the same unique value already exists ({constraintName}). Nothing was saved; reload and try again.")
    {
        ConstraintName = constraintName;
        Cause = innerException;
    }

    /// <summary>Name of the violated constraint or index, e.g. <c>UQ_Staff_Email</c>.</summary>
    public string ConstraintName { get; }

    /// <summary>The provider exception, kept for logging (not shown to users).</summary>
    public Exception? Cause { get; }
}
