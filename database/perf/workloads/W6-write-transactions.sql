-- W6  WRITE COST on CounterTransactions (two of the reporting indexes live on this table).
-- Inserts @Rows paid transactions in one statement (copies of existing rows, so every constraint
-- holds), then rolls back so the read workloads always see the same data. Compare before/after:
-- the extra logical reads and time are the price of maintaining IX_..._Branch_BusinessDate and
-- the filtered IX_..._ClosedAt_Paid on insert.
--@ Rows int 0 20000
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO dbo.CounterTransactions
    (BranchCode, CounterNumber, ClerkStaffId, Source, Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount,
     VoidReason, AuthorisedByStaffId, OpenedAtUtc, ClosedAtUtc, CreatedBy)
SELECT TOP (@Rows) BranchCode, CounterNumber, ClerkStaffId, Source, Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount,
       VoidReason, AuthorisedByStaffId, OpenedAtUtc, ClosedAtUtc, 'perf-write'
FROM dbo.CounterTransactions
WHERE Status = 'Paid'
ORDER BY Id;
ROLLBACK TRANSACTION;
