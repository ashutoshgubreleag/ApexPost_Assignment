/*
  Volume generator for the performance runs (fictional data only).
  Run by scripts/run-perf.ps1 against a SEPARATE database (ApexPostPerf), never the app database.
  Parameter: @Rows = number of counter transactions to add (default run: 300 000).
  Set-based and deterministic: the same @Rows always produces the same data, so before/after
  numbers are comparable between runs and machines.
    - CounterTransactions : @Rows over the last 730 days (92% Paid, 5% Voided, 3% Open, 5% offline)
    - TransactionLines    : 2 lines per transaction (retail + service mix)
    - Shipments           : 1 per 3 transactions
    - MoneyOrders         : 1 per 6 transactions (90% Issued, 10% Cancelled)
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF @Rows IS NULL OR @Rows < 1000 OR @Rows > 5000000
    THROW 50001, N'@Rows must be between 1000 and 5000000.', 1;

DECLARE @Origin datetime2(3) = '2024-10-25T00:00:00';
DECLARE @FirstNewId int = COALESCE((SELECT MAX(Id) FROM dbo.CounterTransactions), 0);

-- Branches that can trade: a clerk and at least one counter.
SELECT ROW_NUMBER() OVER (ORDER BY b.BranchCode) - 1 AS BranchIndex,
       b.BranchCode,
       (SELECT MIN(s.Id) FROM dbo.Staff AS s JOIN dbo.Roles AS r ON r.Id = s.RoleId
         WHERE s.HomeBranchCode = b.BranchCode AND r.Name = 'CounterClerk') AS ClerkId,
       (SELECT MIN(s.Id) FROM dbo.Staff AS s JOIN dbo.Roles AS r ON r.Id = s.RoleId
         WHERE s.HomeBranchCode = b.BranchCode AND r.Name = 'BranchManager') AS ManagerId
INTO #Branch
FROM dbo.Branches AS b
WHERE EXISTS (SELECT 1 FROM dbo.Counters AS c WHERE c.BranchCode = b.BranchCode);
DELETE FROM #Branch WHERE ClerkId IS NULL;
UPDATE #Branch SET ManagerId = ClerkId WHERE ManagerId IS NULL;

DECLARE @BranchCount int = (SELECT COUNT(*) FROM #Branch);
IF @BranchCount = 0
    THROW 50002, N'No branch with a clerk and a counter: apply the migrations (seed data) first.', 1;

SELECT c.BranchCode, c.Number, ROW_NUMBER() OVER (PARTITION BY c.BranchCode ORDER BY c.Number) - 1 AS CounterIndex,
       COUNT(*) OVER (PARTITION BY c.BranchCode) AS CounterCount
INTO #Counter
FROM dbo.Counters AS c;

SELECT z.ZoneCode, ROW_NUMBER() OVER (ORDER BY z.ZoneCode) - 1 AS ZoneIndex INTO #Zone FROM dbo.ShippingZones AS z;
DECLARE @ZoneCount int = (SELECT COUNT(*) FROM #Zone);

-- 1. Transactions ---------------------------------------------------------------------------
;WITH n AS (
    SELECT TOP (@Rows) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS n
    FROM sys.all_objects AS a CROSS JOIN sys.all_objects AS b CROSS JOIN sys.all_objects AS c
), shaped AS (
    SELECT n.n,
           b.BranchCode, b.ClerkId, b.ManagerId,
           CASE WHEN n.n % 100 < 92 THEN 'Paid' WHEN n.n % 100 < 97 THEN 'Voided' ELSE 'Open' END AS Status,
           CAST(((CAST(n.n AS bigint) * 7919) % 20000) / 100.0 + 1 AS decimal(18,2)) AS Subtotal,   -- bigint: n * 7919 overflows int above ~271k rows
           CASE WHEN n.n % 10 = 0 THEN CAST(100 AS decimal(18,2)) ELSE CAST(0 AS decimal(18,2)) END AS PassThrough,
           DATEADD(MINUTE, (CAST(n.n AS bigint) * 37) % (730 * 1440), @Origin) AS ClosedAt
    FROM n
    JOIN #Branch AS b ON b.BranchIndex = n.n % @BranchCount
)
INSERT INTO dbo.CounterTransactions
    (BranchCode, CounterNumber, ClerkStaffId, Source, Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount,
     VoidReason, AuthorisedByStaffId, OpenedAtUtc, ClosedAtUtc, CreatedBy)
SELECT s.BranchCode,
       c.Number,
       s.ClerkId,
       CASE WHEN s.n % 20 = 0 THEN 'OfflineTerminal' ELSE 'Counter' END,
       s.Status,
       s.Subtotal,
       ROUND(s.Subtotal * 0.10, 2),
       s.PassThrough,
       s.Subtotal + ROUND(s.Subtotal * 0.10, 2) + s.PassThrough,
       CASE WHEN s.Status = 'Voided' THEN N'Perf test void' END,
       CASE WHEN s.Status = 'Voided' THEN s.ManagerId END,
       DATEADD(MINUTE, -3, s.ClosedAt),
       CASE WHEN s.Status = 'Open' THEN NULL ELSE s.ClosedAt END,
       'perf-generator'
FROM shaped AS s
CROSS APPLY (SELECT TOP (1) cc.Number FROM #Counter AS cc
             WHERE cc.BranchCode = s.BranchCode AND cc.CounterIndex = s.n % cc.CounterCount) AS c
ORDER BY s.n;

-- 2. Lines (retail on every transaction, a service line on every other one) ------------------
INSERT INTO dbo.TransactionLines
    (TransactionId, LineNumber, LineType, ItemCode, Description, Quantity, UnitPrice, TaxCategory, TaxAmount, LineTotal, PassThroughAmount, CreatedBy)
SELECT t.Id, 1, 'RetailItem', 'PKG-001', N'Small padded bag', 1, t.Subtotal, 'Standard', t.TaxAmount, t.Subtotal, 0, 'perf-generator'
FROM dbo.CounterTransactions AS t
WHERE t.Id > @FirstNewId;

INSERT INTO dbo.TransactionLines
    (TransactionId, LineNumber, LineType, ItemCode, Description, Quantity, UnitPrice, TaxCategory, TaxAmount, LineTotal, PassThroughAmount, CreatedBy)
SELECT t.Id, 2, CASE WHEN t.PassThroughAmount > 0 THEN 'MoneyOrder' ELSE 'Shipping' END,
       'SRV-' + CAST(t.Id AS varchar(12)), N'Service', 1, 0, 'Exempt', 0, 0, t.PassThroughAmount, 'perf-generator'
FROM dbo.CounterTransactions AS t
WHERE t.Id > @FirstNewId AND t.Id % 2 = 0;

-- 3. Shipments ------------------------------------------------------------------------------
INSERT INTO dbo.Shipments
    (TrackingNumber, TransactionId, BranchCode, ShippingType, ServiceLevel, ZoneCode, WeightKg, ChargeableWeightKg,
     Price, Status, QuotedAtUtc, HeightCm, LengthCm, WidthCm, CreatedBy)
SELECT 'PRF' + RIGHT('000000000' + CAST(t.Id AS varchar(12)), 9),
       t.Id,
       t.BranchCode,
       CASE WHEN (t.Id / 5) % 4 = 0 THEN 'Letter' ELSE 'Parcel' END,
       CASE (t.Id / 7) % 3 WHEN 0 THEN 'Standard' WHEN 1 THEN 'Express' ELSE 'Registered' END,
       z.ZoneCode,
       CAST(0.1 + (t.Id % 200) / 10.0 AS decimal(9,3)),
       CAST(0.1 + (t.Id % 200) / 10.0 AS decimal(9,3)),
       CAST(5 + (t.Id % 90) AS decimal(18,2)),
       CASE WHEN t.Status = 'Paid' THEN 'Lodged' WHEN t.Status = 'Voided' THEN 'Cancelled' ELSE 'Quoted' END,
       t.OpenedAtUtc,
       10, 20, 15,
       'perf-generator'
FROM dbo.CounterTransactions AS t
JOIN #Zone AS z ON z.ZoneIndex = (t.Id / 11) % @ZoneCount
-- 1 in 3 transactions, spread evenly over every branch (a plain t.Id % 3 would line up with the
-- branch rotation t.Id % branches and put all shipments in two branches).
WHERE t.Id > @FirstNewId AND (t.Id / 3) % 3 = t.Id % 3;

-- 4. Money orders ---------------------------------------------------------------------------
INSERT INTO dbo.MoneyOrders
    (SerialNumber, TransactionId, BranchCode, Amount, Fee, Sender, Recipient, Status, RequestedAtUtc, IssuedAtUtc, CreatedBy)
SELECT 'PMO' + RIGHT('000000000' + CAST(t.Id AS varchar(12)), 9),
       t.Id,
       t.BranchCode,
       CAST(10 + (t.Id % 2500) AS decimal(18,2)),
       CAST(5 AS decimal(18,2)),
       N'Perf Sender',
       N'Perf Recipient',
       CASE WHEN (t.Id / 13) % 10 = 0 THEN 'Cancelled' ELSE 'Issued' END,
       t.OpenedAtUtc,
       CASE WHEN (t.Id / 13) % 10 = 0 THEN NULL ELSE t.OpenedAtUtc END,
       'perf-generator'
FROM dbo.CounterTransactions AS t
-- 1 in 6 transactions, spread evenly over every branch (see the shipments note).
WHERE t.Id > @FirstNewId AND (t.Id / 6) % 6 = t.Id % 6 AND t.Status <> 'Open';

UPDATE STATISTICS dbo.CounterTransactions WITH FULLSCAN;
UPDATE STATISTICS dbo.TransactionLines WITH FULLSCAN;
UPDATE STATISTICS dbo.Shipments WITH FULLSCAN;
UPDATE STATISTICS dbo.MoneyOrders WITH FULLSCAN;

SELECT (SELECT COUNT(*) FROM dbo.CounterTransactions) AS CounterTransactions,
       (SELECT COUNT(*) FROM dbo.TransactionLines)    AS TransactionLines,
       (SELECT COUNT(*) FROM dbo.Shipments)           AS Shipments,
       (SELECT COUNT(*) FROM dbo.MoneyOrders)         AS MoneyOrders;
