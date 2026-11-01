using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;
using ApexPost.Services.MoneyOrders;

namespace ApexPost.Services.Transactions;

/// <summary>
/// Counter transactions: open a basket, add goods and services, take payment, void or refund.
/// Every mutating method is one use case = one SaveChangesAsync (one database transaction).
/// </summary>
public interface ITransactionService
{
    Task<IReadOnlyList<TransactionSummaryDto>> GetRecentAsync(StaffContext actor, BranchCode? branch, int take, CancellationToken cancellationToken);

    Task<TransactionDetails> GetDetailsAsync(int transactionId, StaffContext actor, CancellationToken cancellationToken);

    Task<TransactionSummaryDto> OpenAsync(StaffContext actor, int counterTerminalId, CancellationToken cancellationToken);

    Task<TransactionSummaryDto> AddRetailItemAsync(int transactionId, AddRetailItemCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task<ShipmentDto> AddShipmentAsync(int transactionId, AddShipmentCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task<MoneyOrderDto> AddMoneyOrderAsync(int transactionId, MoneyOrderCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task<VerificationDto> AddIdentityVerificationAsync(int transactionId, IdentityVerificationCommand command, StaffContext actor, CancellationToken cancellationToken);

    Task<TransactionSummaryDto> RemoveLineAsync(int transactionId, int lineNumber, StaffContext actor, CancellationToken cancellationToken);

    /// <summary>Decrements stock and saves the transaction, lines and payment in ONE SaveChangesAsync.</summary>
    Task<TransactionSummaryDto> PayAsync(int transactionId, PaymentMethod paymentMethod, StaffContext actor, CancellationToken cancellationToken);

    /// <summary>Branch Manager only, reason required.</summary>
    Task<TransactionSummaryDto> VoidAsync(int transactionId, VoidOrRefundCommand command, StaffContext actor, CancellationToken cancellationToken);

    /// <summary>Branch Manager only, reason required, paid transactions only.</summary>
    Task<TransactionSummaryDto> RefundAsync(int transactionId, VoidOrRefundCommand command, StaffContext actor, CancellationToken cancellationToken);
}
