/*
  Least-privilege database role for the web application (ADR-W3-03, README "Security"), version 2.

  The app login is a member of apexpost_app only. Version 1 granted SELECT, INSERT, UPDATE on the
  whole dbo schema and then denied a few things; version 2 grants per table, only what the code
  actually does (checked against the repositories and SqlAuditStore), so a new table gets no
  access until someone decides it should. The role:
    - cannot change the schema (no DDL), is not db_owner;
    - only reads reference data (roles, categories, zones, rates);
    - can never delete history or the audit trail, and never update payments, audit rows,
      sync inbox rows or stock history;
    - deletes only basket lines (removing an item from an OPEN transaction);
    - writes StockAdjustments only through usp_AdjustStock (ownership chaining: the procedure and
      the table are both owned by dbo, so EXECUTE is enough).
  Re-runnable: REVOKE, GRANT and DENY are idempotent. Migrations are applied by a separate
  deployment login (db_ddladmin) or by an administrator, never by this role.
*/
IF DATABASE_PRINCIPAL_ID(N'apexpost_app') IS NULL
    CREATE ROLE apexpost_app AUTHORIZATION dbo;

-- Version 1's schema-wide grant is replaced by the per-table grants below.
REVOKE SELECT, INSERT, UPDATE ON SCHEMA::dbo FROM apexpost_app;

-- Reference data: read only.
GRANT SELECT ON OBJECT::dbo.Roles             TO apexpost_app;
GRANT SELECT ON OBJECT::dbo.ProductCategories TO apexpost_app;
GRANT SELECT ON OBJECT::dbo.ShippingZones     TO apexpost_app;
GRANT SELECT ON OBJECT::dbo.ShippingRates     TO apexpost_app;

-- Master data maintained in the admin screens (deactivate, never delete).
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Branches TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Counters TO apexpost_app;
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Staff    TO apexpost_app;
GRANT SELECT, UPDATE         ON OBJECT::dbo.Products TO apexpost_app;  -- price revisions; products are seeded

-- Day-to-day counter work.
GRANT SELECT, INSERT, UPDATE         ON OBJECT::dbo.CounterTransactions TO apexpost_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.TransactionLines    TO apexpost_app;  -- remove a line from an open basket
GRANT SELECT, INSERT                 ON OBJECT::dbo.Payments            TO apexpost_app;
GRANT SELECT, INSERT, UPDATE         ON OBJECT::dbo.BranchStock         TO apexpost_app;
GRANT SELECT                         ON OBJECT::dbo.StockAdjustments    TO apexpost_app;  -- written by usp_AdjustStock only
GRANT SELECT, INSERT, UPDATE         ON OBJECT::dbo.Shipments           TO apexpost_app;
GRANT SELECT, INSERT, UPDATE         ON OBJECT::dbo.MoneyOrders         TO apexpost_app;
GRANT SELECT, INSERT                 ON OBJECT::dbo.IdentityVerifications TO apexpost_app;
GRANT SELECT, INSERT                 ON OBJECT::dbo.Notifications       TO apexpost_app;

-- Audit trail and offline sync: append-only.
GRANT SELECT, INSERT ON OBJECT::dbo.AuditEntries  TO apexpost_app;
GRANT SELECT, INSERT ON OBJECT::dbo.SyncInbox     TO apexpost_app;
GRANT SELECT, INSERT ON OBJECT::dbo.SyncConflicts TO apexpost_app;

-- Stored procedures.
GRANT EXECUTE ON OBJECT::dbo.usp_DailyBranchSales TO apexpost_app;
GRANT EXECUTE ON OBJECT::dbo.usp_LowStockReport   TO apexpost_app;
GRANT EXECUTE ON OBJECT::dbo.usp_AdjustStock      TO apexpost_app;

-- Belt and braces: history and the audit trail stay append-only even if a broader grant is ever
-- added by mistake (DENY wins over GRANT).
DENY UPDATE, DELETE ON OBJECT::dbo.AuditEntries     TO apexpost_app;
DENY UPDATE, DELETE ON OBJECT::dbo.StockAdjustments TO apexpost_app;
DENY UPDATE, DELETE ON OBJECT::dbo.SyncInbox        TO apexpost_app;
DENY DELETE ON OBJECT::dbo.CounterTransactions      TO apexpost_app;
DENY DELETE ON OBJECT::dbo.Payments                 TO apexpost_app;

-- Example (run once per environment, password from your secret store, never from source control):
--   CREATE LOGIN apexpost_web WITH PASSWORD = '<from secret store>';
--   CREATE USER apexpost_web FOR LOGIN apexpost_web;
--   ALTER ROLE apexpost_app ADD MEMBER apexpost_web;
