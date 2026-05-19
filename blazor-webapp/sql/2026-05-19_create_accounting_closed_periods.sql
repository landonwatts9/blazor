-- Manual gating table for the executive Financial dashboard.
-- Income statement and balance sheet tabs only expose months that
-- appear in this table. Accounting (or anyone with SSMS access)
-- inserts a row when a month's close is finalized; deletes a row
-- to pull it back if a correction is needed.
--
-- One-time DDL. Idempotent: safe to re-run.

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE schema_id = SCHEMA_ID('dbo') AND name = 'AccountingClosedPeriods'
)
BEGIN
    CREATE TABLE dbo.AccountingClosedPeriods (
        period_end DATE          NOT NULL PRIMARY KEY,   -- last day of the closed month
        closed_at  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
        closed_by  NVARCHAR(200) NULL,
        notes      NVARCHAR(500) NULL
    );
END;
GO

-- Seed example for a single month (uncomment + adjust before running):
--   INSERT INTO dbo.AccountingClosedPeriods (period_end, closed_by, notes)
--   VALUES ('2026-04-30', SUSER_SNAME(), 'April close finalized');

-- Historical backfill: marks every month-end from 2019-01-31 through 2026-04-30
-- as closed (88 rows). Useful when standing the dashboard up for the first time;
-- adjust the start date and TOP (n) for a different window.
--   INSERT INTO dbo.AccountingClosedPeriods (period_end, closed_by, notes)
--   SELECT EOMONTH(DATEADD(MONTH, n, '2019-01-01')), SUSER_SNAME(), 'historical backfill'
--   FROM (SELECT TOP (88) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
--         FROM sys.all_objects) m;

-- Grant the financial dashboard to specific users via the existing
-- DashboardAccess table:
--   INSERT INTO dbo.DashboardAccess (username, dashboard_key, granted_by)
--   VALUES ('SAM\landon.watts', 'financial', 'SAM\landon.watts');
