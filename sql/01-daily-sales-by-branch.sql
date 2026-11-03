/*
  01  Daily sales by branch (read-only)

  One row per branch for one business date, INCLUDING branches that sold nothing that day: the
  query starts FROM Branches and LEFT JOINs the paid transactions. The filters on the
  transactions (date and Status = 'Paid') are in the ON clause on purpose: in a WHERE clause they
  would remove the NULL rows of the LEFT JOIN and silently turn it into an INNER JOIN.

  BusinessDate is a persisted computed column (CONVERT(date, ClosedAtUtc)), so
  "BusinessDate = @BusinessDate" is SARGable and seeks IX_CounterTransactions_Branch_BusinessDate
  (PERFORMANCE.md, workload W1b).

  Run in SSMS against ApexPostDb (Ctrl+E), or: sqlcmd -S . -d ApexPostDb -E -C -i sql\01-daily-sales-by-branch.sql
*/
SET NOCOUNT ON;

-- The latest trading day by default; set a fixed date to look at another day, e.g. '2026-11-01'.
DECLARE @BusinessDate date = COALESCE(
    (SELECT MAX(BusinessDate) FROM dbo.CounterTransactions WHERE Status = 'Paid'),
    CAST(SYSUTCDATETIME() AS date));

SELECT  b.BranchCode,
        b.Name                                AS BranchName,
        b.Region,
        b.Status                              AS BranchStatus,
        @BusinessDate                         AS BusinessDate,
        COUNT(t.Id)                           AS PaidTransactions,   -- COUNT(column) ignores the NULL row of a branch with no sales
        COALESCE(SUM(t.Subtotal), 0)          AS NetSales,
        COALESCE(SUM(t.TaxAmount), 0)         AS Tax,
        COALESCE(SUM(t.PassThroughAmount), 0) AS PassThrough,        -- money-order face values collected for others
        COALESCE(SUM(t.TotalAmount), 0)       AS GrandTotal,
        CAST(COALESCE(AVG(t.TotalAmount), 0) AS decimal(18, 2)) AS AverageBasket
FROM dbo.Branches AS b
LEFT JOIN dbo.CounterTransactions AS t
       ON  t.BranchCode   = b.BranchCode
       AND t.BusinessDate = @BusinessDate
       AND t.Status       = 'Paid'
GROUP BY b.BranchCode, b.Name, b.Region, b.Status
ORDER BY GrandTotal DESC, b.BranchCode;
