using System.Globalization;
using ApexPost.Models.Exceptions;
using ApexPost.Models.ValueObjects;

namespace ApexPost.Models.Domain;

/// <summary>A money order sold at the counter. The face value is pass-through money, the fee is revenue.</summary>
public sealed class MoneyOrder : Entity
{
    private MoneyOrder()
    {
        // For EF Core materialisation.
    }

    public string SerialNumber { get; private set; } = string.Empty;

    public int TransactionId { get; private set; }

    public BranchCode BranchCode { get; private set; }

    /// <summary>Face value paid to the recipient (collected, but not ApexPost revenue).</summary>
    public Money Amount { get; private set; }

    /// <summary>ApexPost's fee (revenue).</summary>
    public Money Fee { get; private set; }

    public string Sender { get; private set; } = string.Empty;

    public string Recipient { get; private set; } = string.Empty;

    public int? IdentityVerificationId { get; private set; }

    public MoneyOrderStatus Status { get; private set; } = MoneyOrderStatus.Pending;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>The basket this money order belongs to (lets a new basket and its money order commit together).</summary>
    public CounterTransaction? Transaction { get; private set; }

    public static MoneyOrder CreatePending(CounterTransaction transaction, Money amount, Money fee, Money maximum, string sender, string recipient, DateTimeOffset at, int serialSuffix)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var branch = transaction.BranchCode;
        amount = amount.RoundedToCents;
        if (amount <= Money.Zero)
        {
            throw new BusinessRuleViolationException("MONEY_ORDER_AMOUNT", "A money order must be for more than zero.");
        }

        if (amount > maximum)
        {
            throw new BusinessRuleViolationException("MONEY_ORDER_LIMIT", $"Money orders are limited to {maximum}.");
        }

        return new MoneyOrder
        {
            SerialNumber = string.Create(CultureInfo.InvariantCulture, $"MO-{branch.Value[3..]}-{at:yyMMdd}-{serialSuffix:0000}"),
            TransactionId = transaction.Id,
            Transaction = transaction,
            BranchCode = branch,
            Amount = amount,
            Fee = fee,
            Sender = Guard.Text(sender, nameof(sender), 80),
            Recipient = Guard.Text(recipient, nameof(recipient), 80),
            Status = MoneyOrderStatus.Pending,
            CreatedAt = at,
        };
    }

    /// <summary>
    /// Rule: a money order above the identity threshold can only be issued against a passed
    /// verification recorded in the same transaction.
    /// </summary>
    public void RequireVerification(IdentityVerification? verification)
    {
        if (verification is null || verification.Result != VerificationResult.Passed || verification.TransactionId != TransactionId)
        {
            throw new BusinessRuleViolationException(
                "MONEY_ORDER_ID_REQUIRED",
                "This transaction contains a money order above the identity threshold. Record a passed identity verification before taking payment.");
        }

        IdentityVerificationId = verification.Id;
    }

    public void Issue(DateTimeOffset at)
    {
        if (Status != MoneyOrderStatus.Pending)
        {
            throw new BusinessRuleViolationException("MONEY_ORDER_STATUS", $"Money order {SerialNumber} is {Status} and cannot be issued.");
        }

        Status = MoneyOrderStatus.Issued;
        IssuedAt = at;
    }

    public void Cancel() => Status = MoneyOrderStatus.Cancelled;
}

/// <summary>
/// Result of checking a customer's identity document. Only the masked number exists in the
/// domain; the persistence model goes further and stores only the last four characters.
/// </summary>
public sealed class IdentityVerification : Entity
{
    private IdentityVerification()
    {
    }

    public string Reference { get; private set; } = string.Empty;

    public int TransactionId { get; private set; }

    public BranchCode BranchCode { get; private set; }

    public int StaffId { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public DocumentType DocumentType { get; private set; }

    public MaskedDocumentNumber MaskedDocumentNumber { get; private set; }

    public VerificationResult Result { get; private set; }

    public DateTimeOffset VerifiedAt { get; private set; }

    /// <summary>The raw document number is masked here and goes no further.</summary>
    public static IdentityVerification Record(int transactionId, BranchCode branch, int staffId, string customerName, DocumentType documentType, string rawDocumentNumber, VerificationResult result, DateTimeOffset at, int referenceSuffix) =>
        new()
        {
            Reference = string.Create(CultureInfo.InvariantCulture, $"IDV-{at:yyMMdd}-{referenceSuffix:000000}"),
            TransactionId = transactionId,
            BranchCode = branch,
            StaffId = staffId,
            CustomerName = Guard.Text(customerName, nameof(customerName), 80),
            DocumentType = Guard.Defined(documentType, nameof(documentType)),
            MaskedDocumentNumber = MaskedDocumentNumber.FromRaw(rawDocumentNumber),
            Result = Guard.Defined(result, nameof(result)),
            VerifiedAt = at,
        };

    /// <summary>Rebuilds a verification from storage (used by the persistence mapper only).</summary>
    public static IdentityVerification Rehydrate(int id, string reference, int transactionId, BranchCode branch, int staffId, string customerName, DocumentType documentType, MaskedDocumentNumber masked, VerificationResult result, DateTimeOffset verifiedAt) =>
        new()
        {
            Id = id,
            Reference = reference,
            TransactionId = transactionId,
            BranchCode = branch,
            StaffId = staffId,
            CustomerName = customerName,
            DocumentType = documentType,
            MaskedDocumentNumber = masked,
            Result = result,
            VerifiedAt = verifiedAt,
        };

    /// <summary>Lets the mapper copy the database-generated key back after insert.</summary>
    public void AssignId(int id)
    {
        if (Id != 0 && Id != id)
        {
            throw new InvalidOperationException("The identity of a verification cannot change.");
        }

        Id = id;
    }
}
