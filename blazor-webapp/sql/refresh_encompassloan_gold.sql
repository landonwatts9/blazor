-- Manual refresh of dbo.EncompassLoan_Gold from dbo.vw_EncompassLoan_Silver.
-- Re-run whenever dashboards need fresher data than the last snapshot.
--
-- Run via sqlcmd:
--   sqlcmd -S "AMB-SQL\AMBSQL" -d SAM_Reporting -E -i refresh_encompassloan_gold.sql
-- Or paste into a SSMS query window targeting SAM_Reporting.

USE SAM_Reporting;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    TRUNCATE TABLE dbo.EncompassLoan_Gold;

    INSERT INTO dbo.EncompassLoan_Gold
    SELECT * FROM dbo.vw_EncompassLoan_Silver;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

-- After a refresh, app's IMemoryCache may still hold stale results for up to 5 minutes.
-- Either wait for TTL or recycle the IIS app pool to drop cache immediately.
