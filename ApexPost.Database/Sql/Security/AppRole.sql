/*
  Least-privilege database role for the web application (ADR-W3-03, README "Security").
  The app login is a member of apexpost_app only. It can read and write rows and execute the
  procedures, but cannot change the schema, cannot delete history and is not db_owner.
  Migrations are applied by a separate deployment login (db_ddladmin) or by an administrator.
*/
IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NULL
    CREATE ROLE apexpost_app AUTHORIZATION dbo;

GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO apexpost_app;
GRANT EXECUTE ON OBJECT::dbo.usp_DailyBranchSales TO apexpost_app;
GRANT EXECUTE ON OBJECT::dbo.usp_LowStockReport   TO apexpost_app;
GRANT EXECUTE ON OBJECT::dbo.usp_AdjustStock      TO apexpost_app;

-- History and the audit trail are append-only for the application.
DENY UPDATE, DELETE ON OBJECT::dbo.AuditEntries     TO apexpost_app;
DENY UPDATE, DELETE ON OBJECT::dbo.StockAdjustments TO apexpost_app;
DENY UPDATE, DELETE ON OBJECT::dbo.SyncInbox        TO apexpost_app;
DENY DELETE ON OBJECT::dbo.CounterTransactions      TO apexpost_app;
DENY DELETE ON OBJECT::dbo.Payments                 TO apexpost_app;

-- Example (run once per environment, password from your secret store, never from source control):
--   CREATE LOGIN apexpost_web WITH PASSWORD = '<from secret store>';
--   CREATE USER apexpost_web FOR LOGIN apexpost_web;
--   ALTER ROLE apexpost_app ADD MEMBER apexpost_web;
