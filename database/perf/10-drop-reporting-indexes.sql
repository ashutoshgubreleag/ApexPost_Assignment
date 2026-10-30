/* BEFORE state: remove the five reporting indexes added by migration AddReportingIndexes. */
SET NOCOUNT ON;
DROP INDEX IF EXISTS IX_BranchStock_Branch_Covering           ON dbo.BranchStock;
DROP INDEX IF EXISTS IX_CounterTransactions_Branch_BusinessDate ON dbo.CounterTransactions;
DROP INDEX IF EXISTS IX_CounterTransactions_ClosedAt_Paid     ON dbo.CounterTransactions;
DROP INDEX IF EXISTS IX_Shipments_Zone_Level_QuotedAt         ON dbo.Shipments;
DROP INDEX IF EXISTS IX_MoneyOrders_Branch_RequestedAt_Audited ON dbo.MoneyOrders;
