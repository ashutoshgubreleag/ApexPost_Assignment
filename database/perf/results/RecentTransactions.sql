DECLARE @p int = 20;
DECLARE @branch varchar(10) = 'BR-001';

SELECT TOP(@p) [c].[Id], [c].[BranchCode], [c].[CounterNumber], [c].[ClerkStaffId], [c].[Source], [c].[Status], (
    SELECT COALESCE(SUM([t].[Quantity]), 0)
    FROM [dbo].[TransactionLines] AS [t]
    WHERE [c].[Id] = [t].[TransactionId]), [c].[Subtotal], [c].[TaxAmount], [c].[PassThroughAmount], [c].[TotalAmount], (
    SELECT TOP(1) [p].[Method]
    FROM [dbo].[Payments] AS [p]
    WHERE [c].[Id] = [p].[TransactionId]
    ORDER BY [p].[Id] DESC), [c].[VoidReason], [c].[AuthorisedByStaffId], [c].[OpenedAtUtc], [c].[ClosedAtUtc]
FROM [dbo].[CounterTransactions] AS [c]
WHERE [c].[BranchCode] = @branch
ORDER BY [c].[OpenedAtUtc] DESC