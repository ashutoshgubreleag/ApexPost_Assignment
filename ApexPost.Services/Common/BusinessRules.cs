using Microsoft.Extensions.Options;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.Common;

/// <summary>
/// Configurable business rules expressed as delegates. Services ask the rule
/// (<c>rules.RequiresIdentityVerification(amount)</c>) instead of hard-coding the number, so the
/// threshold comes from configuration and tests can swap in a different rule without
/// subclassing anything.
/// </summary>
public sealed class BusinessRules
{
    public required Predicate<Money> RequiresIdentityVerification { get; init; }

    public required Predicate<Money> IsHighValueMoneyOrder { get; init; }

    public required Func<Money, Money> MoneyOrderFee { get; init; }

    public required Money MaximumMoneyOrderAmount { get; init; }

    public required Money IdentityVerificationFee { get; init; }

    /// <summary>Low-stock rule: (quantity on hand, item reorder threshold, report threshold) → is it low?</summary>
    public required Func<int, int, int, bool> IsLowStock { get; init; }

    /// <summary>Money orders at or above this amount are "high value" (notification, audit report).</summary>
    public required Money HighValueMoneyOrderAmount { get; init; }

    /// <summary>How many times a use case is retried after an optimistic concurrency conflict.</summary>
    public required int ConcurrencyRetryAttempts { get; init; }

    /// <summary>Offline sale price differing from the server price by more than this fraction is flagged.</summary>
    public required decimal OfflinePriceVarianceTolerance { get; init; }

    public required int DefaultLowStockThreshold { get; init; }

    public static BusinessRules FromOptions(BusinessRulesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var idThreshold = new Money(options.MoneyOrderIdentityThreshold);
        var highValue = new Money(options.HighValueMoneyOrderThreshold);
        var minimumFee = new Money(options.MinimumMoneyOrderFee);

        return new BusinessRules
        {
            RequiresIdentityVerification = amount => amount > idThreshold,
            IsHighValueMoneyOrder = amount => amount >= highValue,
            MoneyOrderFee = amount => Money.Max(minimumFee, amount.ApplyRate(options.MoneyOrderFeeRate)),
            MaximumMoneyOrderAmount = new Money(options.MaximumMoneyOrderAmount),
            IdentityVerificationFee = new Money(options.IdentityVerificationFee),
            IsLowStock = (quantity, reorderThreshold, threshold) => quantity <= Math.Max(reorderThreshold, threshold),
            HighValueMoneyOrderAmount = highValue,
            ConcurrencyRetryAttempts = Math.Clamp(options.ConcurrencyRetryAttempts, 1, 100),
            OfflinePriceVarianceTolerance = options.OfflinePriceVarianceTolerance,
            DefaultLowStockThreshold = options.LowStockThreshold,
        };
    }

    public static BusinessRules FromOptions(IOptions<BusinessRulesOptions> options) =>
        FromOptions(options?.Value ?? throw new ArgumentNullException(nameof(options)));
}
