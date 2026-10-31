# ApexPost performance run

- Date: 2026-10-31 13:43
- Server: SQL Server 17.00.1000 ('.'), database ApexPostPerf
- Volume: CounterTransactions 300000, TransactionLines 450000, Shipments 99999, MoneyOrders 48667, BranchStock 192
- Each workload: 1 warm-up + 3 measured runs; CPU/elapsed are medians; logical reads from the last run.

| Workload | Reads before | Reads after | CPU ms before | CPU ms after | Elapsed ms before | Elapsed ms after | Rows |
|---|---:|---:|---:|---:|---:|---:|---:|
| W1a-daily-sales-original | 5523 | 1650 | 62 | 63 | 62 | 60 | 1 |
| W1b-daily-sales-rewritten | 5523 | 5 | 32 | 0 | 38 | 0 | 1 |
| W2a-monthly-revenue-original | 5523 | 1240 | 109 | 63 | 111 | 75 | 6 |
| W2b-monthly-revenue-rewritten | 5523 | 57 | 78 | 16 | 67 | 9 | 6 |
| W3-shipping-volume | 1932 | 8 | 16 | 0 | 20 | 1 | 6 |
| W4-money-order-audit | 1041 | 7 | 15 | 0 | 11 | 1 | 342 |
| W5-low-stock | 56 | 54 | 15 | 0 | 9 | 5 | 13 |
