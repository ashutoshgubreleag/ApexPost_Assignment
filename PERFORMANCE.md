# Performance: indexes and execution plans

This document records how the five reporting indexes (migration `AddReportingIndexes`, ADR-W3-02)
and two query rewrites were chosen, and what they changed, measured on real execution plans.

> **Status:** the method, workloads and scripts are final. The measured numbers in §4 come from
> `scripts/run-perf.ps1` on the reference PC (SQL Server 2025) and are pasted from
> `database/perf/results/summary.md`; the actual plans are committed next to it as `.sqlplan` files.
> Numbers marked _pending_ have not been measured yet: nothing in this file is estimated.

## 1. Method

| Item | Value |
| --- | --- |
| Database | Separate `ApexPostPerf` database built from `database/schema.sql` (same migrations as the app) |
| Volume | `database/perf/00-generate-volume.sql`, `@Rows = 300000`: 300k transactions over 730 days, 450k transaction lines (1.5 per transaction), 100k shipments, ~48k money orders. Deterministic and fictional |
| Before | `10-drop-reporting-indexes.sql` (only the PK/FK/unique indexes from `InitialCreate` remain), statistics updated |
| After | `20-create-reporting-indexes.sql` (identical definitions to the migration), statistics updated |
| Measurement | `SET STATISTICS IO, TIME ON`; one warm-up run then 3 measured runs per workload; logical reads (deterministic) and median CPU / elapsed time |
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

Environment: _pending_ (filled from `summary.md`: SQL Server version, machine, row counts).

| Workload | Logical reads before | Logical reads after | CPU ms before | CPU ms after | Elapsed ms before | Elapsed ms after | Plan before → after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| W1a original (YEAR/MONTH/DAY) | _pending_ | _pending_ | | | | | |
| W1b rewritten (BusinessDate) | _pending_ | _pending_ | | | | | |
| W2a original (YEAR/MONTH) | _pending_ | _pending_ | | | | | |
| W2b rewritten (range) | _pending_ | _pending_ | | | | | |
| W3 shipping volume | _pending_ | _pending_ | | | | | |
| W4 money order audit | _pending_ | _pending_ | | | | | |
| W5 low stock (procedure) | _pending_ | _pending_ | | | | | |

### 4.1 The rewrite: `YEAR()` versus a range

_pending: compare W2a-after with W2b-after (same indexes present), quoting the operator from each
`.sqlplan` (scan versus seek) and the logical reads._

### 4.2 Reading the plans

_pending: for each "after" plan, the seek predicate, whether a key lookup remains, estimated
versus actual rows._

## 5. EF Core query shape

* `ApexPost.IntegrationTests` writes the SQL of the recent-transactions list
  (`TransactionQueries.RecentQuery(...).ToQueryString()`) to `sql-evidence/RecentTransactions.sql`;
  it is a single parameterized `SELECT TOP(@p) ... ORDER BY` with a projection (no `SELECT *`).
* `Read_queries_use_a_fixed_number_of_commands_no_n_plus_one` counts SQL commands with an
  interceptor: the transaction list is **1** command for 5 transactions with 10 lines; the details
  view is a fixed number of split queries regardless of the line count.
* In Development, `Database:LogSqlCommands` logs every command with its parameters.
