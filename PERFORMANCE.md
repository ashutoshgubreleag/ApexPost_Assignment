# Performance: indexes and execution plans

This document records how the five reporting indexes (migration `AddReportingIndexes`, ADR-W3-02)
and two query rewrites were chosen, and what they changed, measured on real execution plans.

> Every number below was measured by `scripts/run-perf.ps1` on 2026-10-31 and copied from
> `database/perf/results/summary.md`. The **actual** execution plans are committed in
> `database/perf/results/before/` and `after/` (`.sqlplan`, open in SSMS or Azure Data Studio), with
> the raw `STATISTICS IO/TIME` output next to each plan (`.txt`). Nothing here is estimated.

## 1. Method

| Item | Value |
| --- | --- |
| Database | Separate `ApexPostPerf` database built from `database/schema.sql` (same migrations as the app) |
| Volume | `database/perf/00-generate-volume.sql`, `@Rows = 300000`: 300,000 transactions over 730 days, 450,000 transaction lines, 99,999 shipments and 48,667 money orders, spread evenly over the six branches, ten zones and three service levels. Deterministic and fictional |
| Before | `10-drop-reporting-indexes.sql` (only the PK/FK/unique indexes from `InitialCreate` remain), statistics updated |
| After | `20-create-reporting-indexes.sql` (identical definitions to the migration), statistics updated |
| Measurement | Seven read workloads (W1–W5) and two write workloads (W6–W7); `SET STATISTICS IO, TIME ON`; one warm-up run then 3 measured runs per workload; logical reads (deterministic) and median CPU / elapsed time |
| Plans | `SET STATISTICS XML ON` on the last run: **actual** execution plans saved as `database/perf/results/{before,after}/<workload>.sqlplan` |
| Parameters | Every workload is sent as a parameterized `SqlCommand` (values from the `--@` header lines), the same way EF Core sends them |

Logical reads are the primary metric: they do not depend on the machine or on cache state.
CPU and elapsed time are reported for context.

