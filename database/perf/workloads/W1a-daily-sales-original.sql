-- W1a  Branch dashboard: one branch, one business day. ORIGINAL form (functions on the column).
-- YEAR()/MONTH()/DAY() on ClosedAtUtc are not SARGable: every row of the table is read and computed.
--@ BranchCode varchar 10 BR-001
--@ Day date 0 2025-06-14
SELECT COUNT(*) AS PaidTransactions, SUM(t.Subtotal) AS Net, SUM(t.TaxAmount) AS Tax, SUM(t.TotalAmount) AS Total
FROM dbo.CounterTransactions AS t
WHERE t.BranchCode = @BranchCode
  AND t.Status = 'Paid'
  AND YEAR(t.ClosedAtUtc) = YEAR(@Day) AND MONTH(t.ClosedAtUtc) = MONTH(@Day) AND DAY(t.ClosedAtUtc) = DAY(@Day);
