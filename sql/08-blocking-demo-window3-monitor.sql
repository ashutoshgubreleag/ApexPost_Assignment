/*
  08  Blocking demo: WINDOW 3, see who blocks whom

  Run while window 2 is waiting. Needs VIEW SERVER STATE (any sysadmin/dbo on a dev box has it).
  1. Blocked requests: session, the session blocking it, wait type and time, and both SQL texts.
  2. The locks involved: window 1 holds X on the KEY (and IX on the page/object); window 2 is
     WAITING for S on the same KEY.
  3. The head of the blocking chain (a session that blocks others but is not blocked itself).
*/
SET NOCOUNT ON;

-- 1. Blocked requests
SELECT  r.session_id                        AS BlockedSession,
        r.blocking_session_id               AS BlockingSession,
        r.wait_type                         AS WaitType,          -- LCK_M_S for the read, LCK_M_U / LCK_M_X for an update
        r.wait_time / 1000.0                AS WaitSeconds,
        r.wait_resource                     AS WaitResource,      -- KEY: <db>:<hobt>(hash) = the stock row
        DB_NAME(r.database_id)              AS DatabaseName,
        blocked.text                        AS BlockedSql,
        blocker.event_info                  AS BlockingSql        -- last batch sent by the blocker
FROM sys.dm_exec_requests AS r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS blocked
OUTER APPLY sys.dm_exec_input_buffer(r.blocking_session_id, NULL) AS blocker
WHERE r.blocking_session_id <> 0;

-- 2. Locks held and requested in ApexPostDb (GRANT = held, WAIT = queued)
SELECT  l.request_session_id                AS SessionId,
        l.resource_type                     AS ResourceType,      -- OBJECT / PAGE / KEY
        l.request_mode                      AS LockMode,          -- IX, X, S, U
        l.request_status                    AS Status,            -- GRANT or WAIT
        CASE l.resource_type
            WHEN 'OBJECT' THEN OBJECT_NAME(l.resource_associated_entity_id, l.resource_database_id)
            ELSE OBJECT_NAME(p.object_id, l.resource_database_id) END AS TableName,
        l.resource_description              AS Resource
FROM sys.dm_tran_locks AS l
LEFT JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
WHERE l.resource_database_id = DB_ID(N'ApexPostDb')
  AND l.resource_type <> 'DATABASE'
ORDER BY l.request_session_id, l.resource_type;

-- 3. Head of the blocking chain and its open transaction
SELECT  s.session_id                        AS HeadBlocker,
        s.login_name                        AS LoginName,
        s.status                            AS SessionStatus,     -- "sleeping" = idle with an open transaction: the classic culprit
        s.open_transaction_count            AS OpenTransactions,
        s.last_request_end_time             AS LastRequestEnded
FROM sys.dm_exec_sessions AS s
WHERE s.session_id IN (SELECT blocking_session_id FROM sys.dm_exec_requests WHERE blocking_session_id <> 0)
  AND s.session_id NOT IN (SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id <> 0);

-- Ending it from here instead of window 1 (use with care, it rolls the blocker back):
--   KILL <HeadBlocker>;
