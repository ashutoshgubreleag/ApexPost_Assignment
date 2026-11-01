/* AFTER state: the same definitions as migration 20261030124132_AddReportingIndexes (see PERFORMANCE.md). */
SET NOCOUNT ON;
IF INDEXPROPERTY(OBJECT_ID('dbo.BranchStock'), 'IX_BranchStock_Branch_Covering', 'IndexID') IS NULL
    CREATE INDEX IX_BranchStock_Branch_Covering ON dbo.BranchStock (BranchCode) INCLUDE (Sku, QuantityOnHand, ReorderThreshold);
IF INDEXPROPERTY(OBJECT_ID('dbo.CounterTransactions'), 'IX_CounterTransactions_Branch_BusinessDate', 'IndexID') IS NULL
    CREATE INDEX IX_CounterTransactions_Branch_BusinessDate ON dbo.CounterTransactions (BranchCode, BusinessDate)
        INCLUDE (Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount);
IF INDEXPROPERTY(OBJECT_ID('dbo.CounterTransactions'), 'IX_CounterTransactions_ClosedAt_Paid', 'IndexID') IS NULL
    CREATE INDEX IX_CounterTransactions_ClosedAt_Paid ON dbo.CounterTransactions (ClosedAtUtc)
        INCLUDE (BranchCode, TotalAmount) WHERE Status = 'Paid';
IF INDEXPROPERTY(OBJECT_ID('dbo.Shipments'), 'IX_Shipments_Zone_Level_QuotedAt', 'IndexID') IS NULL
    CREATE INDEX IX_Shipments_Zone_Level_QuotedAt ON dbo.Shipments (ZoneCode, ServiceLevel, QuotedAtUtc DESC)
        INCLUDE (Status, WeightKg, Price, BranchCode);
IF INDEXPROPERTY(OBJECT_ID('dbo.MoneyOrders'), 'IX_MoneyOrders_Branch_RequestedAt_Audited', 'IndexID') IS NULL
    CREATE INDEX IX_MoneyOrders_Branch_RequestedAt_Audited ON dbo.MoneyOrders (BranchCode, RequestedAtUtc)
        INCLUDE (Amount, Fee, Status, SerialNumber, IdentityVerificationId) WHERE Status IN ('Issued', 'Cancelled');
