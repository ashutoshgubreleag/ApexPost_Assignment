using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.MoneyOrders;

public sealed record MoneyOrderCommand
{
    [InRange(1, 100000)]
    public decimal Amount { get; init; }

    [NotBlank, MaxLengthRule(80)]
    public string Sender { get; init; } = string.Empty;

    [NotBlank, MaxLengthRule(80)]
    public string Recipient { get; init; } = string.Empty;
}

public sealed record IdentityVerificationCommand
{
    [NotBlank, MaxLengthRule(80)]
    public string CustomerName { get; init; } = string.Empty;

    [DefinedEnum]
    public DocumentType DocumentType { get; init; }

    /// <summary>
    /// Full document number as typed by the clerk. It is masked immediately in the service and
    /// never stored, logged or audited (the audit filter only records whitelisted fields).
    /// </summary>
    [NotBlank, MaxLengthRule(40), Matches("^[A-Za-z0-9 -]{5,40}$", "5-40 letters or digits")]
    public string DocumentNumber { get; init; } = string.Empty;

    [DefinedEnum]
    public VerificationResult Result { get; init; }
}

public interface IMoneyOrderService
{
    Money CalculateFee(Money amount);

    Task<IReadOnlyList<MoneyOrderDto>> GetForBranchAsync(BranchCode? branch, CancellationToken cancellationToken);
}
