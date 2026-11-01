-- W1b  Same question, REWRITTEN: equality on the persisted computed column BusinessDate
-- (CONVERT(date, ClosedAtUtc)), which IX_CounterTransactions_Branch_BusinessDate can seek and cover.
--@ BranchCode varchar 10 BR-001
--@ Day date 0 2025-06-14
SELECT COUNT(*) AS PaidTransactions, SUM(t.Subtotal) AS Net, SUM(t.TaxAmount) AS Tax, SUM(t.TotalAmount) AS Total
FROM dbo.CounterTransactions AS t
WHERE t.BranchCode = @BranchCode
  AND t.BusinessDate = @Day
  AND t.Status = 'Paid';
