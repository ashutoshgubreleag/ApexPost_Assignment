using System.Globalization;
using System.Security.Cryptography;
using ApexPost.Models.Domain;
using ApexPost.Models.Exceptions;
using ApexPost.Models.Validation;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Common;
using ApexPost.Services.Events;

namespace ApexPost.Services.Sync;

public interface IOfflineSaleService
{
    /// <summary>Applies one offline sale through the normal domain rules and one unit of work. Idempotent.</summary>
    Task<OfflineSaleResult> RecordAsync(RecordOfflineSaleCommand command, CancellationToken cancellationToken);

    Task<BranchSnapshot> GetSnapshotAsync(BranchCode branch, DateTimeOffset? since, CancellationToken cancellationToken);
}

/// <summary>
/// Server side of the offline outbox. Conflict policies (see CONFLICTS.md):
/// C1 price changed while offline → the sale is honoured at the offline price (the customer has
///    paid it); a variance above the configured tolerance is flagged for review.
/// C2 combined offline sales exceed stock → accept and flag: the goods have physically left the
///    counter, so stock stops at zero (the CHECK constraint still holds) and the shortfall is
///    recorded as a conflict for the branch manager; the terminal shows "accepted with warning".
/// C3 server-only fields (prices, stock, status, ids, audit columns) are never taken from the device:
///    the payload only carries the facts of the sale, and the server re-derives everything else.
/// Idempotency: SyncInbox has a unique (DeviceId, LocalTransactionId) index and is written in the
///    same commit as the sale, so a retried push returns the original result and never double-posts.
/// </summary>
public sealed class OfflineSaleService(
    ISyncInboxRepository inbox,
    ICounterTransactionRepository transactions,
    IStaffRepository staff,
    IProductRepository products,
    IStockRepository stock,
    IMoneyOrderRepository moneyOrders,
    INotificationRepository notifications,
    ISyncQueries syncQueries,
    IUnitOfWork unitOfWork,
    BusinessRules rules,
    IApexPostEvents events,
    TimeProvider clock) : IOfflineSaleService
{
    public async Task<OfflineSaleResult> RecordAsync(RecordOfflineSaleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ReflectionValidator.EnsureValid(command);
        foreach (var line in command.Lines)
        {
            ReflectionValidator.EnsureValid(line);
        }

        foreach (var order in command.MoneyOrders)
        {
            ReflectionValidator.EnsureValid(order);
        }

        if (command.LocalTransactionId == Guid.Empty)
        {
            throw new CommandValidationException(nameof(command.LocalTransactionId), "A local transaction id is required.");
        }

        if (command.Lines.Count == 0 && command.MoneyOrders.Count == 0)
        {
            throw new CommandValidationException(nameof(command.Lines), "An offline sale needs at least one line.");
        }

        var existing = await inbox.FindAsync(command.DeviceId, command.LocalTransactionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ToResult(existing, wasDuplicate: true);
        }

        try
        {
            var (entry, transaction) = await ConcurrencyRetry.ExecuteAsync(unitOfWork, rules.ConcurrencyRetryAttempts, ct => ApplyAsync(command, ct), cancellationToken).ConfigureAwait(false);
            entry.CaptureTransactionId();
            if (transaction is not null)
            {
                events.Publish(new TransactionCompletedEventArgs(transaction.Id, transaction.BranchCode, transaction.TotalAmount, transaction.ClerkStaffId, transaction.ClosedAt ?? clock.GetUtcNow()));
            }

            return ToResult(entry, wasDuplicate: false);
        }
        catch (DuplicateKeyException)
        {
            // Two pushes of the same sale raced; the other one committed first. Return its result.
            unitOfWork.DiscardChanges();
            var winner = await inbox.FindAsync(command.DeviceId, command.LocalTransactionId, cancellationToken).ConfigureAwait(false)
                ?? throw new ConcurrencyConflictException("The offline sale is being processed; retry shortly.");
            return ToResult(winner, wasDuplicate: true);
        }
    }

    public Task<BranchSnapshot> GetSnapshotAsync(BranchCode branch, DateTimeOffset? since, CancellationToken cancellationToken) =>
        syncQueries.GetBranchSnapshotAsync(branch, since, cancellationToken);

    private async Task<(SyncInboxEntry Entry, CounterTransaction? Transaction)> ApplyAsync(RecordOfflineSaleCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var branch = new BranchCode(command.BranchCode);
        var entry = SyncInboxEntry.Receive(command.DeviceId, command.LocalTransactionId, branch, now);
        inbox.Add(entry);

        // Who sold it: must be a real clerk of this branch (the server does not trust the device blindly).
        var clerk = await staff.GetByIdAsync(command.ClerkStaffId, cancellationToken).ConfigureAwait(false);
        if (clerk is null || clerk.HomeBranchCode != branch)
        {
            entry.Reject($"Staff {command.ClerkStaffId} is not a clerk of {branch}.");
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (entry, null);
        }

        // High-value money orders are never accepted from an offline device (they need online ID verification).
        if (command.MoneyOrders.Any(m => rules.RequiresIdentityVerification(new Money(m.Amount))))
        {
            entry.Reject("Money orders above the identity threshold cannot be sold offline.");
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (entry, null);
        }

        var skus = command.Lines.Select(l => new Sku(l.Sku)).Distinct().ToList();
        var catalogue = (await products.GetBySkusAsync(skus, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Sku);
        var unknown = skus.Where(s => !catalogue.ContainsKey(s)).ToList();
        if (unknown.Count > 0)
        {
            entry.Reject($"Unknown SKU(s): {string.Join(", ", unknown)}.");
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (entry, null);
        }

        var capturedAt = command.CapturedAt == default ? now : command.CapturedAt;
        var transaction = CounterTransaction.Open(branch, command.CounterNumber, clerk.Id, capturedAt, TransactionSource.OfflineTerminal);
        var rows = (await stock.GetForUpdateAsync(branch, skus, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Sku);

        foreach (var line in command.Lines)
        {
            var sku = new Sku(line.Sku);
            var product = catalogue[sku];
            var offlinePrice = new Money(line.UnitPrice).RoundedToCents;

            // C1: honour the price the customer paid; flag a large variance for review.
            var variance = Math.Abs(offlinePrice.Amount - product.UnitPrice.Amount) / product.UnitPrice.Amount;
            if (variance > rules.OfflinePriceVarianceTolerance)
            {
                entry.Flag(
                    SyncConflictType.PriceVariance,
                    sku.Value,
                    string.Create(CultureInfo.InvariantCulture, $"{sku} sold offline at {offlinePrice} but the server price is {product.UnitPrice} ({variance:P0} variance); honoured at {offlinePrice}, review required."),
                    now);
            }

            transaction.AddRetailItem(sku, product.Name, offlinePrice, TaxRules.CategoryFor(TransactionLineType.RetailItem, product.TaxCategory), line.Quantity, TaxRules.TaxOn);

            // C2: accept the sale, stop stock at zero and record any shortfall.
            var shortfall = rows.TryGetValue(sku, out var row) ? row.RemoveUpToAvailable(line.Quantity, now) : line.Quantity;
            if (shortfall > 0)
            {
                entry.Flag(
                    SyncConflictType.StockShortfall,
                    sku.Value,
                    $"{sku}: {shortfall} unit(s) sold offline at {branch} were not in stock on the server; stock set to 0, manager to reconcile.",
                    now);
            }
        }

        foreach (var mo in command.MoneyOrders)
        {
            var amount = new Money(mo.Amount).RoundedToCents;
            var order = MoneyOrder.CreatePending(transaction, amount, rules.MoneyOrderFee(amount), rules.MaximumMoneyOrderAmount, mo.Sender, mo.Recipient, capturedAt, RandomNumberGenerator.GetInt32(1000, 9999));
            var taxCategory = TaxRules.CategoryFor(TransactionLineType.MoneyOrder);
            transaction.AddServiceLine(TransactionLineType.MoneyOrder, order.SerialNumber, $"Money order {order.Amount} to {order.Recipient}", order.Fee, taxCategory, TaxRules.TaxOn(order.Fee, taxCategory), order.Amount);
            order.Issue(capturedAt);
            moneyOrders.Add(order);
        }

        transaction.Pay(command.PaymentMethod, capturedAt);
        transactions.Add(transaction);
        entry.Accept(transaction);

        if (entry.Conflicts.Count > 0)
        {
            notifications.Add(Notification.Create(NotificationType.SyncConflict, branch, $"manager.{branch.Value.ToLowerInvariant()}@apexpost.local", $"Offline sale {command.LocalTransactionId} from {command.DeviceId} needs review: {entry.Message}", string.Empty, now));
        }

        // ONE commit: inbox row, conflicts, transaction, lines, payment, money orders and stock.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (entry, transaction);
    }

    private static OfflineSaleResult ToResult(SyncInboxEntry entry, bool wasDuplicate) => new(
        entry.LocalTransactionId,
        entry.Outcome,
        entry.ServerTransactionId ?? entry.LinkedTransaction?.Id,
        entry.Message,
        entry.Conflicts.Select(c => c.Details).ToList(),
        wasDuplicate);
}
