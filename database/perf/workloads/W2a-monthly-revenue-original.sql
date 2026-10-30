-- W2a  Finance: paid revenue per branch for one month. ORIGINAL form: YEAR()/MONTH() on the column.
--@ Year int 0 2025
--@ Month int 0 6
SELECT t.BranchCode, COUNT(*) AS PaidTransactions, SUM(t.TotalAmount) AS Revenue
FROM dbo.CounterTransactions AS t
WHERE t.Status = 'Paid'
  AND YEAR(t.ClosedAtUtc) = @Year AND MONTH(t.ClosedAtUtc) = @Month
GROUP BY t.BranchCode
ORDER BY t.BranchCode;
