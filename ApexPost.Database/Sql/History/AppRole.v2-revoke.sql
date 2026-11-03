/*
  Down step of migration 20261103090000_HardenAppRoleAndAdjustStock: removes the per-table grants
  of AppRole version 2. The migration then re-runs AppRole.v1.sql (schema-wide grant + DENYs).
*/
IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NOT NULL
BEGIN
    REVOKE SELECT ON OBJECT::dbo.Roles             FROM apexpost_app;
    REVOKE SELECT ON OBJECT::dbo.ProductCategories FROM apexpost_app;
    REVOKE SELECT ON OBJECT::dbo.ShippingZones     FROM apexpost_app;
    REVOKE SELECT ON OBJECT::dbo.ShippingRates     FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.Branches FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.Counters FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.Staff    FROM apexpost_app;
    REVOKE SELECT, UPDATE         ON OBJECT::dbo.Products FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.CounterTransactions FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.TransactionLines FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.Payments FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.BranchStock FROM apexpost_app;
    REVOKE SELECT ON OBJECT::dbo.StockAdjustments FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.Shipments FROM apexpost_app;
    REVOKE SELECT, INSERT, UPDATE ON OBJECT::dbo.MoneyOrders FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.IdentityVerifications FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.Notifications FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.AuditEntries  FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.SyncInbox     FROM apexpost_app;
    REVOKE SELECT, INSERT ON OBJECT::dbo.SyncConflicts FROM apexpost_app;
END;
