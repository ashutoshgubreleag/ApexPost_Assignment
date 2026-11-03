# Deadlock avoidance

A **deadlock** happens when two transactions each hold a lock the other needs, so neither can go on.
Blocking (scripts 06–08) ends when the blocker commits; a deadlock never ends by itself. SQL Server
finds the cycle within about 5 seconds and picks one session as the **victim**. It rolls the victim
back and raises error **1205** ("Transaction (Process ID n) was deadlocked … Rerun the transaction").
The other session then finishes normally.

## How one happens: two sales, opposite order

| Step | Session 1 (sale of PKG-001, then PKG-002) | Session 2 (sale of PKG-002, then PKG-001) |
| --- | --- | --- |
| 1 | `BEGIN TRAN; UPDATE BranchStock … Sku='PKG-001'`: **X lock on PKG-001** | |
| 2 | | `BEGIN TRAN; UPDATE BranchStock … Sku='PKG-002'`: **X lock on PKG-002** |
| 3 | `UPDATE … Sku='PKG-002'`: **waits** for session 2 | |
| 4 | | `UPDATE … Sku='PKG-001'`: **waits** for session 1, which is a **cycle** |
| 5 | one of them gets error 1205 and is rolled back | |

To reproduce it, run each column in its own SSMS window, one step at a time:

```sql
-- window 1                                              -- window 2
BEGIN TRAN;                                              BEGIN TRAN;
UPDATE dbo.BranchStock SET QuantityOnHand -= 1            UPDATE dbo.BranchStock SET QuantityOnHand -= 1
 WHERE BranchCode='BR-001' AND Sku='PKG-001';              WHERE BranchCode='BR-001' AND Sku='PKG-002';
UPDATE dbo.BranchStock SET QuantityOnHand -= 1            UPDATE dbo.BranchStock SET QuantityOnHand -= 1
 WHERE BranchCode='BR-001' AND Sku='PKG-002';              WHERE BranchCode='BR-001' AND Sku='PKG-001';
ROLLBACK;  -- the survivor                                -- one window shows Msg 1205
```

## How ApexPost avoids deadlocks

1. **Same lock order everywhere.** Code that locks several stock rows locks them in **one
   statement**, in index-key order of `UQ_BranchStock_Branch_Sku` (`sql/05-sale-transaction.sql`
   step 2a). Two sales that share SKUs then request the shared rows in the same order: the second
   one waits (blocking), but no cycle can form. `usp_AdjustStock` locks exactly one row.
2. **Lock for update when you read to write.** A read that is followed by an update of the same row
   takes `UPDLOCK, HOLDLOCK` (`usp_AdjustStock`, the T-SQL sale). Two shared locks cannot both turn
   into an exclusive lock on the same row, the classic conversion deadlock, because `U` locks are
   not compatible with each other: the second reader waits at the read instead.
3. **Short transactions, nothing slow inside.** Each application use case is one `SaveChanges`.
   The basket is read without locks, all changes are sent in one batch, and the transaction ends.
   No user input, HTTP call or `Task.Delay` happens while locks are held. Fewer locks held for less
   time means fewer chances to collide.
4. **Optimistic concurrency instead of long locks.** `BranchStock` and `CounterTransactions` have a
   `rowversion`. Two clerks editing the same basket do not lock each other out. The second save gets
   a concurrency conflict, `ConcurrencyRetry` re-runs the use case on fresh data, and after the last
   attempt the clerk gets HTTP 409.
5. **Indexes that match the predicates.** Updates and reports seek on
   `UQ_BranchStock_Branch_Sku`, `IX_CounterTransactions_Branch_BusinessDate` and the others in
   PERFORMANCE.md, so a statement locks only the rows it needs. A scan would lock (and wait for)
   rows it does not even change.
6. **Readers do not fight writers for reports.** Reports run as short single statements. If
   reporting load grows, `READ_COMMITTED_SNAPSHOT` lets readers use row versions instead of shared
   locks. That change is deliberately not made yet, because it moves version-store load to tempdb.
   `NOLOCK` is never used: it gives dirty reads of money and stock.

## If one still happens

* **The application retries.** The EF Core connection uses `EnableRetryOnFailure`
  (`Database:MaxRetryCount`, default 3). SQL Server's transient-error list includes 1205, and a
  use case that runs in an explicit transaction goes through `UnitOfWork.ExecuteInTransactionAsync`,
  which wraps the **whole** transaction in the execution strategy. A deadlock victim is therefore
  re-run from the start on fresh data, not half-retried.
* **A T-SQL caller retries.** The pattern is: catch `ERROR_NUMBER() = 1205`, wait a few
  milliseconds (`WAITFOR DELAY '00:00:00.050'`), and run the whole transaction again, at most 3
  times.
* **Find out why.** Every deadlock graph is kept by the built-in `system_health` Extended Events
  session:

```sql
SELECT  CAST(event_data AS xml).value('(event/@timestamp)[1]', 'datetime2') AS DeadlockAtUtc,
        CAST(event_data AS xml).query('(event/data/value/deadlock)[1]')    AS DeadlockGraph   -- open it in SSMS as .xdl
FROM sys.fn_xe_file_target_read_file('system_health*.xel', NULL, NULL, NULL)
WHERE object_name = 'xml_deadlock_report'
ORDER BY DeadlockAtUtc DESC;
```

  The graph names both statements, the locks held and requested, and the victim. The fix is almost
  always rule 1 (make the lock order consistent) or rule 5 (add the index that turns a scan into a
  seek).
