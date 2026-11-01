using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Events;

namespace ApexPost.Services.Notifications;

/// <summary>
/// Subscribes to business events and sends (simulated) notifications.
///
/// Event handlers run synchronously on the request thread, so they must be quick and must not
/// block on I/O. Each handler only writes to a bounded <see cref="Channel{T}"/>; this background
/// service drains the channel and persists asynchronously. Bounded capacity means a burst of
/// events cannot exhaust memory (oldest notifications are dropped and logged instead).
/// </summary>
public sealed partial class NotificationDispatcher : BackgroundService
{
    private readonly IApexPostEvents _events;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly TimeProvider _clock;
    private readonly Channel<Notification> _queue = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public NotificationDispatcher(
        IApexPostEvents events,
        IServiceScopeFactory scopes,
        ILogger<NotificationDispatcher> logger,
        TimeProvider clock)
    {
        _events = events;
        _scopes = scopes;
        _logger = logger;
        _clock = clock;

        // Subscribe immediately (not in ExecuteAsync) so no event raised during startup is missed.
        _events.StockLow += OnStockLow;
        _events.MoneyOrderIssued += OnMoneyOrderIssued;
        _events.TransactionVoided += OnTransactionVoided;
    }

    public override void Dispose()
    {
        _events.StockLow -= OnStockLow;
        _events.MoneyOrderIssued -= OnMoneyOrderIssued;
        _events.TransactionVoided -= OnTransactionVoided;
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notification in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    // The dispatcher is a singleton; each notification gets its own scope (DbContext + unit of work).
                    await using var scope = _scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<INotificationRepository>().Add(notification);
                    await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    LogNotificationSent(notification.Type, notification.Recipient, notification.Message, notification.CorrelationId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed notification must never take the dispatcher down.
                    LogNotificationFailed(ex, notification.Type);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void OnStockLow(object? sender, StockLowEventArgs e) => Enqueue(
        NotificationType.LowStock,
        e.BranchCode,
        $"manager.{e.BranchCode.Value.ToLowerInvariant()}@apexpost.local",
        $"Low stock at {e.BranchCode}: {e.ProductName} ({e.Sku}) has {e.QuantityOnHand} left (reorder at {e.ReorderThreshold}).",
        e.CorrelationId);

    private void OnMoneyOrderIssued(object? sender, MoneyOrderIssuedEventArgs e)
    {
        if (!e.IsHighValue)
        {
            return;
        }

        Enqueue(
            NotificationType.HighValueMoneyOrder,
            e.BranchCode,
            "compliance@apexpost.local",
            $"High-value money order {e.SerialNumber} for {e.Amount} issued at {e.BranchCode}.",
            e.CorrelationId);
    }

    private void OnTransactionVoided(object? sender, TransactionVoidedEventArgs e) => Enqueue(
        e.IsRefund ? NotificationType.TransactionRefunded : NotificationType.TransactionVoided,
        e.BranchCode,
        "audit@apexpost.local",
        $"Transaction {e.TransactionId} at {e.BranchCode} was {(e.IsRefund ? "refunded" : "voided")} by staff {e.ManagerStaffId} ({e.Total}). Reason: {e.Reason}",
        e.CorrelationId);

    private void Enqueue(NotificationType type, BranchCode branch, string recipient, string message, string correlationId)
    {
        var notification = Notification.Create(type, branch, recipient, message, correlationId, _clock.GetUtcNow());
        if (!_queue.Writer.TryWrite(notification))
        {
            LogNotificationDropped(notification.Type);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[NOTIFY] {Type} to {Recipient}: {Message} (CorrelationId {CorrelationId})")]
    private partial void LogNotificationSent(NotificationType type, string recipient, string message, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send {Type} notification")]
    private partial void LogNotificationFailed(Exception exception, NotificationType type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification queue full; dropped {Type} notification")]
    private partial void LogNotificationDropped(NotificationType type);
}
