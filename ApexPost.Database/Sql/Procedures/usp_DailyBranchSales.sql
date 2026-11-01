/*
  usp_DailyBranchSales: retail vs services totals for one branch and one business day.

  Why a procedure (ADR-W3-03): this is a fixed, DBA-owned report contract used by the dashboard,
  the finance export and SSMS users alike. The split needs a conditional aggregate over
  TransactionLines joined to CounterTransactions; keeping it in one place means every consumer gets
  the same definition of "retail" and "services", and the plan is tuned once against
  IX_CounterTransactions_Branch_BusinessDate (seek on BranchCode + BusinessDate, a SARGable
  equality on the persisted computed date column).
*/
CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
    @BranchCode varchar(10),
    @Date       date
AS
BEGIN
    SET NOCOUNT ON;

    IF @BranchCode IS NULL OR @BranchCode NOT LIKE 'BR-[0-9][0-9][0-9]'
        THROW 50001, N'@BranchCode must be a branch code such as BR-001.', 1;
    IF @Date IS NULL
        THROW 50001, N'@Date is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
        THROW 50002, N'Branch not found.', 1;

    SELECT @BranchCode                                         AS BranchCode,
           @Date                                               AS BusinessDate,
           COUNT(DISTINCT t.Id)                                AS PaidTransactions,
           COALESCE(SUM(CASE WHEN l.LineType = 'RetailItem'  THEN l.LineTotal END), 0) AS RetailTotal,
           COALESCE(SUM(CASE WHEN l.LineType <> 'RetailItem' THEN l.LineTotal END), 0) AS ServicesTotal,
           COALESCE(SUM(l.TaxAmount), 0)                       AS TaxTotal,
           COALESCE(SUM(l.LineTotal + l.TaxAmount + l.PassThroughAmount), 0) AS GrandTotal
    FROM dbo.CounterTransactions AS t
    JOIN dbo.TransactionLines    AS l ON l.TransactionId = t.Id
    WHERE t.BranchCode   = @BranchCode
      AND t.BusinessDate = @Date
      AND t.Status       = 'Paid';
END;
