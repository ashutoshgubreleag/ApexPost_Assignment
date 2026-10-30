# Week 3 demo script (about 10 minutes)

Two windows side by side: the web app (left) and the counter terminal (right). SSMS or Azure Data
Studio connected to `ApexPostDb` is useful for step 6. All data is fictional.

## 0. Setup (once)

Follow README §1–§2: create `ApexPostDb`, set the connection string and the same device key for
`ApexPost.MVC` (`Sync:Devices:TERM-BR001-01:Key`) and `ApexPost.CounterTerminal` (`Terminal:DeviceKey`).
To start the terminal from a clean local database, delete `ApexPost.CounterTerminal\bin\Debug\net10.0\terminal-data`.

## 1. Server on SQL Server

```powershell
dotnet run --project .\ApexPost.MVC --launch-profile https
```

* The log shows `Applying migration ...` on the first run (Development only) and the SQL commands
  with parameters (`Database:LogSqlCommands`).
* Sign in as `manager.br001@apexpost.local` / `ApexPost123!`. The dashboard and **Reports → Daily
  sales** run on SQL Server (history seeded for 60 days). The daily split comes from `usp_DailyBranchSales`.
* **Inventory → Adjust stock** (+5 for PKG-001): runs `usp_AdjustStock`. Try −500: the
  procedure's `THROW 50003` is shown as a friendly "stock insufficient" message.

## 2. Terminal online: first sync

```powershell
dotnet run --project .\ApexPost.CounterTerminal
```

```
> sync
Sync OK: pushed 0 (...); pulled 32 product(s), 32 stock row(s).
> catalog Packaging
> stock
```

## 3. Offline sales

```
> offline
Network OFF. Sales keep working and queue in the outbox.
Sync failed, sales kept in the outbox: Offline (network switched off at this terminal).
> sell PKG-001x2 PKG-002x1 pay=Card
> sell PKG-003x1
> mo 250 Ada Bo
> mo 1500 Ada Bo
Refused: Money orders above $1000.00 cannot be sold while the counter is offline ...
> sell PKG-002x9999
Refused: Only <n> x PKG-002 available at this counter.
> status
  Link: OFFLINE (switched off)
  Outbox: 3 pending       (two sales + one money order)
> pending
> today
```

Point out: the sale was saved to Couchbase Lite **before** any network call; `stock` shows
`sold-unsynced` per SKU; the change listener woke the sync worker, which failed and is backing off.

Close the terminal (`quit`) and start it again: the pending sales are still there (persistent database).

## 4. A price changes while the terminal is offline (conflict C1)

In the web app sign in as `admin@apexpost.local`, **Catalog → Revise prices**: Packaging, −20%,
reason "Demo". The terminal still has the old price. Back in the terminal (still offline):

```
> sell PKG-001x1
```

## 5. Reconnect

```
> online
Network ON. Syncing...
Sync OK: pushed 4 (accepted 4, warnings 1, rejected 0); pulled <n> product(s), <n> stock row(s).
> show <id of the last sale>
  warning: PKG-001 sold offline at $2.95 but the server price is $2.36 (25% variance); honoured at $2.95, review required.
> catalog Packaging                      (new prices pulled)
> stock                                  (sold-unsynced back to 0, server quantity reduced)
```

In the web app as `manager.br001`: **Reports → Sync conflicts** lists the price variance;
**Transactions** shows the offline sales with source `OfflineTerminal`; the dashboard shows the
notification.

## 6. Idempotency and the database

In SSMS:

```sql
SELECT DeviceId, LocalTransactionId, Outcome, ServerTransactionId FROM dbo.SyncInbox;
SELECT Id, Source, Status, TotalAmount, ClosedAtUtc, BusinessDate FROM dbo.CounterTransactions WHERE Source = 'OfflineTerminal';
SELECT * FROM dbo.SyncConflicts;
SELECT Reference, DocumentTypeCode, DocumentLast4 FROM dbo.IdentityVerifications;   -- masked
```

A duplicate push is shown by the tests (`Push_records_the_sale_and_a_retry_is_reported_as_duplicate`,
`Lost_response_then_retry_does_not_create_a_second_server_sale`).

## 7. Security in one minute

```powershell
curl.exe -k -i https://localhost:7052/api/sync/branch-snapshot                                   # 401 Problem Details
curl.exe -k -i -H "X-Device-Id: TERM-BR001-01" -H "X-Device-Key: wrong" https://localhost:7052/api/sync/branch-snapshot   # 401
```

## 8. Performance evidence

Open `database/perf/results/before/W1b-daily-sales-rewritten.sqlplan` and the `after` one in SSMS:
clustered index scan before, index seek on `IX_CounterTransactions_Branch_BusinessDate` after, no key
lookup. The numbers are in PERFORMANCE.md §4.
