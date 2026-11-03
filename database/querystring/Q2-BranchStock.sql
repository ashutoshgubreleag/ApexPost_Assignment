DECLARE @branch varchar(10) = 'BR-001';

SELECT [b].[BranchCode], [b].[Sku], [p].[Name], [b].[QuantityOnHand], [b].[ReorderThreshold]
FROM [dbo].[BranchStock] AS [b]
INNER JOIN [dbo].[Products] AS [p] ON [b].[Sku] = [p].[Sku]
WHERE [b].[BranchCode] = @branch
ORDER BY [p].[Name]