Run it yourself:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-perf.ps1               # default instance "."
powershell -ExecutionPolicy Bypass -File scripts\run-perf.ps1 -Rows 100000  # quicker
```

## 2. Workloads

| Id | Used by | Query (file in `database/perf/workloads`) | Hypothesis |
| --- | --- | --- | --- |
| W1a | Dashboard, original form | `W1a-daily-sales-original.sql`: `YEAR(ClosedAtUtc) = YEAR(@Day) AND MONTH(...) AND DAY(...)` | Functions on the column are not SARGable: clustered index scan of all transactions, before and after |
| W1b | Dashboard / `usp_DailyBranchSales` | `W1b-daily-sales-rewritten.sql`: `BusinessDate = @Day` | Before: scan (no index on the computed column). After: **seek** on `IX_CounterTransactions_Branch_BusinessDate`, covered by its INCLUDE columns, no key lookup |
| W2a | Monthly revenue, original form | `W2a-monthly-revenue-original.sql`: `YEAR(...) = @Year AND MONTH(...) = @Month` | Scan before and after |
| W2b | Monthly revenue (`ReportQueries`) | `W2b-monthly-revenue-rewritten.sql`: half-open range `ClosedAtUtc >= @From AND < @To` | After: **range seek** on the filtered `IX_CounterTransactions_ClosedAt_Paid` (only paid rows are in the index) |
| W3 | Shipping volume report | `W3-shipping-volume.sql`: zone + service level + quarter | After: seek on `IX_Shipments_Zone_Level_QuotedAt`, covered |
| W4 | Money order audit (auditor view) | `W4-money-order-audit.sql`: branch + month, issued/cancelled | After: seek on the filtered `IX_MoneyOrders_Branch_RequestedAt_Audited`, covered |
| W5 | Low-stock report | `W5-low-stock.sql`: `EXEC usp_LowStockReport @BranchCode, @Threshold` | After: seek on `IX_BranchStock_Branch_Covering`. BranchStock has only 192 rows (6 branches × 32 SKUs), so the gain is small in absolute terms; the index exists for the per-branch stock screen that runs on every page load |

## 3. Index design notes

| Index | Key | INCLUDE / filter | Why this shape |
| --- | --- | --- | --- |
| `IX_CounterTransactions_Branch_BusinessDate` | `(BranchCode, BusinessDate)` | INCLUDE `Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount` | Equality on both key columns; the dashboard reads only totals, so the INCLUDE list removes every key lookup. `BusinessDate` is a persisted computed column so the predicate is SARGable and the value is stored, not computed per row |
| `IX_CounterTransactions_ClosedAt_Paid` | `(ClosedAtUtc)` | INCLUDE `BranchCode, TotalAmount`, **WHERE Status = 'Paid'** | Range queries over time across all branches; ~92% of rows are paid, but voids and open baskets are excluded and the index is narrow. The query must repeat `Status = 'Paid'` for the optimizer to match the filter |
| `IX_Shipments_Zone_Level_QuotedAt` | `(ZoneCode, ServiceLevel, QuotedAtUtc DESC)` | INCLUDE `Status, WeightKg, Price, BranchCode` | Two equality columns first, then the range column; DESC matches "most recent first" lists |
| `IX_MoneyOrders_Branch_RequestedAt_Audited` | `(BranchCode, RequestedAtUtc)` | INCLUDE `Amount, Fee, Status, SerialNumber, IdentityVerificationId`, **WHERE Status IN ('Issued','Cancelled')** | The audit view never shows pending orders |
| `IX_BranchStock_Branch_Covering` | `(BranchCode)` | INCLUDE `Sku, QuantityOnHand, ReorderThreshold` | The stock screen and low-stock report read one branch |

Write cost: each index adds work to inserts and updates of its table. The counter's hot path
(`PayAsync`) inserts one transaction and touches a few stock rows; two extra index rows per
transaction is a small cost next to the round trip. Indexes that would only serve ad-hoc queries
were not added.

## 4. Results

Environment: SQL Server 2025 (17.00.1000) Enterprise Evaluation, default instance, Windows 11
(10.0.22000) developer PC. Volume: 300,000 `CounterTransactions`, 450,000 `TransactionLines`,
99,999 `Shipments`, 48,667 `MoneyOrders`, 192 `BranchStock`.

| Workload | Logical reads before | Logical reads after | Change | CPU ms before → after | Elapsed ms before → after | Rows | Plan before → after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| W1a daily sales, original `YEAR/MONTH/DAY` | 5,523 | 1,650 | −70% | 62 → 63 | 62 → 60 | 1 | Clustered index scan → scan of the narrower filtered index + seek (index intersection) |
| **W1b daily sales, rewritten `BusinessDate = @Day`** | 5,523 | **5** | **−99.9%** | 32 → 0 | 38 → 0 | 1 | Clustered index scan → **Index Seek** `IX_CounterTransactions_Branch_BusinessDate` |
| W2a monthly revenue, original `YEAR/MONTH` | 5,523 | 1,240 | −78% | 109 → 63 | 111 → 75 | 6 | Clustered index scan → **Index Scan** of `IX_CounterTransactions_ClosedAt_Paid` |
| **W2b monthly revenue, rewritten range** | 5,523 | **57** | **−99.0%** | 78 → 16 | 67 → 9 | 6 | Clustered index scan → **Index Seek** `IX_CounterTransactions_ClosedAt_Paid` |
| W3 shipping volume (zone + level + quarter) | 1,932 | 8 | −99.6% | 16 → 0 | 20 → 1 | 6 | Clustered index scan → **Index Seek** `IX_Shipments_Zone_Level_QuotedAt` |
| W4 money-order audit (branch + month) | 1,041 | 7 | −99.3% | 15 → 0 | 11 → 1 | 342 | Clustered index scan → **Index Seek** `IX_MoneyOrders_Branch_RequestedAt_Audited` |
| W5 low stock (`usp_LowStockReport`) | 56 | 54 | −4% | 15 → 0 | 9 → 5 | 13 | Clustered index scan → **Index Seek** `IX_BranchStock_Branch_Covering` |

CPU and elapsed times are medians of three warm runs. At this volume they are in the tens of
milliseconds, so the 1 ms timer resolution makes "0" mean "under a millisecond". Logical reads are
the reliable comparison.

### 4.1 The rewrite matters as much as the index

The original and rewritten queries return the same rows. With all indexes present:

| | Operator (from the `.sqlplan`) | Logical reads |
| --- | --- | ---: |
| W2a `YEAR(ClosedAtUtc) = @Year AND MONTH(ClosedAtUtc) = @Month` | **Index Scan**: every row of `IX_CounterTransactions_ClosedAt_Paid` is read and `YEAR()`/`MONTH()` evaluated (est. 17,356 rows, actual 11,819) | 1,240 |
| W2b `ClosedAtUtc >= @From AND ClosedAtUtc < @To` | **Index Seek** with a range seek predicate on the same index (est. 11,953, actual 11,819) | 57 |
| W1a `YEAR()/MONTH()/DAY()` | Scan of the filtered index, then joined to a seek on the branch index: 46,000 rows touched to return 67 | 1,650 |
| W1b `BusinessDate = @Day` | **Index Seek** on (`BranchCode`, `BusinessDate`) (est. 186, actual 67) | 5 |

Wrapping the column in a function hides it from the index: SQL Server can only scan and compute.
The new index made the scan smaller (narrower rows, paid rows only), but only the SARGable
rewrite turns it into a seek: **22× fewer reads for W2, 330× for W1**. That is why the application
queries use `BusinessDate` (persisted computed column) and half-open ranges, never `YEAR()`.

### 4.2 Reading the "after" plans

| Workload | Seek on | Key lookup? | Estimated vs actual rows | Missing-index hint |
| --- | --- | --- | --- | --- |
| W1b | `IX_CounterTransactions_Branch_BusinessDate` (BranchCode = @BranchCode, BusinessDate = @Day); `Status = 'Paid'` checked as a residual predicate on the INCLUDE column | No (covered) | 186 / 67 | none (before: 99.9% impact) |
| W2b | `IX_CounterTransactions_ClosedAt_Paid` range on ClosedAtUtc; the index filter already guarantees `Status = 'Paid'` | No (covered) | 11,953 / 11,819 | none (before: 89.1%) |
| W3 | `IX_Shipments_Zone_Level_QuotedAt` (ZoneCode, ServiceLevel equality + QuotedAtUtc range) | No (covered) | 2,698 / 405 | none (before: 93.4%) |
| W4 | `IX_MoneyOrders_Branch_RequestedAt_Audited` (BranchCode equality + RequestedAtUtc range); filtered on Issued/Cancelled | No (covered) | 343 / 342 | none (before: 90.9%) |
| W5 | `IX_BranchStock_Branch_Covering`, then `UQ_Products_Sku` for the product name | No | 18 / 13 | none |

* Every "before" plan carried a missing-index recommendation (impact 88–99.9%), and every "after"
  plan has none: the five indexes are exactly what the optimizer asked for, and nothing more.
* Estimates are close to actuals except W3 (2,698 vs 405). The optimizer assumes zone, level and
  date are independent. The plan shape is still the right one (a seek returning a few hundred
  rows), so no hint or statistics change was made.
* **W5 is honest about scale**: `BranchStock` has 192 rows, so the scan was already cheap (56 vs
  54 reads). The index is kept because the per-branch stock screen runs on every page load and
  the table grows with every new SKU. The procedure's `OPTION (RECOMPILE)` lets the plan seek for
  one branch (shown) and scan when `@BranchCode` is NULL.

### 4.3 Costs: write overhead and storage

Each index is extra work on every insert/update and extra storage. Two write workloads measure it
the same way as the reads (before = without the reporting indexes, after = with them):

| Workload | What it does |
| --- | --- |
| W6 write transactions | Inserts 20,000 paid `CounterTransactions` in one statement, then rolls back (2 reporting indexes to maintain) |
| W7 write shipments | Inserts 20,000 `Shipments`, then rolls back (1 wide covering index to maintain) |

_Write-cost and index-size numbers: pending the next `run-perf.ps1` run (W6/W7 and the storage table
were added after the read measurements above). They will be copied here from `summary.md`._

Context for the decision: the hot write path (`PayAsync`) inserts one transaction and updates a few
stock rows, so per sale the extra cost is a handful of index rows, while the reads it speeds up run
on every dashboard load and report.

## 5. EF Core query shape

Captured on SQL Server by the integration tests, file
`database/perf/results/RecentTransactions.sql`:

```sql
DECLARE @p int = 20;
DECLARE @branch varchar(10) = 'BR-001';

