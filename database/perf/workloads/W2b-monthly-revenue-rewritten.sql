-- W2b  Same result, REWRITTEN as a half-open range [@From, @To) on the raw column, so the filtered
-- index IX_CounterTransactions_ClosedAt_Paid (WHERE Status = 'Paid') can seek and cover it.
--@ From datetime2 0 2025-06-01T00:00:00
--@ To datetime2 0 2025-07-01T00:00:00
SELECT t.BranchCode, COUNT(*) AS PaidTransactions, SUM(t.TotalAmount) AS Revenue
FROM dbo.CounterTransactions AS t
WHERE t.Status = 'Paid'
  AND t.ClosedAtUtc >= @From AND t.ClosedAtUtc < @To
GROUP BY t.BranchCode
ORDER BY t.BranchCode;
