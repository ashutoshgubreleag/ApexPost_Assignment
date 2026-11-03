/*
  05  A counter sale as one T-SQL transaction

  #RecordSale records a retail sale the way the application does in one unit of work. It writes
  the transaction header, its lines, the stock decrements, the totals and the payment, and either
  all of it happens or none of it. It shows:

    SET XACT_ABORT ON   Any run-time error (including a CHECK or FK violation, which on their own
                        abort only the statement) dooms the whole transaction, so a sale can never
                        be half written.
    BEGIN TRAN / COMMIT The unit of work. It is kept short: everything is validated BEFORE it starts,
                        and it waits for no user input.
    TRY / CATCH         One error path for everything.
    XACT_STATE()        In CATCH: -1 = doomed (only ROLLBACK allowed), 1 = still committable (we
                        roll back anyway: all or nothing), 0 = no transaction was open (the error
                        happened in validation, before BEGIN TRAN). @@TRANCOUNT cannot tell -1
                        from 1.
    THROW               Business errors are raised with THROW 51000-51004 (custom numbers >= 50000).
                        THROW without arguments in CATCH re-raises the ORIGINAL error (number,
                        message, line) to the caller after the rollback.
    UPDLOCK, HOLDLOCK   The stock rows are read with update locks before they are checked, so two
                        clerks selling the last unit cannot both pass the check (no lost update).
                        All stock rows are locked by ONE statement in index-key order
                        (UQ_BranchStock_Branch_Sku), so two sales always lock shared rows in the same
                        order. That is the deadlock-avoidance rule from DEADLOCKS.md.

  The script is SAFE TO RUN AS IS. @Commit = 0 is a dry run: the sale is written, shown and then
  rolled back. Each run only uses up an IDENTITY value. Set @Commit = 1 in scenario 1 to keep the
  sale. The scenarios that follow show the failure paths and prove that nothing was left behind.

  Run in SSMS against ApexPostDb (shows the PRINT output in the Messages tab), or:
    sqlcmd -S . -d ApexPostDb -E -C -i sql\05-sale-transaction.sql
  Fictional data: branch BR-001, counter 1, clerk 7 (clerk.br001a@apexpost.local).
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'tempdb..#RecordSale') IS NOT NULL
    DROP PROCEDURE #RecordSale;
GO

CREATE PROCEDURE #RecordSale
    @BranchCode     varchar(10),
    @CounterNumber  int,
    @ClerkStaffId   int,
    @PaymentMethod  varchar(12),
    @ItemsJson      nvarchar(max),      -- e.g. N'[{"sku":"PKG-001","qty":2}]'
    @Commit         bit = 0,
    @TransactionId  int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Basket TABLE (Sku varchar(20) NOT NULL PRIMARY KEY, Quantity int NOT NULL);

    BEGIN TRY
        -- 1. Validate everything that can be checked without locks, BEFORE the transaction starts.
        INSERT @Basket (Sku, Quantity)
        SELECT UPPER(LTRIM(RTRIM(j.sku))), SUM(j.qty)
        FROM OPENJSON(@ItemsJson) WITH (sku varchar(20) '$.sku', qty int '$.qty') AS j
        GROUP BY UPPER(LTRIM(RTRIM(j.sku)));

        IF NOT EXISTS (SELECT 1 FROM @Basket)
            THROW 51000, N'The basket is empty.', 1;
        IF EXISTS (SELECT 1 FROM @Basket WHERE Quantity IS NULL OR Quantity <= 0)
            THROW 51000, N'Every quantity must be a positive whole number.', 1;
        IF NOT EXISTS (SELECT 1
                       FROM dbo.Staff AS s
                       JOIN dbo.Branches AS b ON b.BranchCode = s.HomeBranchCode
                       WHERE s.Id = @ClerkStaffId AND s.IsActive = 1
                         AND s.HomeBranchCode = @BranchCode AND b.Status = 'Active')
            THROW 51001, N'The clerk is not active at this branch, or the branch is closed.', 1;
        IF NOT EXISTS (SELECT 1 FROM dbo.Counters WHERE BranchCode = @BranchCode AND Number = @CounterNumber AND IsActive = 1)
            THROW 51002, N'This counter does not exist or is not active.', 1;
        IF EXISTS (SELECT 1 FROM @Basket AS b
                   LEFT JOIN dbo.Products AS p ON p.Sku = b.Sku AND p.IsActive = 1
                   WHERE p.Id IS NULL)
            THROW 51004, N'The basket contains an unknown or inactive SKU.', 1;

        -- 2. The unit of work.
        BEGIN TRANSACTION;

        -- 2a. Lock the stock rows (one statement, key order) and check them under the lock.
        DECLARE @Shortage nvarchar(1800);
        SELECT @Shortage = STRING_AGG(CONCAT(b.Sku, N': need ', b.Quantity, N', have ', COALESCE(s.QuantityOnHand, 0)), N'; ')
        FROM @Basket AS b
        LEFT JOIN dbo.BranchStock AS s WITH (UPDLOCK, HOLDLOCK)
               ON s.BranchCode = @BranchCode AND s.Sku = b.Sku
        WHERE COALESCE(s.QuantityOnHand, 0) < b.Quantity;

        IF @Shortage IS NOT NULL
        BEGIN
            DECLARE @ShortageMessage nvarchar(2048) = CONCAT(N'Not enough stock. ', @Shortage);
            THROW 51003, @ShortageMessage, 1;
        END;

        -- 2b. Header (Open), then lines at the current price with tax per line (same rates and
        --     rounding as TaxRules: Standard 10%, Reduced 5%, Exempt 0%, half away from zero).
        DECLARE @Now datetime2(3) = SYSUTCDATETIME();

        INSERT dbo.CounterTransactions
            (BranchCode, CounterNumber, ClerkStaffId, Source, Status, Subtotal, TaxAmount, PassThroughAmount, TotalAmount, OpenedAtUtc, CreatedBy)
        VALUES
            (@BranchCode, @CounterNumber, @ClerkStaffId, 'Counter', 'Open', 0, 0, 0, 0, @Now, 'sql-script');
        SET @TransactionId = CAST(SCOPE_IDENTITY() AS int);

        INSERT dbo.TransactionLines
            (TransactionId, LineNumber, LineType, ItemCode, Description, Quantity, UnitPrice, TaxCategory, TaxAmount, LineTotal, PassThroughAmount, CreatedBy)
        SELECT  @TransactionId,
                ROW_NUMBER() OVER (ORDER BY b.Sku),
                'RetailItem',
                p.Sku,
                p.Name,
                b.Quantity,
                p.UnitPrice,
                p.TaxCategory,
                ROUND(p.UnitPrice * b.Quantity * CASE p.TaxCategory WHEN 'Standard' THEN 0.10 WHEN 'Reduced' THEN 0.05 ELSE 0.00 END, 2),
                p.UnitPrice * b.Quantity,
                0,
                'sql-script'
        FROM @Basket AS b
        JOIN dbo.Products AS p ON p.Sku = b.Sku;

        -- 2c. Take the units out of stock. CK_BranchStock_QuantityOnHand_NonNegative is the last
        --     line of defence; with XACT_ABORT ON its violation would doom the transaction.
        UPDATE s
        SET    s.QuantityOnHand = s.QuantityOnHand - b.Quantity,
               s.UpdatedAtUtc   = @Now,
               s.ModifiedAtUtc  = @Now
        FROM dbo.BranchStock AS s
        JOIN @Basket AS b ON b.Sku = s.Sku
        WHERE s.BranchCode = @BranchCode;

        -- 2d. Totals (CK_CounterTransactions_Total checks Total = Subtotal + Tax + PassThrough),
        --     close the sale, record the payment (CK_Payments_Method checks the method).
        DECLARE @Subtotal decimal(18, 2), @Tax decimal(18, 2);
        SELECT @Subtotal = SUM(LineTotal), @Tax = SUM(TaxAmount)
        FROM dbo.TransactionLines
        WHERE TransactionId = @TransactionId;

        UPDATE dbo.CounterTransactions
        SET    Subtotal          = @Subtotal,
               TaxAmount         = @Tax,
               PassThroughAmount = 0,
               TotalAmount       = @Subtotal + @Tax,
               Status            = 'Paid',
               ClosedAtUtc       = @Now,
               ModifiedAtUtc     = @Now
        WHERE Id = @TransactionId;

        INSERT dbo.Payments (TransactionId, Method, Amount, PaidAtUtc, CreatedBy)
        VALUES (@TransactionId, @PaymentMethod, @Subtotal + @Tax, @Now, 'sql-script');

        -- 2e. What was written (visible inside the transaction).
        SELECT t.Id AS TransactionId, t.BranchCode, t.CounterNumber, t.Status, t.Subtotal, t.TaxAmount, t.TotalAmount, t.BusinessDate,
               p.Method AS PaidBy, p.Amount AS Paid
        FROM dbo.CounterTransactions AS t
        JOIN dbo.Payments AS p ON p.TransactionId = t.Id
        WHERE t.Id = @TransactionId;

        SELECT l.LineNumber, l.ItemCode, l.Description, l.Quantity, l.UnitPrice, l.TaxCategory, l.TaxAmount, l.LineTotal
        FROM dbo.TransactionLines AS l
        WHERE l.TransactionId = @TransactionId
        ORDER BY l.LineNumber;

        IF @Commit = 1
        BEGIN
            COMMIT TRANSACTION;
            PRINT CONCAT(N'Sale ', @TransactionId, N' committed.');
        END
        ELSE
        BEGIN
            ROLLBACK TRANSACTION;
            PRINT CONCAT(N'Dry run: sale ', @TransactionId, N' was written and then rolled back (set @Commit = 1 to keep it).');
        END;
    END TRY
    BEGIN CATCH
        DECLARE @State int = XACT_STATE();
        PRINT CONCAT(N'Sale failed: error ', ERROR_NUMBER(), N' "', ERROR_MESSAGE(), N'"; XACT_STATE() = ', @State,
                     CASE @State WHEN -1 THEN N' (doomed: only ROLLBACK is possible)'
                                 WHEN  1 THEN N' (committable, rolled back anyway: all or nothing)'
                                 ELSE         N' (no transaction was open)' END);

        IF @State = -1
            ROLLBACK TRANSACTION;          -- the only thing a doomed transaction allows
        ELSE IF @State = 1
            ROLLBACK TRANSACTION;          -- never commit half a sale

        THROW;                             -- re-raise the original error to the caller
    END CATCH;
END;
GO

/* ---------- Scenario 1: a good sale (dry run) ------------------------------------------------ */
PRINT N'--- Scenario 1: PKG-001 x2 and PKG-002 x1, paid by card (dry run) ---';
SELECT Sku, QuantityOnHand AS StockBefore FROM dbo.BranchStock WHERE BranchCode = 'BR-001' AND Sku IN ('PKG-001', 'PKG-002') ORDER BY Sku;

