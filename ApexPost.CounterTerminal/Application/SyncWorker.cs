using ApexPost.Contracts;
using ApexPost.CounterTerminal.Domain;
using ApexPost.Models.ValueObjects;

namespace ApexPost.CounterTerminal.Application;

/// <summary>Result of one sync attempt, shown to the clerk.</summary>
public sealed record SyncReport(bool Succeeded, int Pushed, int Accepted, int Warnings, int Rejected, int ProductsPulled, int StockPulled, string? Error);

/// <summary>
/// Application-level outbox sync (ADR-W3-08). PUSH: pending sales go to POST /api/sync/transactions
/// in batches; the server's SyncInbox makes every push idempotent, so a sale whose response was lost
/// is simply pushed again. PULL: the catalogue, prices and this branch's stock come back from
/// GET /api/sync/branch-snapshot?since=..., merged field by field. Failures are never data loss:
/// the sale stays Pending and the next attempt waits with exponential backoff and jitter.
/// </summary>
public sealed class SyncWorker(
    IPendingSaleStore sales,
    ICatalogStore catalog,
    IStockStore stock,
    ISyncStateStore syncState,
    ISyncApiClient api,
    TerminalOptions options,
    TimeProvider clock)
{
    private const int BatchSize = 50;
    private readonly SemaphoreSlim _single = new(1, 1);
    private int _consecutiveFailures;

    /// <summary>Raised after every attempt so the UI can refresh its status line.</summary>
    public event EventHandler<SyncReport>? Completed;

    /// <summary>One push + pull. Concurrent callers wait for the attempt in progress.</summary>
    public async Task<SyncReport> SyncOnceAsync(CancellationToken cancellationToken)
    {
        await _single.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var report = await RunAsync(cancellationToken).ConfigureAwait(false);
            Completed?.Invoke(this, report);
            return report;
        }
        finally
        {
            _single.Release();
        }
    }

    /// <summary>
    /// Background loop: sync when the change monitor reports a new local sale, or on the interval;
    /// after a failure wait 2^n seconds (capped, with jitter) before the next attempt.
    /// </summary>
    public async Task RunLoopAsync(IPendingSaleChangeFeed changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(cancellationToken).ConfigureAwait(false);

                using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wake.CancelAfter(NextDelay());
                try
                {
                    await changes.WaitForChangesAsync(wake.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Interval or backoff elapsed: loop round and try again.
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Delay before the next attempt: the interval when healthy, exponential backoff after failures.</summary>
    public TimeSpan NextDelay()
    {
        if (_consecutiveFailures == 0)
        {
            return TimeSpan.FromSeconds(Math.Max(1, options.SyncIntervalSeconds));
        }

        var exponential = Math.Min(options.MaxBackoffSeconds, Math.Pow(2, Math.Min(_consecutiveFailures, 10)));
        var jitter = Random.Shared.NextDouble() * 0.25 * exponential;
        return TimeSpan.FromSeconds(exponential + jitter);
    }

    internal int ConsecutiveFailures => _consecutiveFailures;

    private async Task<SyncReport> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var state = syncState.Get();
        int pushed = 0, accepted = 0, warnings = 0, rejected = 0;
        try
        {
            // ---- PUSH (outbox) ----
            while (true)
            {
                var batch = sales.ListByStatus(SaleStatus.Pending, BatchSize);
                if (batch.Count == 0)
                {
                    break;
                }

                var response = await api.PushAsync(new PushTransactionsRequest(SyncContract.SchemaVersion, batch.Select(ToDto).ToList()), cancellationToken).ConfigureAwait(false);
                var byId = response.Results.ToDictionary(r => r.LocalTransactionId);
                foreach (var sale in batch)
                {
                    pushed++;
                    if (!byId.TryGetValue(sale.LocalTransactionId, out var result))
                    {
                        sales.Save(sale with { Attempts = sale.Attempts + 1, LastAttemptAt = now, LastError = "No result returned; will retry." });
                        continue;
                    }

                    var outcome = result.Outcome switch
                    {
                        "Accepted" or "AcceptedWithWarnings" => SaleStatus.Synced,
                        "Rejected" => SaleStatus.Rejected,
                        _ => SaleStatus.Invalid,
                    };
                    sales.Save(sale with
                    {
                        Status = outcome,
                        Attempts = sale.Attempts + 1,
                        LastAttemptAt = now,
                        LastError = outcome == SaleStatus.Synced ? null : result.Message,
                        ServerTransactionId = result.ServerTransactionId,
                        Warnings = result.Warnings,
                    });

                    if (outcome == SaleStatus.Synced)
                    {
                        accepted++;
                        warnings += result.Warnings.Count;
                        MoveSoldUnitsToServer(sale);
                    }
                    else
                    {
                        rejected++;
                        ReleaseLocalStock(sale);
                    }
                }

                if (batch.Count < BatchSize)
                {
                    break;
                }
            }

            // ---- PULL (catalogue, prices, stock) ----
            var snapshot = await api.PullAsync(state.LastSnapshotAt, cancellationToken).ConfigureAwait(false);
            catalog.Upsert(snapshot.Products.Select(p => new CatalogItem(new Sku(p.Sku), p.Name, p.Category, new Money(p.UnitPrice), p.TaxCategory, p.IsActive)));
            stock.ApplyServerSnapshot(new BranchCode(snapshot.BranchCode), snapshot.Stock, snapshot.GeneratedAtUtc);

            _consecutiveFailures = 0;
            syncState.Save(state with { IsOnline = true, LastSuccessfulSyncAt = clock.GetUtcNow(), LastSnapshotAt = snapshot.GeneratedAtUtc, LastError = null });
            return new SyncReport(true, pushed, accepted, warnings, rejected, snapshot.Products.Count, snapshot.Stock.Count, null);
        }
        catch (SyncUnavailableException ex)
        {
            // Network failure is not data loss: everything not confirmed stays Pending.
            _consecutiveFailures++;
            foreach (var sale in sales.ListByStatus(SaleStatus.Pending, BatchSize))
            {
                sales.Save(sale with { Attempts = sale.Attempts + 1, LastAttemptAt = now, LastError = ex.Message });
            }

            syncState.Save(state with { IsOnline = false, LastError = ex.Message, LastErrorAt = now });
            return new SyncReport(false, pushed, accepted, warnings, rejected, 0, 0, ex.Message);
        }
    }

    /// <summary>The server now counts these units, so they move from the device-owned counter to ServerQuantity.</summary>
    private void MoveSoldUnitsToServer(PendingSale sale)
    {
        foreach (var line in sale.Lines)
        {
            if (stock.Get(sale.BranchCode, line.Sku) is { } level)
            {
                stock.Save(level with
                {
                    ServerQuantity = Math.Max(0, level.ServerQuantity - line.Quantity),
                    LocalSoldSinceSync = Math.Max(0, level.LocalSoldSinceSync - line.Quantity),
                });
            }
        }
    }

    /// <summary>A rejected sale never happened on the server: give its units back to the local counter.</summary>
    private void ReleaseLocalStock(PendingSale sale)
    {
        foreach (var line in sale.Lines)
        {
            if (stock.Get(sale.BranchCode, line.Sku) is { } level)
            {
                stock.Save(level with { LocalSoldSinceSync = Math.Max(0, level.LocalSoldSinceSync - line.Quantity) });
            }
        }
    }

    private static OfflineSaleDto ToDto(PendingSale sale) => new(
        sale.LocalTransactionId,
        sale.CounterNumber,
        sale.ClerkStaffId,
        sale.CapturedAt,
        sale.PaymentMethod,
        sale.Lines.Select(l => new OfflineSaleLineDto(l.Sku.Value, l.Quantity, l.UnitPrice.Amount)).ToList(),
        sale.MoneyOrders.Select(m => new OfflineMoneyOrderDto(m.Amount.Amount, m.Sender, m.Recipient)).ToList());
}
