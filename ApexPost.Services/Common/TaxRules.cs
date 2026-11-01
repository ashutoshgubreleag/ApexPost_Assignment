using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Services.Common;

/// <summary>
/// Tax rules as pattern matching: one readable table instead of nested if/else, and the
/// compiler warns if a new enum member is not handled.
/// Prices are tax-exclusive; tax is calculated per line and rounded to cents.
/// </summary>
public static class TaxRules
{
    public static decimal RateFor(TaxCategory category) => category switch
    {
        TaxCategory.Standard => 0.10m,
        TaxCategory.Reduced => 0.05m,
        TaxCategory.Exempt => 0m,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown tax category."),
    };

    /// <summary>Which tax category applies to a basket line.</summary>
    public static TaxCategory CategoryFor(TransactionLineType lineType, TaxCategory? productCategory = null, bool isInternational = false) =>
        (lineType, isInternational) switch
        {
            (TransactionLineType.RetailItem, _) => productCategory ?? TaxCategory.Standard,
            (TransactionLineType.Shipping, true) => TaxCategory.Exempt,      // exported services are zero-rated
            (TransactionLineType.Shipping, false) => TaxCategory.Standard,
            (TransactionLineType.MoneyOrder, _) => TaxCategory.Exempt,       // financial supply
            (TransactionLineType.IdentityVerification, _) => TaxCategory.Standard,
            _ => throw new ArgumentOutOfRangeException(nameof(lineType), lineType, "Unknown line type."),
        };

    public static Money TaxOn(Money net, TaxCategory category) => net.ApplyRate(RateFor(category));
}
