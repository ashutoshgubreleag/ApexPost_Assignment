using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>
/// A counter transaction (the basket, or "sale"). Aggregate root for its lines and payments: lines
/// can only be added through <see cref="AddRetailItem"/> / <see cref="AddServiceLine"/>, totals are
/// always recalculated from the lines, and status changes go through Pay / Void / Refund so the
/// rules cannot be bypassed by setting a property.
/// </summary>
public sealed class CounterTransaction : Entity
{
    private readonly List<TransactionLine> _lines = [];
    private readonly List<Payment> _payments = [];

    private CounterTransaction()
    {
        // For EF Core materialisation.
    }

    public BranchCode BranchCode { get; private set; }

    public int CounterNumber { get; private set; }

    public int ClerkStaffId { get; private set; }

    public TransactionSource Source { get; private set; } = TransactionSource.Counter;

    public TransactionStatus Status { get; private set; } = TransactionStatus.Open;

    /// <summary>Tax-exclusive revenue (retail + service fees).</summary>
    public Money Subtotal { get; private set; }

    public Money TaxAmount { get; private set; }

    /// <summary>Money order face values collected on behalf of recipients.</summary>
    public Money PassThroughAmount { get; private set; }

    /// <summary>Amount the customer pays: subtotal + tax + pass-through.</summary>
    public Money TotalAmount { get; private set; }

    /// <summary>Mandatory reason for a void or refund.</summary>
    public string? VoidReason { get; private set; }

    public int? AuthorisedByStaffId { get; private set; }

    /// <summary>When the basket was opened (for offline sales: the device's clock).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<TransactionLine> LineItems => _lines.AsReadOnly();

    public IReadOnlyList<Payment> Payments => _payments.AsReadOnly();

    public PaymentMethod? PaymentMethod => _payments.Count == 0 ? null : _payments[^1].Method;

    public bool IsOpen => Status == TransactionStatus.Open;

    public int ItemCount => _lines.Sum(l => l.Quantity);

    public Money RetailNet => _lines.Where(l => l.LineType == TransactionLineType.RetailItem).Select(l => l.LineTotal).Total();

    public Money ServicesNet => _lines.Where(l => l.LineType != TransactionLineType.RetailItem).Select(l => l.LineTotal).Total();

    public bool HasMoneyOrders => _lines.Any(l => l.LineType == TransactionLineType.MoneyOrder);

    public static CounterTransaction Open(BranchCode branch, int counterNumber, int clerkStaffId, DateTimeOffset at, TransactionSource source = TransactionSource.Counter) =>
        new()
        {
            BranchCode = branch,
            CounterNumber = counterNumber,
            ClerkStaffId = clerkStaffId,
            Source = source,
            Status = TransactionStatus.Open,
            CreatedAt = at,
        };

    /// <summary>Adds (or tops up) a retail line. Quantity merges for the same SKU.</summary>
    public TransactionLine AddRetailItem(Sku sku, string description, Money unitPrice, TaxCategory taxCategory, int quantity, Func<Money, TaxCategory, Money> taxOn)
    {
        ArgumentNullException.ThrowIfNull(taxOn);
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        var line = _lines.FirstOrDefault(l => l.LineType == TransactionLineType.RetailItem && l.ItemCode == sku.Value);
        if (line is null)
        {
            line = new TransactionLine(NextLineNumber, TransactionLineType.RetailItem, sku.Value, description, unitPrice, taxCategory);
            _lines.Add(line);
        }

        line.SetQuantity(line.Quantity + quantity, taxOn);
        RecalculateTotals();
        return line;
    }

    /// <summary>The quantity of a SKU already in the basket (used for the friendly stock check).</summary>
    public int QuantityOf(Sku sku) =>
        _lines.Where(l => l.LineType == TransactionLineType.RetailItem && l.ItemCode == sku.Value).Sum(l => l.Quantity);

    /// <summary>Adds a single-quantity service line (shipping, money order fee, identity check).</summary>
    public TransactionLine AddServiceLine(TransactionLineType type, string itemCode, string description, Money net, TaxCategory taxCategory, Money tax, Money passThrough)
    {
        EnsureOpen();
        if (type == TransactionLineType.RetailItem)
        {
            throw new ArgumentException("Use AddRetailItem for retail lines.", nameof(type));
        }

        var line = TransactionLine.Service(NextLineNumber, type, itemCode, description, net, taxCategory, tax, passThrough);
        _lines.Add(line);
        RecalculateTotals();
        return line;
    }

    public TransactionLine RemoveLine(int lineNumber)
    {
        EnsureOpen();
        var line = _lines.FirstOrDefault(l => l.LineNumber == lineNumber)
            ?? throw new EntityNotFoundException("Transaction line", lineNumber);
        _lines.Remove(line);
        RecalculateTotals();
        return line;
    }

    /// <summary>Takes payment for the whole basket and closes it.</summary>
    public Payment Pay(PaymentMethod method, DateTimeOffset at)
    {
        EnsureOpen();
        Guard.Defined(method, nameof(method));
        if (_lines.Count == 0)
        {
            throw new BusinessRuleViolationException("BASKET_EMPTY", "Add at least one item or service before taking payment.");
        }

        var payment = new Payment(method, TotalAmount, at);
        _payments.Add(payment);
        Status = TransactionStatus.Paid;
        ClosedAt = at;
        return payment;
    }

