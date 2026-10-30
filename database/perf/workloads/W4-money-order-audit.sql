-- W4  Money order audit: issued/cancelled orders for one branch in a month, high values first.
-- Index under test: filtered IX_MoneyOrders_Branch_RequestedAt_Audited (BranchCode, RequestedAtUtc) INCLUDE (...).
--@ BranchCode varchar 10 BR-002
--@ From datetime2 0 2025-06-01T00:00:00
--@ To datetime2 0 2025-07-01T00:00:00
SELECT m.SerialNumber, m.Amount, m.Fee, m.Status, m.RequestedAtUtc, m.IdentityVerificationId
FROM dbo.MoneyOrders AS m
WHERE m.BranchCode = @BranchCode
  AND m.Status IN ('Issued', 'Cancelled')
  AND m.RequestedAtUtc >= @From AND m.RequestedAtUtc < @To
ORDER BY m.Amount DESC;
