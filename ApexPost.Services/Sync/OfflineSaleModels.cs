using ApexPost.Models.Domain;
using ApexPost.Models.Validation;

namespace ApexPost.Services.Sync;

/// <summary>Application command for one offline sale (mapped explicitly from the wire contract by the web layer).</summary>
public sealed record RecordOfflineSaleCommand
{
    [NotBlank, MaxLengthRule(50)]
    public string DeviceId { get; init; } = string.Empty;

    public Guid LocalTransactionId { get; init; }

    [NotBlank, Matches("^BR-[0-9]{3}$", "in the form BR-000")]
    public string BranchCode { get; init; } = string.Empty;

    [InRange(1, 50)]
    public int CounterNumber { get; init; }

    [InRange(1, int.MaxValue)]
    public int ClerkStaffId { get; init; }

    /// <summary>When the sale happened on the device (the device's clock, UTC).</summary>
    public DateTimeOffset CapturedAt { get; init; }

    [DefinedEnum]
    public PaymentMethod PaymentMethod { get; init; }

    public IReadOnlyList<OfflineSaleLine> Lines { get; init; } = [];

    public IReadOnlyList<OfflineMoneyOrder> MoneyOrders { get; init; } = [];
}

public sealed record OfflineSaleLine
{
    [NotBlank, Matches("^[A-Za-z]{3}-[0-9]{3}$", "a SKU such as PKG-001")]
    public string Sku { get; init; } = string.Empty;

    [InRange(1, 100)]
    public int Quantity { get; init; }

    /// <summary>The tax-exclusive unit price the customer was charged at the counter (offline snapshot price).</summary>
    [InRange(0.01, 100000)]
    public decimal UnitPrice { get; init; }
}

public sealed record OfflineMoneyOrder
{
    [InRange(1, 100000)]
    public decimal Amount { get; init; }

    [NotBlank, MaxLengthRule(80)]
    public string Sender { get; init; } = string.Empty;

    [NotBlank, MaxLengthRule(80)]
    public string Recipient { get; init; } = string.Empty;
}

/// <summary>What the terminal is told for one pushed sale. The same result is returned for a retry.</summary>
public sealed record OfflineSaleResult(
    Guid LocalTransactionId,
    SyncOutcome Outcome,
    int? ServerTransactionId,
    string Message,
    IReadOnlyList<string> Warnings,
    bool WasDuplicate);
