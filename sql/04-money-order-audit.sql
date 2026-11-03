/*
  04  Money-order audit (read-only)

  Part 1: per branch, issued money orders in the range, their face value and fees, how many were
          above the identity threshold (so needed an ID check) and the identity checks done. Starts
          FROM Branches with LEFT JOINs to pre-aggregated derived tables, so branches with no
          money orders show zeros and the two aggregates cannot multiply each other's rows (joining
          MoneyOrders and IdentityVerifications directly and then summing would double count).
  Part 2: exceptions an auditor must look at: issued orders above the threshold without a PASSED
          identity check. The business rules prevent these, so the expected result is no rows; the query
          proves it on real data.

  Identity data is masked: only DocumentTypeCode and DocumentLast4 exist in the database.

  Run in SSMS against ApexPostDb, or: sqlcmd -S . -d ApexPostDb -E -C -i sql\04-money-order-audit.sql
*/
SET NOCOUNT ON;

DECLARE @To        date = DATEADD(day, 1, CAST(SYSUTCDATETIME() AS date));   -- exclusive: up to and including today
DECLARE @From      date = DATEADD(day, -60, @To);
DECLARE @Threshold decimal(18, 2) = 1000.00;   -- BusinessRules:MoneyOrderIdentityThreshold; an ID check is required ABOVE it

-- Part 1: summary per branch
SELECT  b.BranchCode,
        b.Name                              AS BranchName,
        COALESCE(mo.MoneyOrders, 0)         AS MoneyOrders,
        COALESCE(mo.FaceValue, 0)           AS FaceValue,
        COALESCE(mo.Fees, 0)                AS Fees,
        COALESCE(mo.NeedingId, 0)           AS OrdersNeedingIdCheck,
        COALESCE(iv.Passed, 0)              AS IdChecksPassed,
        COALESCE(iv.Failed, 0)              AS IdChecksFailed
FROM dbo.Branches AS b
LEFT JOIN (SELECT BranchCode,
                  COUNT(*)                                          AS MoneyOrders,
                  SUM(Amount)                                       AS FaceValue,
                  SUM(Fee)                                          AS Fees,
                  SUM(CASE WHEN Amount > @Threshold THEN 1 ELSE 0 END)  AS NeedingId
           FROM dbo.MoneyOrders
           WHERE Status = 'Issued'
             AND RequestedAtUtc >= @From AND RequestedAtUtc < @To
           GROUP BY BranchCode) AS mo
       ON mo.BranchCode = b.BranchCode
LEFT JOIN (SELECT BranchCode,
                  SUM(CASE WHEN ResultCode = 'PASS' THEN 1 ELSE 0 END) AS Passed,
                  SUM(CASE WHEN ResultCode = 'FAIL' THEN 1 ELSE 0 END) AS Failed
           FROM dbo.IdentityVerifications
           WHERE VerifiedAtUtc >= @From AND VerifiedAtUtc < @To
           GROUP BY BranchCode) AS iv
       ON iv.BranchCode = b.BranchCode
ORDER BY b.BranchCode;

-- Part 2: exceptions (expected: none)
SELECT  m.SerialNumber,
        m.BranchCode,
        m.Amount,
        m.RequestedAtUtc,
        m.IdentityVerificationId,
        v.ResultCode                         AS IdCheckResult,
        v.DocumentTypeCode,
        N'****' + v.DocumentLast4            AS DocumentNumberMasked,
        CASE WHEN m.IdentityVerificationId IS NULL THEN N'No identity check linked'
             ELSE N'Identity check did not pass' END AS Problem
FROM dbo.MoneyOrders AS m
LEFT JOIN dbo.IdentityVerifications AS v ON v.Id = m.IdentityVerificationId
WHERE m.Status = 'Issued'
  AND m.Amount > @Threshold
  AND m.RequestedAtUtc >= @From AND m.RequestedAtUtc < @To
  AND (v.Id IS NULL OR v.ResultCode <> 'PASS')
ORDER BY m.RequestedAtUtc DESC;
