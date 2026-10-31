using ApexPost.Database;
using ApexPost.Models.Domain;
using ApexPost.Models.ValueObjects;
using ApexPost.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ApexPost.Repositories.Seeding;

/// <summary>
/// Seeding service for HISTORICAL transactions (reference data is seeded by the InitialCreate
/// migration). Generates fictional, deterministic history (fixed random seed) through the domain
/// model, so every row obeys the same rules as a real sale: about 3,000 paid, voided and refunded
/// baskets over 60 days with retail lines, shipments, money orders and identity checks. It runs
/// only when CounterTransactions is empty, so it is safe to call on every Development start.
/// Historical sales do not move today's stock levels (they represent the past).
/// For execution-plan work, database/perf/01-generate-volume.sql adds ~300,000 more set-based.
/// </summary>
public sealed partial class HistoricalDataSeeder(ApexPostDbContext db, BusinessRules rules, TimeProvider clock, ILogger<HistoricalDataSeeder> logger)
{
    public async Task<int> SeedAsync(int days, int transactionsPerBranchPerDay, CancellationToken cancellationToken)
    {
        if (await db.CounterTransactions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        // All or nothing: a failure part-way must not leave a few days of history behind, because
        // the "is it empty?" check above would then skip seeding for ever. The execution strategy
        // owns the transaction so a transient-fault retry re-runs the whole seed from scratch.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async ct =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var seeded = await SeedHistoryAsync(days, transactionsPerBranchPerDay, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return seeded;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> SeedHistoryAsync(int days, int transactionsPerBranchPerDay, CancellationToken cancellationToken)
    {
        var random = new Random(20261030);
        var branches = await db.Branches.AsNoTracking().Include(b => b.Counters).Where(b => b.Status == BranchStatus.Active).ToListAsync(cancellationToken).ConfigureAwait(false);
        var clerks = await db.Staff.AsNoTracking().Where(s => s.IsActive && (s.Role == StaffRole.CounterClerk || s.Role == StaffRole.BranchManager)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);
        var zones = await db.ShippingZones.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var total = 0;
        var serial = 0;

        for (var day = days; day >= 1; day--)
        {
            var date = today.AddDays(-day);
            var pendingShipments = new List<(CounterTransaction Tx, Func<int, Shipment> Create)>();
            var pendingVerifications = new List<(CounterTransaction Tx, Func<int, IdentityVerification> Create, MoneyOrder Order)>();

            foreach (var branch in branches)
            {
                var staff = clerks.Where(c => c.HomeBranchCode == branch.Code).ToList();
                var manager = staff.FirstOrDefault(s => s.Role == StaffRole.BranchManager) ?? staff[0];
                var counters = branch.Counters.Where(c => c.IsActive).Select(c => c.Number).ToList();
                var count = transactionsPerBranchPerDay + random.Next(-2, 3);

                for (var i = 0; i < count; i++)
                {
                    var openedAt = new DateTimeOffset(date.AddHours(8.5 + random.NextDouble() * 8.5), TimeSpan.Zero);
                    var clerk = staff[random.Next(staff.Count)];
                    var tx = CounterTransaction.Open(branch.Code, counters[random.Next(counters.Count)], clerk.Id, openedAt);

                    foreach (var product in products.OrderBy(_ => random.Next()).Take(random.Next(1, 4)))
                    {
                        tx.AddRetailItem(product.Sku, product.Name, product.UnitPrice, TaxRules.CategoryFor(TransactionLineType.RetailItem, product.TaxCategory), random.Next(1, 4), TaxRules.TaxOn);
                    }

                    // About a third of baskets also post a parcel or letter.
                    if (random.NextDouble() < 0.35)
                    {
                        var zone = zones[random.Next(zones.Count)];
                        var parcel = random.NextDouble() < 0.7;
                        var type = parcel ? ShippingType.Parcel : ShippingType.Letter;
                        var level = parcel ? (ShippingServiceLevel)random.Next(0, 3) : ShippingServiceLevel.Standard;
                        var weight = new Weight(parcel ? Math.Round(0.2m + (decimal)random.NextDouble() * 4.5m, 3) : 0.1m);
                        var dims = parcel ? new Dimensions(30, 20, 10 + random.Next(0, 20)) : new Dimensions(24, 16, 0.5m);
                        var price = (parcel ? zone.ParcelBaseRate + zone.PerKgRate * Math.Ceiling(weight.Kilograms) : zone.LetterBaseRate).RoundedToCents;
                        var tracking = $"APX{date:yyMMdd}{++serial:000000}";
                        var taxCategory = TaxRules.CategoryFor(TransactionLineType.Shipping, isInternational: zone.IsInternational);
                        tx.AddServiceLine(TransactionLineType.Shipping, tracking, $"{type} {level} to {zone.Name}", price, taxCategory, TaxRules.TaxOn(price, taxCategory), Money.Zero);
                        var recipient = new Address("Sample Recipient", $"{random.Next(1, 300)} Example Street", "Sampleton", "NSW", "2000");
                        var at = openedAt;

                        // Copy the serial NOW: the lambda runs after the day's commit, and capturing the
                        // variable itself would give every shipment of the day the same tracking number.
                        var trackingSerial = serial;
                        pendingShipments.Add((tx, txId => Shipment.Create(txId, branch.Code, type, level, zone.ZoneCode, weight, dims, Weight.Max(weight, dims.VolumetricWeight), recipient, price, at, trackingSerial)));
                    }

                    // About one in ten buys a money order; big ones get an identity check first.
                    MoneyOrder? order = null;
                    if (random.NextDouble() < 0.1)
                    {
                        var amount = new Money(random.NextDouble() < 0.3 ? 1000 + random.Next(1, 3000) : 50 + random.Next(1, 900));
                        order = MoneyOrder.CreatePending(tx, amount, rules.MoneyOrderFee(amount), rules.MaximumMoneyOrderAmount, "Sample Sender", "Sample Payee", openedAt, ++serial % 10000);
                        var taxCategory = TaxRules.CategoryFor(TransactionLineType.MoneyOrder);
                        tx.AddServiceLine(TransactionLineType.MoneyOrder, order.SerialNumber, $"Money order {order.Amount}", order.Fee, taxCategory, TaxRules.TaxOn(order.Fee, taxCategory), order.Amount);
                        db.MoneyOrders.Add(order);

                        if (rules.RequiresIdentityVerification(amount))
                        {
                            var reference = $"IDV-{date:yyMMdd}-{++serial:000000}";
                            var fee = rules.IdentityVerificationFee;
                            var idvTax = TaxRules.CategoryFor(TransactionLineType.IdentityVerification);
                            tx.AddServiceLine(TransactionLineType.IdentityVerification, reference, "Identity check", fee, idvTax, TaxRules.TaxOn(fee, idvTax), Money.Zero);
                            var clerkId = clerk.Id;
                            var at = openedAt;
                            var docType = (DocumentType)random.Next(0, 3);
                            var number = $"X{random.Next(10_000_000, 99_999_999)}";
                            var referenceSerial = serial; // copied for the same reason as trackingSerial
                            pendingVerifications.Add((tx, txId => IdentityVerification.Record(txId, branch.Code, clerkId, "Sample Customer", docType, number, VerificationResult.Passed, at, referenceSerial), order));
                        }
                    }

                    var closedAt = openedAt.AddMinutes(random.Next(1, 6));
                    tx.Pay((PaymentMethod)random.Next(0, 3), closedAt);
                    order?.Issue(closedAt);

                    var roll = random.NextDouble();
                    if (roll < 0.02)
                    {
                        tx.Void("Customer changed their mind", manager.Id, closedAt.AddMinutes(2));
                        order?.Cancel();
                    }
                    else if (roll < 0.035)
                    {
                        tx.Refund("Item returned damaged", manager.Id, closedAt.AddHours(1));
                        order?.Cancel();
                    }

                    db.CounterTransactions.Add(tx);
                    total++;
                }
            }

            // One commit per day keeps the change tracker small; shipments and verifications need
            // the generated transaction ids, so they follow in a second commit for the same day.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (tx, create) in pendingShipments)
            {
                var shipment = create(tx.Id);
                if (tx.Status == TransactionStatus.Paid)
                {
                    shipment.Lodge();
                }
                else
                {
                    shipment.Cancel();
                }

                db.Shipments.Add(shipment);
            }

            foreach (var (tx, create, _) in pendingVerifications)
            {
                db.IdentityVerifications.Add(Persistence.IdentityVerificationMapper.ToRecord(create(tx.Id)));
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
        }

        // Link high-value money orders to their verification (set-based, by transaction).
        await db.Database.ExecuteSqlAsync($"""
            UPDATE m SET m.IdentityVerificationId = v.Id
            FROM dbo.MoneyOrders AS m
            JOIN dbo.IdentityVerifications AS v ON v.TransactionId = m.TransactionId
            WHERE m.IdentityVerificationId IS NULL AND v.ResultCode = 'PASS'
            """, cancellationToken).ConfigureAwait(false);

        LogSeeded(total, days);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} historical transactions over {Days} days")]
    private partial void LogSeeded(int count, int days);
}
