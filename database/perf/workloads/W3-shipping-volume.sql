-- W3  Shipping volume report for one zone and service level over a quarter (most recent first).
-- Index under test: IX_Shipments_Zone_Level_QuotedAt (ZoneCode, ServiceLevel, QuotedAtUtc DESC) INCLUDE (...).
--@ ZoneCode varchar 5 Z01
--@ ServiceLevel varchar 12 Express
--@ From datetime2 0 2025-04-01T00:00:00
--@ To datetime2 0 2025-07-01T00:00:00
SELECT s.BranchCode, COUNT(*) AS Shipments, SUM(s.WeightKg) AS TotalWeightKg, SUM(s.Price) AS Revenue
FROM dbo.Shipments AS s
WHERE s.ZoneCode = @ZoneCode
  AND s.ServiceLevel = @ServiceLevel
  AND s.QuotedAtUtc >= @From AND s.QuotedAtUtc < @To
  AND s.Status = 'Lodged'
GROUP BY s.BranchCode
ORDER BY s.BranchCode;