DECLARE @Id int;
EXEC #RecordSale
     @BranchCode    = 'BR-001',
     @CounterNumber = 1,
     @ClerkStaffId  = 7,
     @PaymentMethod = 'Card',
     @ItemsJson     = N'[{"sku":"PKG-001","qty":2},{"sku":"PKG-002","qty":1}]',
     @Commit        = 0,              -- 1 = keep the sale
     @TransactionId = @Id OUTPUT;

SELECT Sku, QuantityOnHand AS StockAfter FROM dbo.BranchStock WHERE BranchCode = 'BR-001' AND Sku IN ('PKG-001', 'PKG-002') ORDER BY Sku;
GO

/* ---------- Scenario 2: not enough stock (THROW 51003 inside the transaction) ----------------- */
PRINT N'--- Scenario 2: more units than the branch has ---';
BEGIN TRY
    EXEC #RecordSale @BranchCode = 'BR-001', @CounterNumber = 1, @ClerkStaffId = 7, @PaymentMethod = 'Cash',
                     @ItemsJson = N'[{"sku":"PKG-001","qty":2},{"sku":"PKG-002","qty":99999}]';
END TRY
BEGIN CATCH
    SELECT N'2: not enough stock' AS Scenario, ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage,
           XACT_STATE() AS XactStateInCaller, @@TRANCOUNT AS OpenTransactions;