SELECT TOP(@p) [c].[Id], [c].[BranchCode], [c].[CounterNumber], [c].[ClerkStaffId], [c].[Source], [c].[Status], (
    SELECT COALESCE(SUM([t].[Quantity]), 0)
    FROM [dbo].[TransactionLines] AS [t]
    WHERE [c].[Id] = [t].[TransactionId]), [c].[Subtotal], [c].[TaxAmount], [c].[PassThroughAmount], [c].[TotalAmount], (
    SELECT TOP(1) [p].[Method]
    FROM [dbo].[Payments] AS [p]
    WHERE [c].[Id] = [p].[TransactionId]
    ORDER BY [p].[Id] DESC), [c].[VoidReason], [c].[AuthorisedByStaffId], [c].[OpenedAtUtc], [c].[ClosedAtUtc]
FROM [dbo].[CounterTransactions] AS [c]
WHERE [c].[BranchCode] = @branch
ORDER BY [c].[OpenedAtUtc] DESC
```

* `TransactionQueries.RecentQuery(...).ToQueryString()`: one statement, a projection of only the
  columns the list shows, `TOP(@p)` and `@branch` as parameters (the `DECLARE` lines are how
  `ToQueryString` displays parameter values; the statement itself never inlines them). The line
  count and the last payment method are correlated sub-queries in the same statement, not extra
  round trips.
* `Read_queries_use_a_fixed_number_of_commands_no_n_plus_one` counts commands with an interceptor:
  the list of 5 transactions is **1** command; the details view is **6** commands (3 split queries
  for header/lines/payments, then shipments, money orders and verifications) for a 1-line and a
  6-line transaction alike, so it does not grow with the data.
* In Development, `Database:LogSqlCommands` logs every command with its parameters.
