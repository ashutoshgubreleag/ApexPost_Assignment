-- W5  Low-stock report for one branch through the stored procedure (OPTION (RECOMPILE) inside).
-- Index under test: IX_BranchStock_Branch_Covering (BranchCode) INCLUDE (Sku, QuantityOnHand, ReorderThreshold).
--@ BranchCode varchar 10 BR-001
--@ Threshold int 0 40
EXEC dbo.usp_LowStockReport @BranchCode = @BranchCode, @Threshold = @Threshold;