END CATCH;
SELECT Sku, QuantityOnHand AS StockUnchanged FROM dbo.BranchStock WHERE BranchCode = 'BR-001' AND Sku IN ('PKG-001', 'PKG-002') ORDER BY Sku;
GO

/* ---------- Scenario 3: a CHECK constraint fails half way (doomed transaction) ---------------- */
-- 'Cheque' breaks CK_Payments_Method on the LAST insert, after the header, lines and stock were
-- already written. XACT_ABORT dooms the transaction (XACT_STATE() = -1) and everything is undone.
PRINT N'--- Scenario 3: invalid payment method, rejected by a CHECK constraint ---';
BEGIN TRY
    EXEC #RecordSale @BranchCode = 'BR-001', @CounterNumber = 1, @ClerkStaffId = 7, @PaymentMethod = 'Cheque',
                     @ItemsJson = N'[{"sku":"PKG-001","qty":1}]';
END TRY
BEGIN CATCH
    SELECT N'3: CHECK constraint' AS Scenario, ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage,
           XACT_STATE() AS XactStateInCaller, @@TRANCOUNT AS OpenTransactions;
END CATCH;
SELECT Sku, QuantityOnHand AS StockUnchanged FROM dbo.BranchStock WHERE BranchCode = 'BR-001' AND Sku = 'PKG-001';
GO

/* ---------- Scenario 4: invalid input, caught before any transaction ------------------------- */
PRINT N'--- Scenario 4: empty basket (XACT_STATE() = 0, nothing to roll back) ---';
BEGIN TRY
    EXEC #RecordSale @BranchCode = 'BR-001', @CounterNumber = 1, @ClerkStaffId = 7, @PaymentMethod = 'Cash', @ItemsJson = N'[]';
END TRY
BEGIN CATCH
    SELECT N'4: empty basket' AS Scenario, ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage,
           XACT_STATE() AS XactStateInCaller, @@TRANCOUNT AS OpenTransactions;
END CATCH;
GO

/* ---------- Proof: nothing was left behind --------------------------------------------------- */
SELECT COUNT(*) AS SalesWrittenByThisScript FROM dbo.CounterTransactions WHERE CreatedBy = 'sql-script';  -- 0 unless you ran with @Commit = 1
GO

DROP PROCEDURE #RecordSale;
GO
