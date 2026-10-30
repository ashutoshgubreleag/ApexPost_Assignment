using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;

namespace ApexPost.Services.MoneyOrders;

/// <summary>
/// Money order pricing and listing. Creating, verifying and issuing money orders are steps of the
/// counter transaction use cases (TransactionService), so they commit with the basket.
/// </summary>
public sealed class MoneyOrderService(ITransactionQueries queries, BusinessRules rules) : IMoneyOrderService
{
    public Money CalculateFee(Money amount) => rules.MoneyOrderFee(amount);

    public Task<IReadOnlyList<MoneyOrderDto>> GetForBranchAsync(BranchCode? branch, CancellationToken cancellationToken) =>
        queries.ListMoneyOrdersAsync(branch, 500, cancellationToken);
}
