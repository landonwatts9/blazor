-- Adds current_underwritingstatus_date to vw_EncompassLoan_Silver, sourced from CX.UW.LOGSTATUS.
-- Applied 2026-04-15 to AMB-SQL\AMBSQL / SAM_Reporting. Additive change only.
-- Full view re-creation is required because the change lives inside the BaseColumns CTE;
-- diff vs. prior definition is a single new CASE block after uw_currentstatus.

-- To apply, either:
--   1) sqlcmd -S "AMB-SQL\AMBSQL" -d SAM_Reporting -E -i <this-file>
--   2) Paste the CASE block into the BaseColumns CTE of the current view definition
--      (right after the line: CAST([CX.UW.STATUS] AS NVARCHAR(MAX)) AS uw_currentstatus,)
--      and run CREATE OR ALTER VIEW.

-- New column expression:
/*
    CASE
        WHEN [CX.UW.LOGSTATUS] IS NULL OR LTRIM(RTRIM([CX.UW.LOGSTATUS])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.UW.LOGSTATUS], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.UW.LOGSTATUS], 101)
    END AS current_underwritingstatus_date,
*/
