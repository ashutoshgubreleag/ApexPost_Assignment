# ApexPost performance run

- Date: 2026-11-01 14:22
- Server: SQL Server 17.00.1000 ('.'), database ApexPostPerf
- Volume: CounterTransactions 300000, TransactionLines 450000, Shipments 99999, MoneyOrders 48667, BranchStock 192
- Each workload: 1 warm-up + 3 measured runs; CPU/elapsed are medians; logical reads from the last run.

| Workload | Reads before | Reads after | CPU ms before | CPU ms after | Elapsed ms before | Elapsed ms after | Rows |
|---|---:|---:|---:|---:|---:|---:|---:|
| W1a-daily-sales-original | 5523 | 1651 | 62 | 62 | 72 | 64 | 1 |
| W1b-daily-sales-rewritten | 5523 | 4 | 31 | 0 | 34 | 0 | 1 |
| W2a-monthly-revenue-original | 5523 | 1241 | 79 | 62 | 88 | 68 | 6 |
| W2b-monthly-revenue-rewritten | 5523 | 57 | 47 | 0 | 53 | 8 | 6 |
| W3-shipping-volume | 1932 | 8 | 16 | 0 | 15 | 1 | 6 |
| W4-money-order-audit | 1041 | 7 | 16 | 0 | 10 | 1 | 342 |
| W5-low-stock | 56 | 54 | 16 | 15 | 13 | 7 | 13 |
| W6-write-transactions | 286406 | 415485 | 750 | 1266 | 807 | 1733 | 0 |
| W7-write-shipments | 423501 | 488427 | 1016 | 1250 | 1135 | 1372 | 0 |

W6/W7 are write workloads (insert 20,000 rows, then roll back): the 'after' minus 'before' difference is the index maintenance cost.

## Index storage (after)

| Table | Index | Used KB | Rows |
|---|---|---:|---:|
| BranchStock | PK_BranchStock | 32 | 192 |
| BranchStock | IX_BranchStock_Branch_Covering | 16 | 192 |
| CounterTransactions | PK_CounterTransactions | 44200 | 300000 |
| CounterTransactions | IX_CounterTransactions_Branch_BusinessDate | 34264 | 300000 |
| CounterTransactions | IX_CounterTransactions_ClosedAt_Paid | 17672 | 276000 |
| MoneyOrders | PK_MoneyOrders | 8328 | 48667 |
| MoneyOrders | IX_MoneyOrders_Branch_RequestedAt_Audited | 3528 | 48667 |
| Shipments | PK_Shipments | 27616 | 99999 |
| Shipments | IX_Shipments_Zone_Level_QuotedAt | 13192 | 99999 |
