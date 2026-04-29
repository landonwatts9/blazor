-- Wraps the gold-table refresh in a stored procedure so the SQL Agent job step
-- is just one line: EXEC dbo.usp_Refresh_EncompassLoan_Gold
-- Idempotent: re-runnable to update the proc definition.

USE SAM_Reporting;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Note: SET ANSI_NULLS / QUOTED_IDENTIFIER ON outside the proc is what gets
-- baked into the procedure metadata at create time. Re-running this script
-- with those flags ON is what the SQL Agent job needs.
CREATE OR ALTER PROCEDURE dbo.usp_Refresh_EncompassLoan_Gold
AS
BEGIN
    SET NOCOUNT ON;
    SET ARITHABORT ON;        -- required for indexed-view-aware INSERT
    SET CONCAT_NULL_YIELDS_NULL ON;
    SET ANSI_WARNINGS ON;
    SET ANSI_PADDING ON;
    SET NUMERIC_ROUNDABORT OFF;

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
END
GO
