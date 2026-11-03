/*
  02  Top 10 products by revenue per region (read-only)

  Revenue = net line value of retail items (TransactionLines.LineTotal, before tax) on PAID
  transactions in the date range. Shipping, money-order and ID-check lines are services, not
  products, so they are excluded (LineType = 'RetailItem').

  How the "top 10 per group" works:
    1. ProductRevenue  aggregates lines per (region, SKU): one row per product per region.
    2. Ranked          numbers the products inside each region with ROW_NUMBER() OVER
                       (PARTITION BY Region ORDER BY Revenue DESC). SKU breaks ties so the ranking
                       is deterministic; RANK() is shown too (equal revenue = equal rank).
    3. The outer query keeps rank 1-10 of every region. A plain TOP (10) would return 10 rows for
       the whole country, not 10 per region.
  ShareOfRegion is a window SUM over the same partition: no second pass over the data.
  Regions with no sales in the range simply do not appear (no products to rank).

  Run in SSMS against ApexPostDb, or: sqlcmd -S . -d ApexPostDb -E -C -i sql\02-top-products-by-revenue-per-region.sql
*/
SET NOCOUNT ON;

DECLARE @To   date = COALESCE((SELECT MAX(BusinessDate) FROM dbo.CounterTransactions WHERE Status = 'Paid'), CAST(SYSUTCDATETIME() AS date));
DECLARE @From date = DATEADD(day, -29, @To);   -- 30 days inclusive
DECLARE @TopN int  = 10;

WITH ProductRevenue AS
(
    SELECT  b.Region,
            l.ItemCode                AS Sku,
            SUM(l.Quantity)           AS UnitsSold,
            SUM(l.LineTotal)          AS Revenue,
            COUNT(DISTINCT t.Id)      AS Transactions
    FROM dbo.CounterTransactions AS t
    JOIN dbo.Branches            AS b ON b.BranchCode     = t.BranchCode
    JOIN dbo.TransactionLines    AS l ON l.TransactionId  = t.Id
                                     AND l.LineType       = 'RetailItem'
    WHERE t.Status = 'Paid'
      AND t.BusinessDate BETWEEN @From AND @To      -- SARGable range on the persisted column
    GROUP BY b.Region, l.ItemCode
),
Ranked AS
(
    SELECT  pr.Region,
            pr.Sku,
            p.Name                    AS ProductName,
            c.Name                    AS Category,
            pr.UnitsSold,
            pr.Revenue,
            pr.Transactions,
            ROW_NUMBER() OVER (PARTITION BY pr.Region ORDER BY pr.Revenue DESC, pr.Sku) AS RowNumber,
            RANK()       OVER (PARTITION BY pr.Region ORDER BY pr.Revenue DESC)         AS RevenueRank,
            CAST(100.0 * pr.Revenue / NULLIF(SUM(pr.Revenue) OVER (PARTITION BY pr.Region), 0) AS decimal(5, 2)) AS ShareOfRegionPct
    FROM ProductRevenue          AS pr
    JOIN dbo.Products            AS p ON p.Sku = pr.Sku
    JOIN dbo.ProductCategories   AS c ON c.Id  = p.CategoryId
)
SELECT  Region,
        RowNumber      AS Position,
        RevenueRank,
        Sku,
        ProductName,
        Category,
        UnitsSold,
        Revenue,
        ShareOfRegionPct,
        Transactions,
        @From          AS FromDate,
        @To            AS ToDate
FROM Ranked
WHERE RowNumber <= @TopN
ORDER BY Region, RowNumber;

/*
  Same result with CROSS APPLY (one TOP (10) sub-query per region); handy when there is a small
  Regions table and an index that delivers each region's rows already sorted:

  SELECT r.Region, x.Sku, x.Revenue
  FROM (SELECT DISTINCT Region FROM dbo.Branches) AS r
  CROSS APPLY (SELECT TOP (@TopN) l.ItemCode AS Sku, SUM(l.LineTotal) AS Revenue
               FROM dbo.CounterTransactions AS t
               JOIN dbo.Branches AS b ON b.BranchCode = t.BranchCode AND b.Region = r.Region
               JOIN dbo.TransactionLines AS l ON l.TransactionId = t.Id AND l.LineType = 'RetailItem'
               WHERE t.Status = 'Paid' AND t.BusinessDate BETWEEN @From AND @To
               GROUP BY l.ItemCode
               ORDER BY Revenue DESC, l.ItemCode) AS x
  ORDER BY r.Region, x.Revenue DESC;
*/
