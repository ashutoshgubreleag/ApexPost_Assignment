/*
  usp_AdjustStock: manual stock adjustment (receive or write off) plus its history row.

  Why a procedure (ADR-W3-03): the read-check-write and the history insert must be atomic and
  must hold the right lock. UPDLOCK + HOLDLOCK on the stock row serialises concurrent adjustments
  of the same SKU at the same branch without blocking other rows; XACT_ABORT guarantees that any
  error (including a CHECK violation) dooms the transaction, and the CATCH block checks
  XACT_STATE() before rolling back (version 2; version 1 checked @@TRANCOUNT only, see
  Sql/History). Done in LINQ it would take two round trips inside an explicit transaction and
  could not take the update lock on the first read.
  Called from EF Core with SqlQuery and an interpolated FormattableString (parameters only).
*/
CREATE OR ALTER PROCEDURE dbo.usp_AdjustStock
    @BranchCode varchar(10),
    @Sku        varchar(20),
    @Delta      int,
    @Reason     nvarchar(200),
    @ChangedBy  varchar(50)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @BranchCode IS NULL OR @Sku IS NULL OR @Delta IS NULL OR @Delta = 0
        THROW 50001, N'@BranchCode, @Sku and a non-zero @Delta are required.', 1;
    IF @Delta NOT BETWEEN -1000 AND 1000
        THROW 50001, N'@Delta must be between -1000 and 1000.', 1;
    IF NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
        THROW 50001, N'@Reason is required.', 1;
    IF NULLIF(LTRIM(RTRIM(@ChangedBy)), '') IS NULL
        THROW 50001, N'@ChangedBy is required.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Current int;

        SELECT @Current = QuantityOnHand
        FROM dbo.BranchStock WITH (UPDLOCK, HOLDLOCK)
        WHERE BranchCode = @BranchCode AND Sku = @Sku;

        IF @Current IS NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
               OR NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Sku = @Sku)
                THROW 50002, N'Unknown branch or SKU.', 1;
            IF @Delta < 0
                THROW 50003, N'There is no stock of this SKU at this branch to write off.', 1;

            INSERT dbo.BranchStock (BranchCode, Sku, QuantityOnHand, ReorderThreshold, UpdatedAtUtc, CreatedBy)
            VALUES (@BranchCode, @Sku, @Delta, 5, SYSUTCDATETIME(), @ChangedBy);
            SET @Current = 0;
        END
        ELSE
        BEGIN
            IF @Current + @Delta < 0
                THROW 50003, N'The adjustment would take stock below zero.', 1;

            UPDATE dbo.BranchStock
            SET QuantityOnHand = QuantityOnHand + @Delta,
                UpdatedAtUtc   = SYSUTCDATETIME(),
                ModifiedAtUtc  = SYSUTCDATETIME()
            WHERE BranchCode = @BranchCode AND Sku = @Sku;
        END;

        INSERT dbo.StockAdjustments (BranchCode, Sku, Delta, QuantityAfter, Reason, ChangedBy)
        VALUES (@BranchCode, @Sku, @Delta, @Current + @Delta, @Reason, @ChangedBy);

        DECLARE @AdjustmentId int = SCOPE_IDENTITY();
        COMMIT TRANSACTION;

        SELECT @Current + @Delta AS QuantityAfter, @AdjustmentId AS StockAdjustmentId;
    END TRY
    BEGIN CATCH
        -- XACT_STATE() says whether the transaction can still be committed; @@TRANCOUNT only says
        -- that one is open:
        --   -1  doomed: an error occurred and, with XACT_ABORT ON, SQL Server allows only ROLLBACK;
        --    1  active and committable: still rolled back, because the stock change and its history
        --       row are all-or-nothing;
        --    0  no transaction (the error came before BEGIN TRANSACTION): nothing to roll back.
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        THROW; -- re-raise the original error (number, message, line) to the caller
    END CATCH;
END;
