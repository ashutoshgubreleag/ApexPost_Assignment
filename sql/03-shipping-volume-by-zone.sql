/*
  03  Shipping volume by zone and service level (read-only)

  Lodged shipments for one calendar month, per zone and service level, with a total line per zone
  (GROUPING SETS). Starts FROM ShippingZones and LEFT JOINs the shipments, so a zone that shipped
  nothing still appears with zeros; as in script 01, the shipment filters are in the ON clause.

  The date filter is a half-open range on the raw column ([From, To)), not MONTH(QuotedAtUtc) = ...:
  the column is left untouched, so IX_Shipments_Zone_Level_QuotedAt can seek (PERFORMANCE.md, W3).

  Run in SSMS against ApexPostDb, or: sqlcmd -S . -d ApexPostDb -E -C -i sql\03-shipping-volume-by-zone.sql
*/
SET NOCOUNT ON;

-- Month of the latest shipment by default; or set e.g. DECLARE @From date = '2026-10-01'.
DECLARE @From date = DATEFROMPARTS(
    YEAR(COALESCE((SELECT MAX(QuotedAtUtc) FROM dbo.Shipments WHERE Status = 'Lodged'), SYSUTCDATETIME())),
    MONTH(COALESCE((SELECT MAX(QuotedAtUtc) FROM dbo.Shipments WHERE Status = 'Lodged'), SYSUTCDATETIME())),
    1);
DECLARE @To date = DATEADD(month, 1, @From);   -- exclusive upper bound

SELECT  z.ZoneCode,
        z.Name                                        AS ZoneName,
        CASE WHEN GROUPING(s.ServiceLevel) = 1 THEN N'(zone total)'
             ELSE COALESCE(s.ServiceLevel, N'(no shipments)') END AS ServiceLevel,
        COUNT(s.Id)                                   AS Shipments,
        COALESCE(SUM(s.ChargeableWeightKg), 0)        AS ChargeableWeightKg,
        COALESCE(SUM(s.Price), 0)                     AS Revenue,
        CAST(COALESCE(AVG(s.Price), 0) AS decimal(18, 2)) AS AveragePrice,
        @From                                         AS MonthStart
FROM dbo.ShippingZones AS z
LEFT JOIN dbo.Shipments AS s
       ON  s.ZoneCode     = z.ZoneCode
       AND s.Status       = 'Lodged'
       AND s.QuotedAtUtc >= @From
       AND s.QuotedAtUtc <  @To
GROUP BY GROUPING SETS ((z.ZoneCode, z.Name, s.ServiceLevel), (z.ZoneCode, z.Name))
ORDER BY z.ZoneCode, GROUPING(s.ServiceLevel), s.ServiceLevel;