    /// <summary>Rule: voids need a branch manager and a reason; open or paid baskets only.</summary>
    public void Void(string reason, int managerStaffId, DateTimeOffset at) => Reverse(reason, managerStaffId, at, isRefund: false);

    /// <summary>Rule: refunds need a branch manager and a reason; paid baskets only.</summary>
    public void Refund(string reason, int managerStaffId, DateTimeOffset at) => Reverse(reason, managerStaffId, at, isRefund: true);

    public static bool CanReverse(TransactionStatus status, bool isRefund) => (isRefund, status) switch
    {
        (false, TransactionStatus.Open or TransactionStatus.Paid) => true,
        (true, TransactionStatus.Paid) => true,
        _ => false,
    };

    public IEnumerable<(Sku Sku, int Quantity)> RetailQuantities() =>
        _lines.Where(l => l.LineType == TransactionLineType.RetailItem)
            .GroupBy(l => l.ItemCode)
            .Select(g => (new Sku(g.Key), g.Sum(l => l.Quantity)));

    public IEnumerable<string> ItemCodes(TransactionLineType type) =>
        _lines.Where(l => l.LineType == type).Select(l => l.ItemCode);

    public void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException("TRANSACTION_NOT_OPEN", $"Transaction {Id} is {Status} and can no longer be changed.");
        }
    }

    private void Reverse(string reason, int managerStaffId, DateTimeOffset at, bool isRefund)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
        {
            throw new CommandValidationException(nameof(reason), "A reason of at least 5 characters is required.");
        }

        if (!CanReverse(Status, isRefund))
        {
            throw new BusinessRuleViolationException("INVALID_STATUS", $"A {Status} transaction cannot be {(isRefund ? "refunded" : "voided")}.");
        }

        Status = isRefund ? TransactionStatus.Refunded : TransactionStatus.Voided;
        VoidReason = Guard.Text(reason, nameof(reason), 200);
        AuthorisedByStaffId = managerStaffId;
        ClosedAt ??= at;
    }

    private int NextLineNumber => _lines.Count == 0 ? 1 : _lines.Max(l => l.LineNumber) + 1;

    private void RecalculateTotals()
    {
        Subtotal = _lines.Select(l => l.LineTotal).Total();
        TaxAmount = _lines.Select(l => l.TaxAmount).Total();
        PassThroughAmount = _lines.Select(l => l.PassThroughAmount).Total();
        TotalAmount = Subtotal + TaxAmount + PassThroughAmount;
    }
}

/// <summary>
/// One line of a basket. Composition over inheritance: every line has the same shape and
/// <see cref="LineType"/> + <see cref="ItemCode"/> (SKU, tracking number, money order serial or
/// verification reference) identify what is behind it.
/// </summary>
public sealed class TransactionLine
{
    private TransactionLine()
    {
        // For EF Core materialisation.
    }

    internal TransactionLine(int lineNumber, TransactionLineType type, string itemCode, string description, Money unitPrice, TaxCategory taxCategory)
    {
        LineNumber = lineNumber;
        LineType = type;
        ItemCode = Guard.Text(itemCode, nameof(itemCode), 30);
        Description = Guard.Text(description, nameof(description), 200);
        UnitPrice = unitPrice;
        TaxCategory = taxCategory;
    }

    public int TransactionId { get; private set; }

    /// <summary>Line number within the transaction (1, 2, 3...).</summary>
    public int LineNumber { get; private set; }

    public TransactionLineType LineType { get; private set; }

    public string ItemCode { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; }

    public TaxCategory TaxCategory { get; private set; }

    public Money TaxAmount { get; private set; }

    /// <summary>Tax-exclusive net amount (UnitPrice x Quantity).</summary>
    public Money LineTotal { get; private set; }

    /// <summary>Non-revenue money collected by this line (money order face value).</summary>
    public Money PassThroughAmount { get; private set; }

    internal static TransactionLine Service(int lineNumber, TransactionLineType type, string itemCode, string description, Money net, TaxCategory taxCategory, Money tax, Money passThrough) =>
        new(lineNumber, type, itemCode, description, net, taxCategory)
        {
            Quantity = 1,
            LineTotal = net,
            TaxAmount = tax,
            PassThroughAmount = passThrough,
        };

    internal void SetQuantity(int quantity, Func<Money, TaxCategory, Money> taxOn)
    {
        Quantity = quantity;
        LineTotal = (UnitPrice * quantity).RoundedToCents;
        TaxAmount = taxOn(LineTotal, TaxCategory);
    }
}

/// <summary>A payment taken for a transaction.</summary>
public sealed class Payment : Entity
{
    private Payment()
    {
        // For EF Core materialisation.
    }

    internal Payment(PaymentMethod method, Money amount, DateTimeOffset at)
    {
        Method = method;
        Amount = amount;
        PaidAt = at;
    }

    public int TransactionId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public Money Amount { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }
}
