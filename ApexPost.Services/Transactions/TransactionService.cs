using System.Security.Cryptography;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Caching;
using ApexPost.Services.Catalog;
using ApexPost.Services.Common;
using ApexPost.Services.Events;
using ApexPost.Services.Inventory;
using ApexPost.Services.MoneyOrders;
using ApexPost.Services.Shipping;

namespace ApexPost.Services.Transactions;

/// <summary>
/// Command service for counter transactions. Each public method is one use case:
/// load the aggregate(s) through repositories → apply domain methods (which enforce the rules) →
/// stage new rows → ONE <see cref="IUnitOfWork.SaveChangesAsync"/> → publish events after the
/// commit. Reads for the screens go through <see cref="ITransactionQueries"/>.
/// </summary>
public sealed class TransactionService(
    ICounterTransactionRepository transactions,
    IStaffRepository staff,
    IBranchRepository branches,
    IStockRepository stock,
    IShipmentRepository shipments,
    IMoneyOrderRepository moneyOrders,
    IIdentityVerificationRepository verifications,
    ITransactionQueries queries,
    IUnitOfWork unitOfWork,
    IProductCatalogService catalog,
    IShippingService shipping,
    IReferenceDataCache referenceData,
    BusinessRules rules,
    IApexPostEvents events,
    TimeProvider clock) : ITransactionService
{
    public Task<IReadOnlyList<TransactionSummaryDto>> GetRecentAsync(StaffContext actor, BranchCode? branch, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var scope = branch ?? (actor.IsAdministrator || actor.IsAuditor ? null : actor.BranchCode);
        if (scope is { } code && !actor.CanReadBranch(code))
        {
            throw new ForbiddenOperationException("You can only view transactions from your own branch.");
        }

        return queries.ListRecentAsync(scope, Math.Clamp(take, 1, 500), cancellationToken);
    }

    public async Task<TransactionDetails> GetDetailsAsync(int transactionId, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var data = await queries.GetDetailsAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Transaction", transactionId);
        if (!actor.CanReadBranch(data.Transaction.BranchCode))
        {
            throw new ForbiddenOperationException("You can only view transactions from your own branch.");
        }

        var required = data.Lines.Any(l => l.LineType == TransactionLineType.MoneyOrder && rules.RequiresIdentityVerification(l.PassThroughAmount));
        var onBasket = data.Lines.Where(l => l.LineType == TransactionLineType.IdentityVerification).Select(l => l.ItemCode).ToHashSet(StringComparer.Ordinal);
        var satisfied = data.Verifications.Any(v => v.Result == VerificationResult.Passed && onBasket.Contains(v.Reference));
        return new TransactionDetails(data.Transaction, data.Lines, data.Payments, data.Shipments, data.MoneyOrders, data.Verifications, required, satisfied);
    }

    public async Task<TransactionSummaryDto> OpenAsync(StaffContext actor, int counterTerminalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var clerk = await EnsureCanTransactAsync(actor, actor.BranchCode, cancellationToken).ConfigureAwait(false);
        var branch = await EnsureOpenBranchAsync(clerk.HomeBranchCode, cancellationToken).ConfigureAwait(false);
        branch.EnsureActiveCounter(counterTerminalId);

        var transaction = CounterTransaction.Open(branch.Code, counterTerminalId, clerk.Id, clock.GetUtcNow());
        transactions.Add(transaction);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return transaction.ToSummary();
    }

    public async Task<TransactionSummaryDto> AddRetailItemAsync(int transactionId, AddRetailItemCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command);

        return await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, async ct =>
        {
            var transaction = await LoadOpenForUpdateAsync(transactionId, actor, ct).ConfigureAwait(false);
            var sku = new Sku(command.Sku);
            var product = await catalog.FindProductAsync(sku, ct).ConfigureAwait(false)
                ?? throw new CommandValidationException(nameof(command.Sku), $"Product {sku} is not in the catalogue.");

            // Early, friendly check; the authoritative check happens at payment, on tracked rows.
            var wanted = transaction.QuantityOf(sku) + command.Quantity;
            var available = await stock.GetQuantityAsync(transaction.BranchCode, sku, ct).ConfigureAwait(false);
            if (wanted > available)
            {
                throw new BusinessRuleViolationException("STOCK_INSUFFICIENT", $"Only {available} x {product.Name} in stock at {transaction.BranchCode}.");
            }

            transaction.AddRetailItem(sku, product.Name, product.UnitPrice, TaxRules.CategoryFor(TransactionLineType.RetailItem, product.TaxCategory), command.Quantity, TaxRules.TaxOn);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return transaction.ToSummary();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ShipmentDto> AddShipmentAsync(int transactionId, AddShipmentCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command.Recipient);

        // Quote first: it calls the courier API, so it happens before any row is loaded for update.
        var quote = await shipping.QuoteAsync(command.Quote, cancellationToken).ConfigureAwait(false);
        var transaction = await LoadOpenForUpdateAsync(transactionId, actor, cancellationToken).ConfigureAwait(false);

        var shipment = Shipment.Create(
            transaction.Id,
            transaction.BranchCode,
            quote.Context.ShippingType,
            quote.Context.ServiceLevel,
            quote.Context.Zone.ZoneCode,
            quote.Context.ActualWeight,
            quote.Context.Dimensions,
            quote.ChargeableWeight,
            command.Recipient.ToAddress(),
            quote.Net,
            clock.GetUtcNow(),
            RandomNumberGenerator.GetInt32(100000, 999999));
        shipments.Add(shipment);

        transaction.AddServiceLine(
            TransactionLineType.Shipping,
            shipment.TrackingNumber,
            $"{shipment.ShippingType} {shipment.ServiceLevel} to {quote.Context.Zone.Name} ({quote.ChargeableWeight})",
            quote.Net,
            quote.TaxCategory,
            quote.Tax,
            Money.Zero);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return shipment.ToDto();
    }

    public async Task<MoneyOrderDto> AddMoneyOrderAsync(int transactionId, MoneyOrderCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command);
        var transaction = await LoadOpenForUpdateAsync(transactionId, actor, cancellationToken).ConfigureAwait(false);

        var amount = new Money(command.Amount).RoundedToCents;
        var order = MoneyOrder.CreatePending(
            transaction,
            amount,
            rules.MoneyOrderFee(amount),
            rules.MaximumMoneyOrderAmount,
            command.Sender,
            command.Recipient,
            clock.GetUtcNow(),
            RandomNumberGenerator.GetInt32(1000, 9999));
        moneyOrders.Add(order);

        var taxCategory = TaxRules.CategoryFor(TransactionLineType.MoneyOrder);
        transaction.AddServiceLine(
            TransactionLineType.MoneyOrder,
            order.SerialNumber,
            $"Money order {order.Amount} to {order.Recipient}",
            order.Fee,
            taxCategory,
            TaxRules.TaxOn(order.Fee, taxCategory),
            order.Amount);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return order.ToDto();
    }

    public async Task<VerificationDto> AddIdentityVerificationAsync(int transactionId, IdentityVerificationCommand command, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ReflectionValidator.EnsureValid(command);
        var transaction = await LoadOpenForUpdateAsync(transactionId, actor, cancellationToken).ConfigureAwait(false);

        // The raw document number is masked inside Record(...) and goes no further.
        var verification = IdentityVerification.Record(
            transaction.Id,
            transaction.BranchCode,
            actor.StaffId,
            command.CustomerName,
            command.DocumentType,
            command.DocumentNumber,
            command.Result,
            clock.GetUtcNow(),
            RandomNumberGenerator.GetInt32(100000, 999999));
        verifications.Add(verification);

        var fee = rules.IdentityVerificationFee;
        var taxCategory = TaxRules.CategoryFor(TransactionLineType.IdentityVerification);
        transaction.AddServiceLine(
            TransactionLineType.IdentityVerification,
            verification.Reference,
            $"Identity check: {verification.DocumentType} {verification.MaskedDocumentNumber} ({verification.Result})",
            fee,
            taxCategory,
            TaxRules.TaxOn(fee, taxCategory),
            Money.Zero);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return verification.ToDto();
    }

    public async Task<TransactionSummaryDto> RemoveLineAsync(int transactionId, int lineNumber, StaffContext actor, CancellationToken cancellationToken)
    {
        var transaction = await LoadOpenForUpdateAsync(transactionId, actor, cancellationToken).ConfigureAwait(false);
        var line = transaction.RemoveLine(lineNumber);

        switch (line.LineType)
        {
            case TransactionLineType.Shipping:
                foreach (var shipment in await shipments.GetByTrackingNumbersAsync([line.ItemCode], cancellationToken).ConfigureAwait(false))
                {
                    shipment.Cancel();
                }

                break;
            case TransactionLineType.MoneyOrder:
                foreach (var order in await moneyOrders.GetBySerialNumbersAsync([line.ItemCode], cancellationToken).ConfigureAwait(false))
                {
                    order.Cancel();
                }

                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return transaction.ToSummary();
    }

    public async Task<TransactionSummaryDto> PayAsync(int transactionId, PaymentMethod paymentMethod, StaffContext actor, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(paymentMethod))
        {
            throw new CommandValidationException(nameof(paymentMethod), "Choose a payment method.");
        }

        var (transaction, issued, lowStock) = await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, async ct =>
        {
            var tx = await LoadOpenForUpdateAsync(transactionId, actor, ct).ConfigureAwait(false);
            if (tx.LineItems.Count == 0)
            {
                throw new BusinessRuleViolationException("BASKET_EMPTY", "Add at least one item or service before taking payment.");
            }

            var now = clock.GetUtcNow();

            // Rule: a money order above the threshold needs a passed identity verification in THIS transaction.
            var orders = await moneyOrders.GetBySerialNumbersAsync(tx.ItemCodes(TransactionLineType.MoneyOrder).ToList(), ct).ConfigureAwait(false);
            if (orders.Any(o => rules.RequiresIdentityVerification(o.Amount)))
            {
                var checks = await verifications.GetByReferencesAsync(tx.ItemCodes(TransactionLineType.IdentityVerification).ToList(), ct).ConfigureAwait(false);
                var passed = checks.FirstOrDefault(v => v.Result == VerificationResult.Passed && v.TransactionId == tx.Id);
                foreach (var order in orders.Where(o => rules.RequiresIdentityVerification(o.Amount)))
                {
                    order.RequireVerification(passed);
                }
            }

            // Rule: stock can never go negative. Stock rows are tracked with a rowversion, so a
            // concurrent sale of the same rows makes SaveChanges fail and this use case retry.
            var low = await InventoryService.RemoveStockAsync(stock, tx.BranchCode, tx.RetailQuantities(), rules, now, ct).ConfigureAwait(false);

            foreach (var shipment in await shipments.GetByTrackingNumbersAsync(tx.ItemCodes(TransactionLineType.Shipping).ToList(), ct).ConfigureAwait(false))
            {
                shipment.Lodge();
            }

            foreach (var order in orders)
            {
                order.Issue(now);
            }

            tx.Pay(paymentMethod, now);

            // ONE commit: stock decrements, money orders, shipments, the transaction, its lines and the payment.
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return (tx, orders, low);
        }, cancellationToken).ConfigureAwait(false);

        await PublishAfterPaymentAsync(transaction, issued, lowStock, cancellationToken).ConfigureAwait(false);
        return transaction.ToSummary();
    }

    public Task<TransactionSummaryDto> VoidAsync(int transactionId, VoidOrRefundCommand command, StaffContext actor, CancellationToken cancellationToken) =>
        ReverseAsync(transactionId, command, actor, isRefund: false, cancellationToken);

    public Task<TransactionSummaryDto> RefundAsync(int transactionId, VoidOrRefundCommand command, StaffContext actor, CancellationToken cancellationToken) =>
        ReverseAsync(transactionId, command, actor, isRefund: true, cancellationToken);

    private async Task<TransactionSummaryDto> ReverseAsync(int transactionId, VoidOrRefundCommand command, StaffContext actor, bool isRefund, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        var action = isRefund ? "refund" : "void";

        // Rule: voids and refunds require the Branch Manager role and a reason.
        if (actor.Role != StaffRole.BranchManager)
        {
            throw new ForbiddenOperationException($"Only a branch manager can {action} a transaction.");
        }

        ReflectionValidator.EnsureValid(command);

        var transaction = await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, async ct =>
        {
            var tx = await LoadAsync(transactionId, ct).ConfigureAwait(false);
            var manager = await EnsureCanTransactAsync(actor, tx.BranchCode, ct).ConfigureAwait(false);
            var wasPaid = tx.Status == TransactionStatus.Paid;

            var now = clock.GetUtcNow();
            if (isRefund)
            {
                tx.Refund(command.Reason, manager.Id, now);
            }
            else
            {
                tx.Void(command.Reason, manager.Id, now);
            }

            if (wasPaid)
            {
                await InventoryService.ReturnStockAsync(stock, tx.BranchCode, tx.RetailQuantities(), now, ct).ConfigureAwait(false);
            }

            foreach (var shipment in await shipments.GetByTrackingNumbersAsync(tx.ItemCodes(TransactionLineType.Shipping).ToList(), ct).ConfigureAwait(false))
            {
                shipment.Cancel();
            }

            foreach (var order in await moneyOrders.GetBySerialNumbersAsync(tx.ItemCodes(TransactionLineType.MoneyOrder).ToList(), ct).ConfigureAwait(false))
            {
                order.Cancel();
            }

            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return tx;
        }, cancellationToken).ConfigureAwait(false);

        events.Publish(new TransactionVoidedEventArgs(transaction.Id, transaction.BranchCode, transaction.AuthorisedByStaffId ?? actor.StaffId, transaction.VoidReason ?? string.Empty, isRefund, transaction.TotalAmount, clock.GetUtcNow()));
        return transaction.ToSummary();
    }

    private async Task<CounterTransaction> LoadAsync(int transactionId, CancellationToken cancellationToken) =>
        await transactions.GetAsync(transactionId, cancellationToken).ConfigureAwait(false)
        ?? throw new EntityNotFoundException("Transaction", transactionId);

    private async Task<CounterTransaction> LoadOpenForUpdateAsync(int transactionId, StaffContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var transaction = await LoadAsync(transactionId, cancellationToken).ConfigureAwait(false);

        // Re-checked on every request: a cookie can outlive a deactivation or a branch closure.
        await EnsureCanTransactAsync(actor, transaction.BranchCode, cancellationToken).ConfigureAwait(false);
        await EnsureOpenBranchAsync(transaction.BranchCode, cancellationToken).ConfigureAwait(false);
        transaction.EnsureOpen();
        return transaction;
    }

    private async Task<StaffMember> EnsureCanTransactAsync(StaffContext actor, BranchCode branch, CancellationToken cancellationToken)
    {
        var member = await staff.GetByIdAsync(actor.StaffId, cancellationToken).ConfigureAwait(false)
            ?? throw new ForbiddenOperationException("Your staff record no longer exists.");
        member.EnsureCanTransactAt(branch);
        return member;
    }

    private async Task<Models.Domain.Branch> EnsureOpenBranchAsync(BranchCode code, CancellationToken cancellationToken)
    {
        var branch = await branches.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException("Branch", code);
        branch.EnsureOpenForTrading();
        return branch;
    }

    private async Task PublishAfterPaymentAsync(CounterTransaction transaction, IReadOnlyList<MoneyOrder> issued, IReadOnlyList<BranchStock> lowStock, CancellationToken cancellationToken)
    {
        var at = transaction.ClosedAt ?? clock.GetUtcNow();
        events.Publish(new TransactionCompletedEventArgs(transaction.Id, transaction.BranchCode, transaction.TotalAmount, transaction.ClerkStaffId, at));
        foreach (var order in issued)
        {
            events.Publish(new MoneyOrderIssuedEventArgs(order.Id, order.SerialNumber, order.BranchCode, order.Amount, rules.IsHighValueMoneyOrder(order.Amount), at));
        }

        if (lowStock.Count > 0)
        {
            var products = await referenceData.GetProductsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in lowStock)
            {
                var name = products.TryGetValue(row.Sku, out var product) ? product.Name : row.Sku.Value;
                events.Publish(new StockLowEventArgs(row.BranchCode, row.Sku, name, row.QuantityOnHand, row.ReorderThreshold, at));
            }
        }
    }
}
