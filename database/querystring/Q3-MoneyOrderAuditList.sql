DECLARE @p int = 500;
DECLARE @startOffset datetime2 = '2026-10-01T00:00:00.0000000Z';
DECLARE @endOffset datetime2 = '2026-11-01T00:00:00.0000000Z';
DECLARE @branch varchar(10) = 'BR-001';

SELECT TOP(@p) [m].[Id], [m].[SerialNumber], [m].[TransactionId], [m].[BranchCode], [m].[Amount], [m].[Fee], [m].[Sender], [m].[Recipient], [m].[IdentityVerificationId], [m].[Status], [m].[RequestedAtUtc], [m].[IssuedAtUtc]
FROM [dbo].[MoneyOrders] AS [m]
WHERE [m].[Status] <> 'Pending' AND [m].[RequestedAtUtc] >= @startOffset AND [m].[RequestedAtUtc] < @endOffset AND [m].[BranchCode] = @branch
ORDER BY [m].[RequestedAtUtc] DESC