using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Domain;

/// <summary>Business rules the terminal enforces while it may be offline.</summary>
public static class TerminalRules
{
    /// <summary>Rule: stock cannot go negative against the local snapshot (minus unsynced local sales).</summary>
    public static void EnsureStockAvailable(StockLevel? stock, Sku sku, int quantity)
    {
        var available = stock?.Available ?? 0;
        if (quantity > available)
        {
            throw new BusinessRuleViolationException("STOCK_INSUFFICIENT", $"Only {available} x {sku} available at this counter.");
        }
    }

    /// <summary>
    /// Rule (ADR-W3-10): money orders above the identity threshold need a passed identity
    /// verification recorded online in the same transaction (anti-money-laundering control). The
    /// terminal's outbox carries no verification, so such money orders are refused while offline;
    /// online they must be processed at a full counter where the check is recorded.
    /// </summary>
    public static void EnsureMoneyOrderAllowed(Money amount, Money identityThreshold, bool isOnline)
    {
        if (amount <= Money.Zero)
        {
            throw new BusinessRuleViolationException("MONEY_ORDER_AMOUNT", "A money order must be for more than zero.");
        }

        if (amount > identityThreshold)
        {
            throw isOnline
                ? new BusinessRuleViolationException("MONEY_ORDER_ID_REQUIRED", $"Money orders above {identityThreshold} need an identity verification: process this one at a full online counter.")
                : new BusinessRuleViolationException("MONEY_ORDER_BLOCKED_OFFLINE", $"Money orders above {identityThreshold} cannot be sold while the counter is offline (identity verification needs the network).");
        }
    }
}
