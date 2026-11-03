# SQL scripts (Week 3, Part B)

Standalone T-SQL for `ApexPostDb`: the reporting queries, a sale as one T-SQL transaction, and a
blocking demo, plus a note on avoiding deadlocks. They run against the database the migrations
create (README §1), with its fictional seed data. Scripts 01–05 change nothing. Script 05 rolls
back unless you set `@Commit = 1`. The blocking demo rolls back its one update.

| Script | What it shows | Key techniques |
| --- | --- | --- |
| [01-daily-sales-by-branch.sql](01-daily-sales-by-branch.sql) | Sales per branch for one business date, **including branches with no sales** | `LEFT JOIN` from `Branches` with the filters in `ON` (not `WHERE`), `COUNT(column)` vs `COUNT(*)`, `COALESCE`, SARGable `BusinessDate` |
| [02-top-products-by-revenue-per-region.sql](02-top-products-by-revenue-per-region.sql) | **Top 10 products by revenue in each region** | CTEs, `ROW_NUMBER()` / `RANK() OVER (PARTITION BY Region ORDER BY Revenue DESC)`, window `SUM` for share of region; `CROSS APPLY TOP (n)` alternative |
| [03-shipping-volume-by-zone.sql](03-shipping-volume-by-zone.sql) | Lodged shipments per zone and service level for a month, with zone totals | `LEFT JOIN` from `ShippingZones`, `GROUPING SETS`, `GROUPING()`, half-open date range |
| [04-money-order-audit.sql](04-money-order-audit.sql) | Money orders per branch and identity checks; exceptions (orders above the threshold without a passed check) | pre-aggregated derived tables (no double counting), masked identity data |
| [05-sale-transaction.sql](05-sale-transaction.sql) | **A complete sale in one transaction**: header, lines, stock, totals, payment | `SET XACT_ABORT ON`, `BEGIN TRAN`, `TRY/CATCH`, **`XACT_STATE()`**, `THROW`, `UPDLOCK, HOLDLOCK`; four scenarios: success, not enough stock, CHECK violation (doomed transaction), invalid input |
| [06-blocking-demo-window1-blocker.sql](06-blocking-demo-window1-blocker.sql) | **SSMS blocking demo, window 1**: an open transaction holding an X lock | |
| [07-blocking-demo-window2-blocked.sql](07-blocking-demo-window2-blocked.sql) | Window 2: a reader that waits; lock timeout (1222); writer vs writer; why not `NOLOCK` | `SET LOCK_TIMEOUT` |
| [08-blocking-demo-window3-monitor.sql](08-blocking-demo-window3-monitor.sql) | Window 3: who blocks whom, wait types, locks held and requested, head blocker | `sys.dm_exec_requests`, `sys.dm_tran_locks`, `sys.dm_exec_input_buffer` |
| [DEADLOCKS.md](DEADLOCKS.md) | **Deadlock-avoidance note**: how a deadlock forms, the six rules ApexPost follows, retry on 1205, reading the deadlock graph | |

## Running them

* **SSMS:** connect to `.` (tick *Trust server certificate*), open the script, pick `ApexPostDb` and
  press F5. Results appear in the Results tab and `PRINT` output in the Messages tab.
* **Command line:**

  ```powershell
  sqlcmd -S . -d ApexPostDb -E -C -i .\sql\02-top-products-by-revenue-per-region.sql
  ```

* **Blocking demo:** open 06, 07 and 08 in three query windows and run them in that order. Then go
  back to window 1 and run its `ROLLBACK TRANSACTION`.

## How they are tested

`ApexPost.IntegrationTests/SqlFolderScriptTests.cs` runs scripts 01–05 against a fresh, migrated
test database (split on `GO` like SSMS does). It checks four things:

* every script runs without error;
* the daily report returns one row per branch;
* the top-10 report never returns more than 10 rows for a region;
* the sale script leaves stock and transactions exactly as they were.

The blocking scripts are interactive and are run by hand.
