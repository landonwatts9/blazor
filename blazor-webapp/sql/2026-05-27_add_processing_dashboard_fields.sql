CREATE OR ALTER VIEW dbo.vw_EncompassLoan_Silver
AS
WITH RankedLoans AS (
    -- Step 1: Deduplicate (get latest row per loan number)
    -- Note: We do NOT filter test folder yet - that happens AFTER getting latest version
    SELECT
        *,
        ROW_NUMBER() OVER (
            PARTITION BY encompass_loan_number
            ORDER BY created_at DESC, ID DESC
        ) AS rn
    FROM Encompass.dbo.reports_data
    WHERE encompass_loan_number IS NOT NULL  -- Exclude NULL loan numbers
      AND LTRIM(RTRIM(CAST(encompass_loan_number AS NVARCHAR(MAX)))) NOT IN ('', '//')  -- Exclude empty/invalid values
      AND TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(encompass_loan_number AS NVARCHAR(MAX)))), ',', '') AS BIGINT) IS NOT NULL  -- Exclude non-numeric values
),
LatestLoans AS (
    -- Step 2: Keep only latest version of each loan, then exclude test folder loans
    SELECT *
    FROM RankedLoans
    WHERE rn = 1
      AND ([cx.km.movetofolder] IS NULL OR UPPER(LTRIM(RTRIM([cx.km.movetofolder]))) NOT IN ('TEST', 'OB TEST'))
),
BaseColumns AS (
    -- Step 3: Apply base transformations (column mapping, type casting, cleaning)
    SELECT
    CASE
        WHEN ID IS NULL OR LTRIM(RTRIM(CAST(ID AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(ID AS NVARCHAR(MAX)))), ',', '') AS BIGINT)
    END AS sql_id,
    CASE
        WHEN created_at IS NULL OR LTRIM(RTRIM(created_at)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, created_at, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, created_at, 101)
    END AS created_date,
    LTRIM(RTRIM(primary_borrower_email)) AS primary_borrower_email,
    LTRIM(RTRIM(primary_borrower_address)) AS primary_borrower_address,
    LTRIM(RTRIM(primary_borrower_city)) AS primary_borrower_city,
    UPPER(LTRIM(RTRIM(primary_borrower_state))) AS primary_borrower_state,
    LTRIM(RTRIM(primary_borrower_zip)) AS primary_borrower_zip,
    LTRIM(RTRIM(primary_borrower_mailing_address)) AS primary_borrower_mailing_address,
    LTRIM(RTRIM(primary_borrower_mailing_city)) AS primary_borrower_mailing_city,
    UPPER(LTRIM(RTRIM(primary_borrower_mailing_state))) AS primary_borrower_mailing_state,
    LTRIM(RTRIM(primary_borrower_mailing_zip)) AS primary_borrower_mailing_zip,
    LTRIM(RTRIM(assigned_loan_officer_email)) AS assigned_loan_officer_email,
    CASE
        WHEN encompass_loan_number IS NULL OR LTRIM(RTRIM(CAST(encompass_loan_number AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(encompass_loan_number AS NVARCHAR(MAX)))), ',', '') AS BIGINT)
    END AS loan_number,
    LTRIM(RTRIM(milestone_status)) AS milestone_status,
    CASE
        WHEN interest_rate IS NULL OR LTRIM(RTRIM(CAST(interest_rate AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(interest_rate AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,3))
    END AS interest_rate,
    CASE
        WHEN purchase_price IS NULL OR LTRIM(RTRIM(CAST(purchase_price AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(purchase_price AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS purchase_price,
    CASE
        WHEN loan_amount IS NULL OR LTRIM(RTRIM(CAST(loan_amount AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(loan_amount AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS loanamount_excludingmip,
    LTRIM(RTRIM(loan_purpose)) AS loan_purpose,
    LTRIM(RTRIM(loan_type)) AS loan_type,
    CAST(occupancy AS NVARCHAR(MAX)) AS occupancy,
    CASE
        WHEN est_closing_date IS NULL OR LTRIM(RTRIM(est_closing_date)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, est_closing_date, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, est_closing_date, 101)
    END AS est_closing_date,
    COALESCE(CASE
        WHEN appraised_value IS NULL OR LTRIM(RTRIM(CAST(appraised_value AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(appraised_value AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END, 0) AS appraised_value,
    CASE
        WHEN appraisal_received_date IS NULL OR LTRIM(RTRIM(appraisal_received_date)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, appraisal_received_date, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, appraisal_received_date, 101)
    END AS appraisal_received_date,
    CASE
        WHEN appraisal_order_date IS NULL OR LTRIM(RTRIM(appraisal_order_date)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, appraisal_order_date, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, appraisal_order_date, 101)
    END AS appraisal_order_date,
    LTRIM(RTRIM(seller_company_name)) AS seller_company_name,
    LTRIM(RTRIM(seller_agent_name)) AS seller_agent_name,
    LTRIM(RTRIM(seller_agent_phone)) AS seller_agent_phone,
    LTRIM(RTRIM(seller_agent_email)) AS seller_agent_email,
    LTRIM(RTRIM(subject_property_address)) AS subject_property_address,
    LTRIM(RTRIM(subject_property_city)) AS subject_property_city,
    UPPER(LTRIM(RTRIM(subject_property_state))) AS subject_property_state,
    LTRIM(RTRIM(subject_property_zip)) AS subject_property_zip,
    LTRIM(RTRIM(loa_full_name)) AS loa_full_name,
    LTRIM(RTRIM(loa_full_email)) AS loa_full_email,
    LTRIM(RTRIM([618])) AS appraiser_name,
    LTRIM(RTRIM([622])) AS appraiser_phone,
    LTRIM(RTRIM([89])) AS appraiser_email,
    CAST([4004] AS NVARCHAR(MAX)) AS coborrower_firstname,
    CAST([4006] AS NVARCHAR(MAX)) AS coborrower_lastname,
    LTRIM(RTRIM([1480])) AS coborrower_phone,
    LTRIM(RTRIM([1268])) AS coborrower_email,
    CAST([13] AS NVARCHAR(MAX)) AS property_county,
    LTRIM(RTRIM([610])) AS escrow_company,
    LTRIM(RTRIM([611])) AS escrow_agent,
    LTRIM(RTRIM([615])) AS escrow_phone,
    LTRIM(RTRIM([87])) AS escrow_email,
    CASE
        WHEN [1484] IS NULL OR LTRIM(RTRIM(CAST([1484] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1484] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS borrower_minimumfico,
    CAST(NeedsListRequested AS NVARCHAR(MAX)) AS needslist_requested,
    CASE
        WHEN [HMDA.X83] IS NULL OR LTRIM(RTRIM(CAST([HMDA.X83] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([HMDA.X83] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS hmdaterm_months,
    LTRIM(RTRIM([VEND.X140])) AS buyersagent_phone,
    LTRIM(RTRIM([VEND.X139])) AS buyersagent_name,
    LTRIM(RTRIM([VEND.X141])) AS buyersagent_email,
    CASE
        WHEN [3152] IS NULL OR LTRIM(RTRIM([3152])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3152], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3152], 101)
    END AS tildisclosure_date,
    CAST([CX.LOA.NEEDSLIST.REQUESTED] AS NVARCHAR(MAX)) AS loaneedslist_requested,
    CASE
        WHEN [CX.LTT.UT.IMPDATE.1] IS NULL OR LTRIM(RTRIM([CX.LTT.UT.IMPDATE.1])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.LTT.UT.IMPDATE.1], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.LTT.UT.IMPDATE.1], 101)
    END AS financingappraisaldue_date,
    CASE
        WHEN [CX.OS.PROCESSING.ORDERED] IS NULL OR LTRIM(RTRIM([CX.OS.PROCESSING.ORDERED])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.OS.PROCESSING.ORDERED], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.OS.PROCESSING.ORDERED], 101)
    END AS processingordered_date,
    CASE
        WHEN CUST08FV IS NULL OR LTRIM(RTRIM(CUST08FV)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, CUST08FV, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, CUST08FV, 101)
    END AS estimatedcddelivery_date,
    CAST([CX.PROCESSOR.NEEDSLIST.REQ] AS NVARCHAR(MAX)) AS processorneedslist_requested,
    LTRIM(RTRIM([88])) AS titlecompany_email,
    CAST([CX.UW.STATUS] AS NVARCHAR(MAX)) AS uw_currentstatus,
    CASE
        WHEN [CX.UW.LOGSTATUS] IS NULL OR LTRIM(RTRIM(CAST([CX.UW.LOGSTATUS] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATETIME2, [CX.UW.LOGSTATUS], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATETIME2, [CX.UW.LOGSTATUS], 101)
    END AS current_underwritingstatus_date,
    CAST([4000] AS NVARCHAR(MAX)) AS borrower_firstname,
    CAST([4002] AS NVARCHAR(MAX)) AS borrower_lastname,
    CAST([66] AS NVARCHAR(MAX)) AS borrower_homephone,
    CAST([1490] AS NVARCHAR(MAX)) AS borrower_cellphone,
    LTRIM(RTRIM([1612])) AS loanofficer_name,
    CASE
        WHEN [1402] IS NULL OR LTRIM(RTRIM([1402])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1402], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1402], 101)
    END AS borrower_birthdate,
    CASE
        WHEN [1403] IS NULL OR LTRIM(RTRIM([1403])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1403], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1403], 101)
    END AS coborrower_birthdate,
    CAST([CX.FILE.PREQUALIFIED] AS NVARCHAR(MAX)) AS file_prequalified,
    CAST([CX.COMPANY.LEAD.SOURCE] AS NVARCHAR(MAX)) AS companylead_source,
    LTRIM(RTRIM([3968])) AS loanofficer_email,
    CAST([1401] AS NVARCHAR(MAX)) AS loan_program,
    CASE
        WHEN [763] IS NULL OR LTRIM(RTRIM([763])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [763], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [763], 101)
    END AS estimatedclosing_date,
    CASE
        WHEN [CUST09FV] IS NULL OR LTRIM(RTRIM(CAST([CUST09FV] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATETIME2, [CUST09FV], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATETIME2, [CUST09FV], 101)
    END AS expected_signing_date,
    CAST([2626] AS NVARCHAR(MAX)) AS channel,
    CASE
        WHEN [CX.GBL.FLAG] IS NULL THEN NULL
        WHEN UPPER(LTRIM(RTRIM([CX.GBL.FLAG]))) IN ('TRUE', '1', 'YES', 'Y') THEN 1
        ELSE 0
    END AS goodbyeletter_flag,
    CAST([3535] AS NVARCHAR(MAX)) AS ratelock_sellsideservicer,
    CASE
        WHEN [LE2.X25] IS NULL OR LTRIM(RTRIM(CAST([LE2.X25] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([LE2.X25] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS estimated_cashtoclose,
    CASE
        WHEN [CX.CDBWT.FLAG] IS NULL THEN NULL
        WHEN UPPER(LTRIM(RTRIM([CX.CDBWT.FLAG]))) IN ('TRUE', '1', 'YES', 'Y') THEN 1
        ELSE 0
    END AS cdbalanced_withtitle,
    CASE
        WHEN [NEWHUD2.X300] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X300] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X300] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS origination_fee,
    CAST([CX.CRM.SYNC] AS NVARCHAR(MAX)) AS crmsync,
    LTRIM(RTRIM([608])) AS amortization_type,
    CASE
        WHEN ORGID IS NULL OR LTRIM(RTRIM(CAST(ORGID AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(ORGID AS NVARCHAR(MAX)))), ',', '') AS BIGINT)
    END AS branch_id,
    CAST([1393] AS NVARCHAR(MAX)) AS loanstatus_current,
    CAST([420] AS NVARCHAR(MAX)) AS lien_position,
    CASE
        WHEN [2] IS NULL OR LTRIM(RTRIM(CAST([2] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS loanamount,
    CASE
        WHEN [353] IS NULL OR LTRIM(RTRIM(CAST([353] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([353] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS ltv,
    CAST([Log.MS.Stage] AS NVARCHAR(MAX)) AS next_expectedmilestone,
    LTRIM(RTRIM([1553])) AS property_type,
    CASE
        WHEN [4] IS NULL OR LTRIM(RTRIM(CAST([4] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([4] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS term_months,
    CASE
        WHEN [742] IS NULL OR LTRIM(RTRIM(CAST([742] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([742] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS dti,
    CASE
        WHEN [VASUMM.X23] IS NULL OR LTRIM(RTRIM(CAST([VASUMM.X23] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([VASUMM.X23] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS creditscore_qualifying,
    LTRIM(RTRIM([617])) AS appraisalcompany_name,
    LTRIM(RTRIM([362])) AS processor_name,
    LTRIM(RTRIM([1991])) AS funder_name,
    LTRIM(RTRIM([317])) AS DNU_loanofficer_name,
    CASE
        WHEN [3238] IS NULL OR LTRIM(RTRIM(CAST([3238] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3238] AS NVARCHAR(MAX)))), ',', '') AS BIGINT)
    END AS loanofficer_nmlsid,
    LTRIM(RTRIM([CX.UWD.PLATINUM])) AS platinum_status,
    LTRIM(RTRIM([VEND.X263])) AS investor_name,
    LTRIM(RTRIM([VEND.X178])) AS servicingcompany_name,
    LTRIM(RTRIM([984])) AS DNU_underwriter_name,
    LTRIM(RTRIM([VEND.X200])) AS warehousebank_name,
    CASE
        WHEN [3475] IS NULL OR LTRIM(RTRIM(CAST([3475] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3475] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension1_bps,
    CASE
        WHEN [3477] IS NULL OR LTRIM(RTRIM(CAST([3477] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3477] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension2_bps,
    CASE
        WHEN [3479] IS NULL OR LTRIM(RTRIM(CAST([3479] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3479] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension3_bps,
    CASE
        WHEN [3481] IS NULL OR LTRIM(RTRIM(CAST([3481] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3481] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension4_bps,
    CASE
        WHEN [3483] IS NULL OR LTRIM(RTRIM(CAST([3483] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3483] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension5_bps,
    CASE
        WHEN [3485] IS NULL OR LTRIM(RTRIM(CAST([3485] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3485] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension6_bps,
    CASE
        WHEN [3487] IS NULL OR LTRIM(RTRIM(CAST([3487] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3487] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension7_bps,
    CASE
        WHEN [3489] IS NULL OR LTRIM(RTRIM(CAST([3489] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3489] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension8_bps,
    CASE
        WHEN [3491] IS NULL OR LTRIM(RTRIM(CAST([3491] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3491] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension9_bps,
    CASE
        WHEN [3493] IS NULL OR LTRIM(RTRIM(CAST([3493] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3493] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockextension10_bps,
    CASE
        WHEN [2090] IS NULL OR LTRIM(RTRIM(CAST([2090] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2090] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS originallock_numberofdays,
    CASE
        WHEN [2295] IS NULL OR LTRIM(RTRIM(CAST([2295] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2295] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_totalsellprice,
    CAST([2287] AS NVARCHAR(MAX)) AS sellside_investorlocktype,
    CAST([2278] AS NVARCHAR(MAX)) AS sellside_investorname,
    CASE
        WHEN [CX.RETAILFUND.LOCKEDWHOLE] IS NULL THEN NULL
        WHEN UPPER(LTRIM(RTRIM([CX.RETAILFUND.LOCKEDWHOLE]))) IN ('TRUE', '1', 'YES', 'Y') THEN 1
        ELSE 0
    END AS wholeloan_flag,
    CASE
        WHEN [1996] IS NULL OR LTRIM(RTRIM([1996])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1996], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1996], 101)
    END AS funding_date,
    CASE
        WHEN [HMDA.X29] IS NULL OR LTRIM(RTRIM([HMDA.X29])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [HMDA.X29], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [HMDA.X29], 101)
    END AS hmda_applicationdate,
    CASE
        WHEN [3156] IS NULL OR LTRIM(RTRIM([3156])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3156], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3156], 101)
    END AS lastcdsent_date,
    CASE
        WHEN [745] IS NULL OR LTRIM(RTRIM([745])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [745], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [745], 101)
    END AS lead_applicationdate,
    LTRIM(RTRIM([624])) AS creditreportingagency_name,
    LTRIM(RTRIM([411])) AS titlecompany_name,
    CASE
        WHEN [2149] IS NULL OR LTRIM(RTRIM([2149])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2149], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2149], 101)
    END AS buyside_lockdate,
    CASE
        WHEN [2151] IS NULL OR LTRIM(RTRIM([2151])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2151], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2151], 101)
    END AS buyside_lockexpirationdate,
    CASE
        WHEN [2297] IS NULL OR LTRIM(RTRIM([2297])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2297], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2297], 101)
    END AS sellside_investordeliverydate,
    CASE
        WHEN [2291] IS NULL OR LTRIM(RTRIM([2291])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2291], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2291], 101)
    END AS sellside_lockdate,
    CASE
        WHEN [2222] IS NULL OR LTRIM(RTRIM([2222])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2222], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2222], 101)
    END AS sellside_lockexpirationdate,
    COALESCE(CASE
        WHEN [2161] IS NULL OR LTRIM(RTRIM(CAST([2161] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2161] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END, 0) AS buyside_baseprice,
    CASE
        WHEN [2203] IS NULL OR LTRIM(RTRIM(CAST([2203] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2203] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS buyside_basepriceratenetbuyprice,
    CASE
        WHEN [2202] IS NULL OR LTRIM(RTRIM(CAST([2202] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2202] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS buyside_nasepricetotaladjustment,
    CASE
        WHEN [2150] IS NULL OR LTRIM(RTRIM(CAST([2150] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2150] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS buyside_numberofdays,
    CASE
        WHEN [2218] IS NULL OR LTRIM(RTRIM(CAST([2218] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2218] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS buyside_totalbuyprice,
    CASE
        WHEN [3424] IS NULL OR LTRIM(RTRIM(CAST([3424] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3424] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecm_premium,
    CASE
        WHEN [2232] IS NULL OR LTRIM(RTRIM(CAST([2232] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2232] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_basepricerate,
    CASE
        WHEN [2273] IS NULL OR LTRIM(RTRIM(CAST([2273] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2273] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_basepricetotaladjustment,
    CASE
        WHEN [2223] IS NULL OR LTRIM(RTRIM(CAST([2223] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2223] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_baserate,
    CASE
        WHEN [2274] IS NULL OR LTRIM(RTRIM(CAST([2274] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2274] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_netsellprice,
    CASE
        WHEN [2221] IS NULL OR LTRIM(RTRIM(CAST([2221] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2221] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS sellside_numberofdays,
    CASE
        WHEN [2276] IS NULL OR LTRIM(RTRIM(CAST([2276] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2276] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_srppaid,
    CASE
        WHEN [3428] IS NULL OR LTRIM(RTRIM(CAST([3428] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3428] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS servicerelease_premium,
    CASE
        WHEN [2286] IS NULL OR LTRIM(RTRIM(CAST([2286] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2286] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellside_investorcommitment,
    CAST([2288] AS NVARCHAR(MAX)) AS sellside_investorloannumber,
    CASE
        WHEN [3835] IS NULL OR LTRIM(RTRIM(CAST([3835] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3835] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS ratelock_comparisontotalcomparisonprice,
    CASE
        WHEN [3142] IS NULL OR LTRIM(RTRIM([3142])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3142], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3142], 101)
    END AS application3142_date,
    CASE
        WHEN [3977] IS NULL OR LTRIM(RTRIM([3977])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3977], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3977], 101)
    END AS closingdisclosuresent_date,
    CASE
        WHEN [2553] IS NULL OR LTRIM(RTRIM([2553])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2553], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2553], 101)
    END AS closingdocs_regzdisbursement_date,
    CASE
        WHEN [MS.STATUSDATE] IS NULL OR LTRIM(RTRIM([MS.STATUSDATE])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [MS.STATUSDATE], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [MS.STATUSDATE], 101)
    END AS currentmilestone_date,
    CASE
        WHEN [749] IS NULL OR LTRIM(RTRIM([749])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [749], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [749], 101)
    END AS currentstatus_date,
    CASE
        WHEN [DENIAL.X69] IS NULL OR LTRIM(RTRIM([DENIAL.X69])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [DENIAL.X69], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [DENIAL.X69], 101)
    END AS denial_date,
    CASE
        WHEN [682] IS NULL OR LTRIM(RTRIM([682])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [682], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [682], 101)
    END AS firstpayment_date,
    CASE
        WHEN [1999] IS NULL OR LTRIM(RTRIM([1999])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1999], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1999], 101)
    END AS fundingreleased_date,
    CASE
        WHEN [1997] IS NULL OR LTRIM(RTRIM([1997])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1997], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1997], 101)
    END AS fundingsent_date,
    CASE
        WHEN [3148] IS NULL OR LTRIM(RTRIM([3148])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3148], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3148], 101)
    END AS gfeinitialdisclosureprovided_date,
    CASE
        WHEN [3143] IS NULL OR LTRIM(RTRIM([3143])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3143], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3143], 101)
    END AS initialdisclosuredue_date,
    CASE
        WHEN [761] IS NULL OR LTRIM(RTRIM([761])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [761], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [761], 101)
    END AS lock_date,
    CASE
        WHEN [2370] IS NULL OR LTRIM(RTRIM([2370])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2370], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2370], 101)
    END AS purchaseadvice_date,
    CASE
        WHEN [762] IS NULL OR LTRIM(RTRIM([762])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [762], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [762], 101)
    END AS ratelockexpires_date,
    CASE
        WHEN [CX.KM.LOA.NEEDSLIST.DATE] IS NULL OR LTRIM(RTRIM([CX.KM.LOA.NEEDSLIST.DATE])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.KM.LOA.NEEDSLIST.DATE], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.KM.LOA.NEEDSLIST.DATE], 101)
    END AS tcneedslistrequested_Date,
    CASE
        WHEN [2014] IS NULL OR LTRIM(RTRIM([2014])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2014], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2014], 101)
    END AS shipping_date,
    CASE
        WHEN [2301] IS NULL OR LTRIM(RTRIM([2301])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2301], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2301], 101)
    END AS underwritingapproval_date,
    CASE
        WHEN [2303] IS NULL OR LTRIM(RTRIM([2303])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2303], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2303], 101)
    END AS underwritingsuspended_date,
    CASE
        WHEN [CX.REVERSE.AMTDELIVERED] IS NULL OR LTRIM(RTRIM(CAST([CX.REVERSE.AMTDELIVERED] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.REVERSE.AMTDELIVERED] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecm_amountdelivered,
    CASE
        WHEN [NEWHUD2.X927] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X927] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X927] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS discount_points,
    CASE
        WHEN [NEWHUD2.X498] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X498] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X498] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS processing_fee,
    CASE
        WHEN [NEWHUD2.X399] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X399] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X399] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS docprep_fee,
    CASE
        WHEN [CX.PREMIUM] IS NULL OR LTRIM(RTRIM(CAST([CX.PREMIUM] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.PREMIUM] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS forward_premium,
    CASE
        WHEN [CX.DISCOUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.DISCOUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.DISCOUNT] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS discount_fee,
    CASE
        WHEN [2389] IS NULL OR LTRIM(RTRIM(CAST([2389] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2389] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS secondarymarket_gainloss,
    CASE
        WHEN [2373] IS NULL OR LTRIM(RTRIM(CAST([2373] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2373] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS transfer_fee,
    CASE
        WHEN [4083] IS NULL OR LTRIM(RTRIM(CAST([4083] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([4083] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS lender_credits,
    CASE
        WHEN [CX.CLOSING.COSTS.HECM] IS NULL OR LTRIM(RTRIM(CAST([CX.CLOSING.COSTS.HECM] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.CLOSING.COSTS.HECM] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecmclosingcosts,
    CASE
        WHEN [CX.WHOLELOAN] IS NULL OR LTRIM(RTRIM(CAST([CX.WHOLELOAN] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.WHOLELOAN] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS wholeloan_price,
    CASE
        WHEN [NEWHUD.X139] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X139] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X139] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_lock_fees,
    CAST([1822] AS NVARCHAR(MAX)) AS referralsource,
    CASE
        WHEN [CX.REVERSE.SAMPAIDCC] IS NULL OR LTRIM(RTRIM(CAST([CX.REVERSE.SAMPAIDCC] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.REVERSE.SAMPAIDCC] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sam_paid_closing_costs,
    CASE
        WHEN [CX.REVERSE.ORIGAMT] IS NULL OR LTRIM(RTRIM(CAST([CX.REVERSE.ORIGAMT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.REVERSE.ORIGAMT] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecm_origination_fee,
    CAST([CX.COMPANY.LEAD] AS NVARCHAR(MAX)) AS companylead,
    CASE
        WHEN CUST32FV IS NULL OR LTRIM(RTRIM(CAST(CUST32FV AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(CUST32FV AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS additional_sam_paid_closing_costs,
    CASE
        WHEN [1625] IS NULL OR LTRIM(RTRIM(CAST([1625] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1625] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs,
    CASE
        WHEN [1839] IS NULL OR LTRIM(RTRIM(CAST([1839] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1839] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_2,
    CASE
        WHEN [1842] IS NULL OR LTRIM(RTRIM(CAST([1842] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1842] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_3,
    CASE
        WHEN [NEWHUD.X733] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X733] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X733] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_4,
    CASE
        WHEN [NEWHUD.X1237] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1237] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1237] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_5,
    CASE
        WHEN [NEWHUD.X1245] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1245] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1245] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_6,
    CASE
        WHEN [NEWHUD.X1253] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1253] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1253] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_7,
    CASE
        WHEN [NEWHUD.X1261] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1261] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1261] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_8,
    CASE
        WHEN [NEWHUD.X1269] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1269] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1269] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_9,
    CASE
        WHEN [NEWHUD.X1277] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1277] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1277] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_10,
    CASE
        WHEN [NEWHUD.X1285] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1285] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1285] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrower_paid_closing_costs_11,
    CASE
        WHEN [200] IS NULL OR LTRIM(RTRIM(CAST([200] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([200] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs,
    CASE
        WHEN [1626] IS NULL OR LTRIM(RTRIM(CAST([1626] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1626] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_2,
    CASE
        WHEN [1840] IS NULL OR LTRIM(RTRIM(CAST([1840] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1840] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_3,
    CASE
        WHEN [1843] IS NULL OR LTRIM(RTRIM(CAST([1843] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1843] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_4,
    CASE
        WHEN [NEWHUD.X779] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X779] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X779] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_5,
    CASE
        WHEN [NEWHUD.X1238] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1238] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1238] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_6,
    CASE
        WHEN [NEWHUD.X1246] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1246] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1246] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_7,
    CASE
        WHEN [NEWHUD.X1254] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1254] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1254] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_8,
    CASE
        WHEN [NEWHUD.X1262] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1262] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1262] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_9,
    CASE
        WHEN [NEWHUD.X1270] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1270] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1270] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_10,
    CASE
        WHEN [NEWHUD.X1286] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1286] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1286] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS seller_paid_closing_costs_11,
    CASE
        WHEN [1821] IS NULL OR LTRIM(RTRIM(CAST([1821] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([1821] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecm_projectedloanamount,
    CASE
        WHEN [CX.MAX.CLAIM.AMOUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.MAX.CLAIM.AMOUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.MAX.CLAIM.AMOUNT] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hecm_maxclaimamount,
    CASE
        WHEN [CX.BROKER.FEES] IS NULL OR LTRIM(RTRIM(CAST([CX.BROKER.FEES] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.BROKER.FEES] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS broker_fees,
    CASE
        WHEN [MS.START] IS NULL OR LTRIM(RTRIM([MS.START])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [MS.START], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [MS.START], 101)
    END AS filestart_date,
    CASE
        WHEN [CX.INITIALUW.DT] IS NULL OR LTRIM(RTRIM([CX.INITIALUW.DT])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.INITIALUW.DT], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.INITIALUW.DT], 101)
    END AS underwritinginitialreview_date,
    CASE
        WHEN [2987] IS NULL OR LTRIM(RTRIM([2987])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [2987], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [2987], 101)
    END AS underwritingdenied_date,
    CASE
        WHEN [CX.UWSUB.DT] IS NULL OR LTRIM(RTRIM([CX.UWSUB.DT])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.UWSUB.DT], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.UWSUB.DT], 101)
    END AS underwritingsubmission_date,
    CASE
        WHEN L770 IS NULL OR LTRIM(RTRIM(L770)) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, L770, 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, L770, 101)
    END AS document_date,
    CAST([CX.CD.STATUS] AS NVARCHAR(MAX)) AS cdstatus,
    CASE
        WHEN [CX.ECDORDER.CERTIFY.DATE] IS NULL OR LTRIM(RTRIM(CAST([CX.ECDORDER.CERTIFY.DATE] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATETIME2, [CX.ECDORDER.CERTIFY.DATE], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATETIME2, [CX.ECDORDER.CERTIFY.DATE], 101)
    END AS early_cd_audit_date,
    CASE
        WHEN [CX.CDORDER.CERTIFY.DATE] IS NULL OR LTRIM(RTRIM(CAST([CX.CDORDER.CERTIFY.DATE] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATETIME2, [CX.CDORDER.CERTIFY.DATE], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATETIME2, [CX.CDORDER.CERTIFY.DATE], 101)
    END AS cd_audit_date,
    CASE
        WHEN [FV.X396] IS NULL OR LTRIM(RTRIM(CAST([FV.X396] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([FV.X396] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS cure_amount,
    CAST([3172] AS NVARCHAR(MAX)) AS toleranceresolution_comments,
    LTRIM(RTRIM([CX.UW.SUBTYPE])) AS underwritingsubmittal_type,
    CASE
        WHEN [78] IS NULL OR LTRIM(RTRIM([78])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [78], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [78], 101)
    END AS maturity_date,
    LTRIM(RTRIM([LoanTeamMember.Name.Trans Coordinator])) AS transactioncoordinator_name,
    CASE
        WHEN [Log.MS.Date.Docs Signing] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Docs Signing])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Docs Signing], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Docs Signing], 101)
    END AS docssigning_date,
    LTRIM(RTRIM([LoanTeamMember.Name.Closer])) AS closer_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Post Closer])) AS postcloser_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Shipper])) AS shipper_name,
    CAST(CoreMilestone AS NVARCHAR(MAX)) AS current_coremilestone,
    CAST([MS.STATUS] AS NVARCHAR(MAX)) AS current_milestone,
    CAST(LOANFOLDER AS NVARCHAR(MAX)) AS DNU_loanfolder,
    CASE
        WHEN [Log.MS.Date.Docs to Title] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Docs to Title])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Docs to Title], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Docs to Title], 101)
    END AS docstotitle_date,
    CASE
        WHEN [Log.MS.Date.Purchased] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Purchased])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Purchased], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Purchased], 101)
    END AS purchased_date,
    CASE
        WHEN [Log.MS.Date.Send to Closing] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Send to Closing])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Send to Closing], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Send to Closing], 101)
    END AS sendtoclosing_date,
    CASE
        WHEN [Log.MS.Date.Completion] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Completion])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Completion], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Completion], 101)
    END AS filecompletion_date,
    CASE
        WHEN [Log.MS.Date.Funding] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Funding])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Funding], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Funding], 101)
    END AS DNU_funding_date,
    CASE
        WHEN [Log.MS.Date.LO Qualification] IS NULL OR LTRIM(RTRIM([Log.MS.Date.LO Qualification])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.LO Qualification], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.LO Qualification], 101)
    END AS loqualification_date,
    CASE
        WHEN [Log.MS.Date.Processing] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Processing])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Processing], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Processing], 101)
    END AS processing_date,
    CASE
        WHEN [Log.MS.Date.LOA Review] IS NULL OR LTRIM(RTRIM([Log.MS.Date.LOA Review])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.LOA Review], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.LOA Review], 101)
    END AS loareview_date,
    CASE
        WHEN [Log.MS.Date.Reconciled] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Reconciled])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Reconciled], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Reconciled], 101)
    END AS reconciled_date,
    CASE
        WHEN [Task.DateCompleted.NeedsListRequested] IS NULL OR LTRIM(RTRIM([Task.DateCompleted.NeedsListRequested])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Task.DateCompleted.NeedsListRequested], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Task.DateCompleted.NeedsListRequested], 101)
    END AS needslistrequestedcompletion_date,
    CASE
        WHEN [Log.MS.Date.Shipping] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Shipping])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Shipping], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Shipping], 101)
    END AS shippinglog_date,
    CASE
        WHEN [Log.MS.Date.Underwriting] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Underwriting])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Underwriting], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Underwriting], 101)
    END AS underwriting_date,
    CASE
        WHEN [Log.MS.Date.TC Review] IS NULL OR LTRIM(RTRIM([Log.MS.Date.TC Review])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.TC Review], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.TC Review], 101)
    END AS tcreview_date,
    LTRIM(RTRIM([LoanTeamMember.Name.Loan Officer])) AS DNU2_loanofficer_name,
    LTRIM(RTRIM([LoanTeamMember.Name.LOA])) AS loa_name,
    LTRIM(RTRIM([LoanTeamMember.Name.LOA 1])) AS loa1_name,
    LTRIM(RTRIM([LoanTeamMember.Name.LOA 2])) AS loa2_name,
    LTRIM(RTRIM([LoanTeamMember.Name.LOA 3])) AS loa3_name,
    LTRIM(RTRIM([LoanTeamMember.Name.LOA 4])) AS loa4_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Production Partner])) AS productionpartner_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Loan Processor])) AS DNU_processor_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Helper Processor])) AS helperprocessor_name,
    LTRIM(RTRIM([LoanTeamMember.Name.Underwriter])) AS underwriter_name,
    LTRIM(RTRIM(PreProcessor_Name)) AS preprocessor_name,
    LTRIM(RTRIM([2825])) AS rateregistrationinvestor_name,
    CASE
        WHEN [1518] IS NULL OR LTRIM(RTRIM([1518])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [1518], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [1518], 101)
    END AS subjectpropertypurchase_date,
    CASE
        WHEN [Log.MS.DateTime.Pre-Processing] IS NULL OR LTRIM(RTRIM([Log.MS.DateTime.Pre-Processing])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.DateTime.Pre-Processing], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.DateTime.Pre-Processing], 101)
    END AS preprocessing_date,
    CAST([CX.LOANFOLDER] AS NVARCHAR(MAX)) AS DNU_loanfolder_Current,
    CAST([cx.km.movetofolder] AS NVARCHAR(MAX)) AS currentloanfolder,
    CASE
        WHEN [Log.MS.Date.Pre-Processing] IS NULL OR LTRIM(RTRIM([Log.MS.Date.Pre-Processing])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [Log.MS.Date.Pre-Processing], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [Log.MS.Date.Pre-Processing], 101)
    END AS preprocessing1_date,
    CASE
        WHEN [HMDA.X92] IS NULL OR LTRIM(RTRIM(CAST([HMDA.X92] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([HMDA.X92] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS hmda_repurchaseyear,
    CASE
        WHEN [HMDA.X93] IS NULL OR LTRIM(RTRIM(CAST([HMDA.X93] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([HMDA.X93] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS hmda_repurchaseamount,
    CAST([HMDA.X94] AS NVARCHAR(MAX)) AS hmda_repurchasepurchasetype,
    CAST([HMDA.X95] AS NVARCHAR(MAX)) AS hmda_repurchaseloanstatus,
    CASE
        WHEN [HMDA.X96] IS NULL OR LTRIM(RTRIM([HMDA.X96])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [HMDA.X96], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [HMDA.X96], 101)
    END AS hmda_repurchaseactiondate,
    CASE
        WHEN [3312] IS NULL OR LTRIM(RTRIM([3312])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [3312], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [3312], 101)
    END AS repurchase_date,
    CASE
        WHEN [3313] IS NULL OR LTRIM(RTRIM(CAST([3313] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3313] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS repurchase_cost,
    CASE
        WHEN [CX.LOAN.STORY.APPRAISALDUE] IS NULL OR LTRIM(RTRIM([CX.LOAN.STORY.APPRAISALDUE])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [CX.LOAN.STORY.APPRAISALDUE], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [CX.LOAN.STORY.APPRAISALDUE], 101)
    END AS appraisaldue_date,
    CAST([CX.BUYERSAGENT.FIRSTNAME] AS NVARCHAR(MAX)) AS buyersagent_firstname,
    CAST([CX.SELLERSAGENT.FIRSTNAME] AS NVARCHAR(MAX)) AS sellersagent_firstname,
    LTRIM(RTRIM([1409])) AS processor_email,
    CASE
        WHEN [4073] IS NULL THEN NULL
        WHEN UPPER(LTRIM(RTRIM([4073]))) IN ('TRUE', '1', 'YES', 'Y') THEN 1
        ELSE 0
    END AS authorizedcreditpull_flag,
    CASE
        WHEN [4074] IS NULL OR LTRIM(RTRIM([4074])) IN ('', '//') THEN NULL
        WHEN TRY_CONVERT(DATE, [4074], 101) < '1582-10-15' THEN NULL
        ELSE TRY_CONVERT(DATE, [4074], 101)
    END AS creditauthorized_date,
    CASE
        WHEN [3293] IS NULL OR LTRIM(RTRIM(CAST([3293] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3293] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS undiscounted_rate,
    CASE
        WHEN BrokerCheckAmount IS NULL OR LTRIM(RTRIM(CAST(BrokerCheckAmount AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(BrokerCheckAmount AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS brokercheck_amount,
    CAST([2976] AS NVARCHAR(MAX)) AS leadsource,
    CASE
        WHEN [NEWHUD2.X1150] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1150] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1150] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS newhud2_x1150,
    CASE
        WHEN [NEWHUD2.X1227] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1227] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1227] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS newhud2_x1227,
    CASE
        WHEN [NEWHUD2.X1151] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1151] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1151] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS newhud2_x1151,
    CASE
        WHEN [641] IS NULL OR LTRIM(RTRIM(CAST([641] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([641] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS appraisalfee_borrowerpaid,
    CASE
        WHEN [640] IS NULL OR LTRIM(RTRIM(CAST([640] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([640] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS creditreportfee_borrowerpaid,
    CASE
        WHEN [336] IS NULL OR LTRIM(RTRIM(CAST([336] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([336] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS taxservicefee_borrowerpaid,
    CASE
        WHEN [NEWHUD2.X400] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X400] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X400] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS floodcertfee_borrowerpaid,
    CASE
        WHEN [NEWHUD2.X32] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X32] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X32] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrowerpaidinadvance_totalfees,
    CASE
        WHEN [67] IS NULL OR LTRIM(RTRIM(CAST([67] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([67] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS experianfico,
    CASE
        WHEN [CX.RETAILFUND.LOCKEDSHORT] IS NULL OR LTRIM(RTRIM(CAST([CX.RETAILFUND.LOCKEDSHORT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.RETAILFUND.LOCKEDSHORT] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,5))
    END AS lockedshort_bps,
    CAST([CX.RETAILFUND.COMMENTS] AS NVARCHAR(MAX)) AS retailfundingcomments,
    CASE
        WHEN [CX.BROKER.CHECKAMT] IS NULL OR LTRIM(RTRIM(CAST([CX.BROKER.CHECKAMT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.BROKER.CHECKAMT] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS DNU_brokercheck_amount,
    CAST([CX.BROKER.FEESDESC] AS NVARCHAR(MAX)) AS brokerfee_description,
    CASE
        WHEN [CX.KM.UWC.ASSETS.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.ASSETS.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.ASSETS.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwasset_conditioncount,
    CASE
        WHEN [CX.KM.UWC.CREDIT.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.CREDIT.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.CREDIT.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwcredit_conditioncount,
    CASE
        WHEN [CX.KM.UWC.INCOME.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.INCOME.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.INCOME.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwincome_conditioncount,
    CASE
        WHEN [CX.KM.UWC.LEGAL.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.LEGAL.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.LEGAL.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwlegal_conditioncount,
    CASE
        WHEN [CX.KM.UWC.MISC.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.MISC.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.MISC.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS ummiscellaneous_conditioncount,
    CASE
        WHEN [CX.KM.UWC.PROPERTY.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.PROPERTY.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.PROPERTY.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwproperty_conditioncount,
    CASE
        WHEN [CX.KM.UWC.OPENINTERNAL.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.OPENINTERNAL.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.OPENINTERNAL.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwinternalcondition_currentcount,
    CASE
        WHEN [CX.KM.UWC.REQUESTED.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.REQUESTED.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.REQUESTED.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwrequestedcondition_currentcount,
    CASE
        WHEN [CX.KM.UWC.REREQUESTED.COUNT] IS NULL OR LTRIM(RTRIM(CAST([CX.KM.UWC.REREQUESTED.COUNT] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([CX.KM.UWC.REREQUESTED.COUNT] AS NVARCHAR(MAX)))), ',', '') AS INT)
    END AS uwrerequestedcondition_currentcount,
    CASE
        WHEN [NEWHUD.X1150] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X1150] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X1150] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS originationpoints_bps,
    CASE
        WHEN [2211] IS NULL OR LTRIM(RTRIM(CAST([2211] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2211] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS purchaseadvice_principal,
    CASE
        WHEN [2214] IS NULL OR LTRIM(RTRIM(CAST([2214] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2214] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS purchaseadvice_totaldue,
    CASE
        WHEN [2375] IS NULL OR LTRIM(RTRIM(CAST([2375] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2375] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS taxservice_fee,
    CASE
        WHEN [2377] IS NULL OR LTRIM(RTRIM(CAST([2377] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2377] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS floodservice_fee,
    CASE
        WHEN [2834] IS NULL OR LTRIM(RTRIM(CAST([2834] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2834] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS interest_fee,
    CASE
        WHEN [2835] IS NULL OR LTRIM(RTRIM(CAST([2835] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2835] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS impound_fee,
    CASE
        WHEN [558] IS NULL OR LTRIM(RTRIM(CAST([558] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([558] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS aggregateadjustment,
    CASE
        WHEN [NEWHUD2.X1092] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1092] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1092] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS appraisal_fee,
    CASE
        WHEN [NEWHUD2.X1125] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1125] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1125] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS creditreport_fee,
    CASE
        WHEN [NEWHUD2.X1158] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1158] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1158] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS taxservice_fee2,
    CASE
        WHEN [NEWHUD2.X1191] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1191] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1191] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS floodcert_fee,
    CASE
        WHEN [NEWHUD2.X1224] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X1224] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X1224] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS inspection_fee,
    CASE
        WHEN [NEWHUD2.X2148] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X2148] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X2148] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS interest2_fee,
    CASE
        WHEN [NEWHUD2.X2181] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X2181] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X2181] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS mip_fee,
    CASE
        WHEN [NEWHUD2.X2280] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X2280] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X2280] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS vafunding_fee,
    CASE
        WHEN [NEWHUD.X277] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X277] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X277] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS borrowerclosingcosts_total,
    CASE
        WHEN [NEWHUD.X278] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD.X278] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD.X278] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS sellerclosingcosts_total,
    CASE
        WHEN TOTPCC IS NULL OR LTRIM(RTRIM(CAST(TOTPCC AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST(TOTPCC AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS closingcosts_total,
    CASE
        WHEN [3371] IS NULL OR LTRIM(RTRIM(CAST([3371] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([3371] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS corporate_concession,
    CASE
        WHEN [NEWHUD2.X260] IS NULL OR LTRIM(RTRIM(CAST([NEWHUD2.X260] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([NEWHUD2.X260] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS discount_fee2,
    CASE
        WHEN [372] IS NULL OR LTRIM(RTRIM(CAST([372] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([372] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS appraisalmanagement_fee2,
    CASE
        WHEN [575] IS NULL OR LTRIM(RTRIM(CAST([575] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([575] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS appraisalmanagement_fee,
    CASE
        WHEN [2381] IS NULL OR LTRIM(RTRIM(CAST([2381] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([2381] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS latedelivery_fee,
    CASE
        WHEN [349] IS NULL OR LTRIM(RTRIM(CAST([349] AS NVARCHAR(MAX)))) IN ('', '//') THEN NULL
        ELSE TRY_CAST(REPLACE(LTRIM(RTRIM(CAST([349] AS NVARCHAR(MAX)))), ',', '') AS DECIMAL(19,2))
    END AS appraisalmanagement_fee3,
    CAST([352] AS NVARCHAR(MAX)) AS placeholder_352

    FROM LatestLoans
),
-- =====================================================
-- Step 4: Join with Conforming Limits for Jumbo Status
-- =====================================================
WithConformingLimits AS (
    SELECT
        bc.*,
        -- Conforming limit lookup based on property location and lock year
        cl.limit_1_unit AS conforming_limit_used,
        cl.limit_year AS conforming_limit_year,
        cl.is_high_cost AS property_is_highcost_area,
        cl.fips_code AS property_fips_code,
        -- Jumbo status: Compare loan amount to conforming limit
        CASE
            WHEN cl.limit_1_unit IS NULL THEN 'Unknown'
            WHEN bc.loanamount > cl.limit_1_unit THEN 'Jumbo'
            ELSE 'Conforming'
        END AS jumbo_status
    FROM BaseColumns bc
    LEFT JOIN SAM_Reporting.dbo.DIM_ConformingLimits cl
        ON UPPER(LTRIM(RTRIM(bc.subject_property_state))) = cl.state_abbrev
        AND UPPER(LTRIM(RTRIM(
            REPLACE(REPLACE(REPLACE(CAST(bc.property_county AS NVARCHAR(100)), ' COUNTY', ''), ' PARISH', ''), ' CITY', '')
        ))) = cl.county_name
        AND YEAR(COALESCE(bc.lock_date, bc.funding_date, bc.hmda_applicationdate)) = cl.limit_year
)
SELECT
    -- ========================================
    -- All base columns from WithConformingLimits CTE
    -- ========================================
    *,

    -- ========================================
    -- BUSINESS CALCULATED COLUMNS
    -- ========================================

    -- Funded flag: 1 if loan has funding_date, 0 otherwise
    CASE WHEN funding_date IS NOT NULL THEN 1 ELSE 0 END AS funded_flag,

    -- Originated amount: For HECM loans use max claim amount, otherwise use loan amount
    CASE
        WHEN UPPER(loan_program) = 'HECM' THEN hecm_maxclaimamount
        ELSE loanamount
    END AS originated_amount,

    -- Final funded loan amount: For HECM use delivered amount, otherwise use loan amount
    CASE
        WHEN UPPER(loan_program) = 'HECM' THEN hecm_amountdelivered
        ELSE loanamount
    END AS loanamount_fundedfinal,

    -- Projected loan amount: For HECM use max claim amount, otherwise use loan amount
    CASE
        WHEN UPPER(loan_program) = 'HECM' THEN hecm_maxclaimamount
        ELSE loanamount
    END AS loanamount_projected,

    -- Loan channel summary: Categorize as HECM, Brokered, or Retail
    CASE
        WHEN UPPER(loan_program) = 'HECM' THEN 'HECM'
        WHEN UPPER(channel) = 'BROKERED' THEN 'Brokered'
        ELSE 'Retail'
    END AS loanchannel_summary,

    -- ========================================
    -- PULL-THROUGH STAGE FLAGS
    -- Track loan progression through pipeline stages
    -- ========================================

    -- Stage 1: Application submitted
    CASE WHEN hmda_applicationdate IS NOT NULL THEN 1 ELSE 0 END AS stage_application,

    -- Stage 2: Loan Officer qualified
    CASE WHEN loqualification_date IS NOT NULL THEN 1 ELSE 0 END AS stage_qualified,

    -- Stage 3: Rate locked
    CASE WHEN lock_date IS NOT NULL THEN 1 ELSE 0 END AS stage_locked,

    -- Stage 4: Underwriting approved
    CASE WHEN underwritingapproval_date IS NOT NULL THEN 1 ELSE 0 END AS stage_approved,

    -- Stage 5: Funded
    CASE WHEN funding_date IS NOT NULL THEN 1 ELSE 0 END AS stage_funded,

    -- Stage 6: Withdrawn/Cancelled/Denied
    -- Note: If funded, never mark as withdrawn (funding takes precedence)
    CASE
        WHEN funding_date IS NOT NULL THEN 0
        WHEN UPPER(loanstatus_current) IN ('CANCELLED', 'DENIED', 'WITHDRAWN')
             OR denial_date IS NOT NULL
             OR underwritingdenied_date IS NOT NULL THEN 1
        ELSE 0
    END AS stage_withdrawn,

    -- Pipeline stage summary: Highest stage reached (prioritized)
    CASE
        WHEN funding_date IS NOT NULL THEN 'Funded'
        WHEN (UPPER(loanstatus_current) IN ('CANCELLED', 'DENIED', 'WITHDRAWN')
              OR denial_date IS NOT NULL
              OR underwritingdenied_date IS NOT NULL)
             AND funding_date IS NULL THEN 'Withdrawn'
        WHEN underwritingapproval_date IS NOT NULL THEN 'Approved'
        WHEN lock_date IS NOT NULL THEN 'Locked'
        WHEN loqualification_date IS NOT NULL THEN 'Qualified'
        WHEN hmda_applicationdate IS NOT NULL THEN 'Application'
        ELSE 'Lead'
    END AS pipeline_stage,

    -- Loan closed flag: 1 if funded OR withdrawn, 0 if still in process
    CASE
        WHEN funding_date IS NOT NULL THEN 1
        WHEN (UPPER(loanstatus_current) IN ('CANCELLED', 'DENIED', 'WITHDRAWN')
              OR denial_date IS NOT NULL
              OR underwritingdenied_date IS NOT NULL)
             AND funding_date IS NULL THEN 1
        ELSE 0
    END AS loan_closed,

    -- ========================================
    -- BUSINESS DAYS CALCULATIONS
    -- Uses dbo.dim_dates table for weekend/holiday exclusion
    -- ========================================

    -- Business days from HMDA application to funding
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > hmda_applicationdate
          AND date <= funding_date
          AND businessdayflag = 1
    ) AS turntime_hmdaapp_to_fund,

    -- Business days from application to LO qualification
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > hmda_applicationdate
          AND date <= loqualification_date
          AND businessdayflag = 1
    ) AS turntime_app_to_loqual,

    -- Business days from LO qualification to preprocessing
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > loqualification_date
          AND date <= preprocessing_date
          AND businessdayflag = 1
    ) AS turntime_loqual_to_preprocessing,

    -- Business days from preprocessing to processing
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > preprocessing_date
          AND date <= processing_date
          AND businessdayflag = 1
    ) AS turntime_preprocessing_to_processing,

    -- Business days from processing to underwriting
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > processing_date
          AND date <= underwriting_date
          AND businessdayflag = 1
    ) AS turntime_processing_to_uw,

    -- Business days from underwriting to send to closing
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > underwriting_date
          AND date <= sendtoclosing_date
          AND businessdayflag = 1
    ) AS turntime_uw_to_sendtoclosing,

    -- Business days from send to closing to docs to title
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > sendtoclosing_date
          AND date <= docstotitle_date
          AND businessdayflag = 1
    ) AS turntime_sendtoclosing_to_docstotitle,

    -- Business days from docs to title to docs signing
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > docstotitle_date
          AND date <= docssigning_date
          AND businessdayflag = 1
    ) AS turntime_docstotitle_to_docsigning,

    -- Business days from docs signing to funding
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > docssigning_date
          AND date <= funding_date
          AND businessdayflag = 1
    ) AS turntime_docsigning_to_funding,

    -- Business days from funding to shipping
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > funding_date
          AND date <= shipping_date
          AND businessdayflag = 1
    ) AS turntime_funding_to_shipping,

    -- Business days from shipping to purchase
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > shipping_date
          AND date <= purchased_date
          AND businessdayflag = 1
    ) AS turntime_shipping_to_purchase,

    -- Business days from purchase to reconciled
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > purchased_date
          AND date <= reconciled_date
          AND businessdayflag = 1
    ) AS turntime_purchase_to_reconciled,

    -- Business days from reconciled to file completion
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > reconciled_date
          AND date <= filecompletion_date
          AND businessdayflag = 1
    ) AS turntime_reconciled_to_filecompletion,

    -- Business days from lock to funding
    (
        SELECT COUNT(*)
        FROM SAM_Reporting.dbo.DIM_Dates
        WHERE date > lock_date
          AND date <= funding_date
          AND businessdayflag = 1
    ) AS turntime_lock_to_fund

FROM WithConformingLimits;

