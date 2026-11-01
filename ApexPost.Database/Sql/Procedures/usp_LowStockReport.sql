/*
  usp_LowStockReport: products at or below max(item reorder threshold, @Threshold).
  Both parameters are optional (NULL = all branches / the default threshold of 5).

  OPTION (RECOMPILE) vs alternatives (ADR-W3-03):
  An optional-parameter ("catch-all") predicate such as (@BranchCode IS NULL OR BranchCode = @BranchCode)
  gets ONE cached plan that must be correct for both shapes, so SQL Server scans. With RECOMPILE the
  optimizer sees the actual values on each call ("parameter embedding"), removes the dead branch
  of the OR and can SEEK IX_BranchStock_Branch_Covering for a single branch. The price is a compile
  on every call (~1 ms here). That is acceptable because this report runs a few times a day per
  manager, not thousands of times a second. Alternatives considered: two procedures (one per
  shape) or dynamic SQL with sp_executesql (two cached plans, but harder to read and review).
*/
CREATE OR ALTER PROCEDURE dbo.usp_LowStockReport
    @BranchCode varchar(10) = NULL,
    @Threshold  int         = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @BranchCode IS NOT NULL AND @BranchCode NOT LIKE 'BR-[0-9][0-9][0-9]'
        THROW 50001, N'@BranchCode must be NULL or a branch code such as BR-001.', 1;
    IF @Threshold IS NOT NULL AND (@Threshold < 0 OR @Threshold > 1000)
        THROW 50001, N'@Threshold must be between 0 and 1000.', 1;

    SET @Threshold = COALESCE(@Threshold, 5);

    SELECT s.BranchCode,
           s.Sku,
           p.Name       AS ProductName,
           p.CategoryId,
           s.QuantityOnHand,
           s.ReorderThreshold
    FROM dbo.BranchStock AS s
    JOIN dbo.Products    AS p ON p.Sku = s.Sku
    WHERE (@BranchCode IS NULL OR s.BranchCode = @BranchCode)
      AND s.QuantityOnHand <= CASE WHEN s.ReorderThreshold > @Threshold THEN s.ReorderThreshold ELSE @Threshold END
    ORDER BY s.BranchCode, s.QuantityOnHand, s.Sku
    OPTION (RECOMPILE);
END;
