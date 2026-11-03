/*
  06  Blocking demo: WINDOW 1, the blocker

  Run the three demo scripts in THREE separate SSMS query windows (each window is its own session):
    Window 1: this script. It opens a transaction, updates one stock row and does NOT commit, so it
              keeps an exclusive (X) lock on that row.
    Window 2: 07-blocking-demo-window2-blocked.sql. It tries to read and update the same row and
              waits (the query keeps "Executing...").
    Window 3: 08-blocking-demo-window3-monitor.sql. It shows who blocks whom, the wait type
              (LCK_M_S / LCK_M_U), the locks held and the SQL text of both sessions.
  Then come back here and run the ROLLBACK at the bottom: window 2 finishes at once.

  This is exactly what a long transaction in the application would do: hold locks while waiting
  (for a user, a web call, a slow loop). The application avoids it: one SaveChanges per use case,
  nothing slow inside a transaction (see DEADLOCKS.md).

  Fictional data; the update is rolled back, so nothing changes.
*/
USE ApexPostDb;
SET NOCOUNT ON;

SELECT @@SPID AS BlockerSessionId;   -- note it: window 3 shows it as blocking_session_id

BEGIN TRANSACTION;

    UPDATE dbo.BranchStock
    SET    QuantityOnHand = QuantityOnHand + 1,
           ModifiedAtUtc  = SYSUTCDATETIME()
    WHERE  BranchCode = 'BR-001' AND Sku = 'PKG-001';

    SELECT @@TRANCOUNT AS OpenTransactions, N'Row BR-001/PKG-001 is now X-locked. Run window 2, then window 3.' AS NextStep;

-- Leave the transaction open. When you have seen the blocking in window 3, select the next line
-- and run it (F5). Window 2 continues immediately.
--
-- ROLLBACK TRANSACTION;
--
-- Forgot which window has the open transaction? DBCC OPENTRAN shows the oldest active one.
