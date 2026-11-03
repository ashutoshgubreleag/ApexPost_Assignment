/*
  07  Blocking demo: WINDOW 2, the blocked session

  Run AFTER window 1 (06-blocking-demo-window1-blocker.sql) has its transaction open.

  Step A: a plain read of the locked row. Under the default READ COMMITTED isolation level a reader
          needs a shared (S) lock, which is incompatible with window 1's exclusive (X) lock, so it
          WAITS (wait type LCK_M_S) until window 1 commits or rolls back. It does not fail; there is
          no timeout by default (LOCK_TIMEOUT = -1).

  Step B (optional, run it alone after cancelling A): the same read with a lock timeout. After 5
          seconds it fails with error 1222 ("Lock request time out period exceeded") instead of
          waiting forever, which is how an application can choose to fail fast.

  If the database has READ_COMMITTED_SNAPSHOT ON (ApexPostDb does not), step A reads the last
  committed version from tempdb instead of waiting; step C still blocks, because two writers can
  never change the same row at the same time under any isolation level.

  Step D (optional): reading with NOLOCK does not wait, but returns window 1's UNCOMMITTED value
          (a dirty read) that disappears when window 1 rolls back. That is why the application never
          uses NOLOCK for money or stock.
*/
USE ApexPostDb;
SET NOCOUNT ON;

SELECT @@SPID AS BlockedSessionId;

-- Step A: waits until window 1 ends its transaction.
SELECT BranchCode, Sku, QuantityOnHand, SYSUTCDATETIME() AS ReadAtUtc
FROM dbo.BranchStock
WHERE BranchCode = 'BR-001' AND Sku = 'PKG-001';

/* Step B: fail fast instead of waiting (select these lines and run them on their own)
SET LOCK_TIMEOUT 5000;   -- milliseconds
BEGIN TRY
    SELECT QuantityOnHand FROM dbo.BranchStock WHERE BranchCode = 'BR-001' AND Sku = 'PKG-001';
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;   -- 1222
END CATCH;
SET LOCK_TIMEOUT -1;
*/

/* Step C: a second writer always waits (wait type LCK_M_U), whatever the isolation level
UPDATE dbo.BranchStock
SET    QuantityOnHand = QuantityOnHand - 1
WHERE  BranchCode = 'BR-001' AND Sku = 'PKG-001';
-- (if you ran it, undo it after window 1 has rolled back: run the same UPDATE with + 1)
*/

/* Step D: dirty read (do not use for real data)
SELECT QuantityOnHand AS UncommittedValue
FROM dbo.BranchStock WITH (NOLOCK)
WHERE BranchCode = 'BR-001' AND Sku = 'PKG-001';
*/
