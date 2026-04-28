-- One-time DDL to add primary key + covering indexes to dbo.EncompassLoan_Gold.
-- Required because SELECT INTO doesn't carry indexes from the source view.
-- Re-runnable: each ALTER/CREATE is wrapped in an existence check.

USE SAM_Reporting;
GO

-- SELECT INTO inherits nullability; gold's loan_number is nullable. PKs require NOT NULL.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.EncompassLoan_Gold') AND name = 'loan_number' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.EncompassLoan_Gold ALTER COLUMN loan_number BIGINT NOT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.EncompassLoan_Gold')
      AND type = 'PK'
)
BEGIN
    ALTER TABLE dbo.EncompassLoan_Gold
        ADD CONSTRAINT PK_EncompassLoan_Gold PRIMARY KEY CLUSTERED (loan_number);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Gold_funding_date' AND object_id = OBJECT_ID('dbo.EncompassLoan_Gold'))
BEGIN
    CREATE INDEX IX_Gold_funding_date ON dbo.EncompassLoan_Gold (funding_date)
        INCLUDE (loanamount_fundedfinal, loanchannel_summary, loanofficer_name, funded_flag, loan_purpose);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Gold_estclose_pipeline' AND object_id = OBJECT_ID('dbo.EncompassLoan_Gold'))
BEGIN
    CREATE INDEX IX_Gold_estclose_pipeline ON dbo.EncompassLoan_Gold (estimatedclosing_date)
        INCLUDE (currentloanfolder, funded_flag, loanchannel_summary, milestone_status, loanofficer_name, loanamount, loanamount_fundedfinal, loan_purpose)
        WHERE funded_flag = 0;
END
GO

-- Skipping an index on uw_currentstatus / currentloanfolder: both are NVARCHAR(MAX) in
-- the gold table (inherited from silver) and can't be index key columns. Processor
-- pipeline query falls back to full scan, which is fine at gold's row count.
