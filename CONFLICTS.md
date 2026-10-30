# Offline sync conflict policies

A counter terminal can sell for hours without a network. When it reconnects, the server
(`OfflineSaleService`) applies each pushed sale through the normal domain rules and one unit of
work. These are the conflicts that can occur, what happens, and why. The guiding rule
(ADR-W3-11): **a sale that physically happened is recorded; a disagreement is flagged for a
person, never silently dropped or silently "fixed".**

Every conflict is stored in `SyncConflicts` (linked to the `SyncInbox` row of the sale), raises a
`SyncConflict` notification to the branch manager, is returned to the terminal as a warning
(`AcceptedWithWarnings`), and is listed under **Reports → Sync conflicts**.

## Summary

| # | Conflict | Policy | Result on the terminal | Server state |
| --- | --- | --- | --- | --- |
| C1 | Price changed on the server while the terminal was offline | **Honour the offline price**; flag if the difference is above `BusinessRules:OfflinePriceVarianceTolerance` (5%) | `Accepted`, or `AcceptedWithWarnings` + `PriceVariance` | Line recorded at the price the customer paid |
| C2 | Combined offline sales exceed server stock | **Accept and flag**; stock stops at 0 | `AcceptedWithWarnings` + `StockShortfall` | `QuantityOnHand = 0`, shortfall recorded for reconciliation |
| C3 | Same stock document changed on both sides | **Field-level ownership**: server-owned fields always come from the server, device-owned fields never leave the device | Local counters kept, server quantity replaced | Server never reads quantities, prices, ids or statuses from the device |
| C4 | Same sale pushed twice (lost response, retry, race) | **Idempotent** by unique `(DeviceId, LocalTransactionId)` | Original result returned, `Duplicate = true` | One transaction, one stock decrement |
| C5 | Sale breaks a rule only the server can check (unknown clerk, clerk of another branch, high-value money order, unknown SKU) | **Reject** | `Rejected` with the reason; not retried; units released locally | Inbox row with `Rejected`, no transaction |

## C1: price changed while offline

**Scenario.** At 09:00 the terminal pulls `PKG-001` at $2.95. At 10:00 an administrator revises
packaging prices by +10% (`Catalog → Revise prices`). The terminal is offline and sells two bags at
$2.95 at 10:30. At 11:00 it syncs.

**Options considered.**

1. *Server price wins*: re-price the sale at $3.25. Rejected: the customer paid $5.90 and left; the
   books would show money that was never taken.
2. *Reject the sale*: rejected for the same reason; the goods are gone.
3. **Device price wins, flag big differences** (chosen). The line is stored at $2.95 (the
   `UnitPrice` sent by the terminal); if `|offline − server| / server` exceeds the tolerance a
   `PriceVariance` conflict is recorded, so a manager can see a mis-configured terminal or a
   tampered price.

**Guard rails.** The server still validates that the SKU exists and is active, and the price is
range-checked by the command validator. Tax is recomputed by the server from its own tax rules.

**Tests.** `C1_price_changed_while_offline_is_honoured_and_flagged_above_tolerance`,
`C1_small_price_difference_is_accepted_without_a_warning`.

## C2: offline sales exceed server stock

**Scenario.** BR-001 has 3 medium boxes. Terminal A (offline) sells 2. Meanwhile a clerk at an
online counter sells 2. Terminal A syncs: the server has 1 left but 2 were sold.

**Options considered.**

1. *Reject the offline sale*: the boxes have left the shop; rejecting makes stock and cash both wrong.
2. *Allow negative stock*: rejected; `CK_BranchStock_QuantityOnHand_NonNegative` exists because
   negative stock breaks reorder reports and hides the real problem.
3. **Accept and flag** (chosen). `BranchStock.RemoveUpToAvailable` takes what is there (stock
   becomes 0) and returns the shortfall (1). A `StockShortfall` conflict ("PKG-002: 1 unit(s) sold
   offline at BR-001 were not in stock on the server; stock set to 0, manager to reconcile") is
   stored and the manager is notified. The usual cause is a delivery not yet booked in; the manager
   reconciles with a stock adjustment (`usp_AdjustStock`).

**Why it rarely happens.** The terminal itself refuses to sell more than
`serverQuantity − localSoldSinceSync`, so only concurrent sales on *other* counters can cause it.

**Tests.** `C2_offline_sales_beyond_stock_are_accepted_stock_stops_at_zero_and_shortfall_is_flagged`.

## C3: concurrent changes to the same document

The terminal's `stock::{Branch}::{Sku}` document is written by both sides: the server's snapshot
and the terminal's own sales. Instead of last-write-wins on the whole document, each field has
exactly one owner:

| Field | Owner | On pull |
| --- | --- | --- |
| `serverQuantity`, `reorderThreshold`, `serverSyncedAt` | server | replaced |
| `localSoldSinceSync`, `lastLocalSaleAt` | device | kept |

`StockMerge.Apply` implements the rule; `Available = serverQuantity − localSoldSinceSync`. When the
server confirms a sale, its units move from `localSoldSinceSync` to the server's count, so they are
never counted twice. In the other direction, the push payload (`OfflineSaleDto`) contains only
facts captured at the counter (what, how many, at what price, when, how paid); stock levels,
current prices, ids, statuses and audit columns are never sent, so the device cannot overwrite them.

The catalogue (`product::{Sku}`) is server-owned: a pull replaces it. Pending sales are
device-owned until the server answers; only the sync metadata (status, attempts, server id,
warnings) is updated afterwards.

**Tests.** `Pulled_stock_replaces_server_fields_but_keeps_device_owned_counters`,
`Reconnecting_pushes_the_outbox_and_marks_sales_synced`.

## C4: duplicates

At-least-once delivery is made exactly-once on the server:

* `SyncInbox` has `UQ_SyncInbox_Device_LocalTransaction (DeviceId, LocalTransactionId)` and is
  inserted in the **same** `SaveChanges` as the transaction, lines, payment and stock changes.
* A retry finds the existing inbox row and returns its stored result (`Duplicate = true`).
* Two pushes of the same sale at the same instant: the loser hits the unique index
  (SQL error 2627 → `DuplicateKeyException`), discards its changes and returns the winner's result.

**Tests.** `Offline_sale_is_recorded_once_and_a_retry_returns_the_same_result`,
`Push_records_the_sale_and_a_retry_is_reported_as_duplicate`,
`Lost_response_then_retry_does_not_create_a_second_server_sale` (terminal),
`Racing_pushes_of_the_same_offline_sale_create_one_transaction` (SQL Server).

## C5: rejected sales

The server does not trust the device blindly. A sale is rejected (recorded in `SyncInbox` with the
reason, no transaction created) when the clerk is not a clerk of the device's branch, a money order
is above the identity threshold, or a SKU is unknown. The terminal marks the sale `Rejected`, stops
retrying it, gives its units back to local stock and shows it in `pending`/`show` so a supervisor
can follow up.

**Tests.** `Offline_sale_from_a_clerk_of_another_branch_is_rejected_without_a_transaction`,
`High_value_money_order_from_an_offline_device_is_rejected`,
`Rejected_sale_is_not_retried_and_its_units_are_released`.
