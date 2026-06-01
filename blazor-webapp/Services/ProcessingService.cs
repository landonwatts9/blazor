using System.Data;
using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class ProcessingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public ProcessingService(SqlService sql, IMemoryCache cache)
    {
        _sql = sql;
        _cache = cache;
    }

    private Task<T> Cached<T>(string key, Func<Task<T>> factory) =>
        _cache.GetOrCreateAsync(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return factory();
        })!;

    // ─── SQL ─────────────────────────────────────────────────────────────────

    // Tab 1: Underwriting Turn Time
    private const string UwSql = @"
SELECT
    g.loan_number,
    LTRIM(RTRIM(CONCAT(g.borrower_firstname, ' ', g.borrower_lastname))) AS borrower_name,
    g.uw_currentstatus,
    g.current_underwritingstatus_date,
    g.processor_name,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    g.expected_signing_date,
    g.estimatedclosing_date
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE g.currentloanfolder = 'My Pipeline'
  AND g.uw_currentstatus IN ('Submitted for Final Approval','In Review','To Be Assigned','Assigned')
ORDER BY g.expected_signing_date, g.loan_number";

    // Tab 2: CD Turn Time
    private const string CdSql = @"
SELECT
    g.loan_number,
    LTRIM(RTRIM(CONCAT(g.borrower_firstname, ' ', g.borrower_lastname))) AS borrower_name,
    g.cdstatus,
    g.early_cd_audit_date,
    g.cd_audit_date,
    g.processor_name,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    g.expected_signing_date,
    g.estimatedclosing_date
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE g.currentloanfolder = 'My Pipeline'
  AND g.cdstatus IN ('Needed','Assigned')
ORDER BY g.expected_signing_date, g.loan_number";

    // Tab 3: Closing Docs Turn Time
    private const string ClosingDocsSql = @"
SELECT
    g.loan_number,
    LTRIM(RTRIM(CONCAT(g.borrower_firstname, ' ', g.borrower_lastname))) AS borrower_name,
    g.milestone_status AS last_milestone,
    g.currentmilestone_date AS last_milestone_date,
    g.processor_name,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    g.expected_signing_date,
    g.estimatedclosing_date
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE g.currentloanfolder = 'My Pipeline'
  AND g.milestone_status = 'Send to Closing'
ORDER BY g.expected_signing_date, g.loan_number";

    // Tab 4: Funding Turn Time (filter by RegZ disbursement date == today)
    private const string FundingSql = @"
SELECT
    g.loan_number,
    LTRIM(RTRIM(CONCAT(g.borrower_firstname, ' ', g.borrower_lastname))) AS borrower_name,
    g.processor_name,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    g.expected_signing_date,
    g.estimatedclosing_date
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE CAST(g.closingdocs_regzdisbursement_date AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY lo, g.loan_number";

    // KPI counts for the header row — one round-trip, same filters as each tab's SQL.
    private const string CountsSql = @"
SELECT
    (SELECT COUNT(*) FROM dbo.EncompassLoan_Gold
       WHERE currentloanfolder='My Pipeline'
         AND uw_currentstatus IN ('Submitted for Final Approval','In Review','To Be Assigned','Assigned')) AS uw_count,
    (SELECT COUNT(*) FROM dbo.EncompassLoan_Gold
       WHERE currentloanfolder='My Pipeline'
         AND cdstatus IN ('Needed','Assigned')) AS cd_count,
    (SELECT COUNT(*) FROM dbo.EncompassLoan_Gold
       WHERE currentloanfolder='My Pipeline'
         AND milestone_status='Send to Closing') AS docs_count,
    (SELECT COUNT(*) FROM dbo.EncompassLoan_Gold
       WHERE CAST(closingdocs_regzdisbursement_date AS DATE) = CAST(GETDATE() AS DATE)) AS funding_count";

    // ─── Fetchers ────────────────────────────────────────────────────────────

    public Task<ProcessingCounts> GetCountsAsync() =>
        Cached($"processing:counts:{DateTime.Today:yyyyMMdd}", FetchCountsAsync);

    private async Task<ProcessingCounts> FetchCountsAsync()
    {
        var rows = await _sql.QueryAsync(CountsSql);
        var r = rows.Single();
        return new ProcessingCounts(
            UwCount: Convert.ToInt32(r["uw_count"]),
            CdCount: Convert.ToInt32(r["cd_count"]),
            ClosingDocsCount: Convert.ToInt32(r["docs_count"]),
            FundingCount: Convert.ToInt32(r["funding_count"]));
    }

    public Task<IReadOnlyList<UwTurnTimeRow>> GetUwAsync() =>
        Cached("processing:uw", FetchUwAsync);

    private async Task<IReadOnlyList<UwTurnTimeRow>> FetchUwAsync() =>
        (await _sql.QueryAsync(UwSql, r => new UwTurnTimeRow(
            LoanNumber: Convert.ToInt64(r["loan_number"]),
            BorrowerName: r["borrower_name"] as string ?? string.Empty,
            UwStatus: r["uw_currentstatus"] as string,
            UwStatusDate: r["current_underwritingstatus_date"] as DateTime?,
            ProcessorName: r["processor_name"] as string,
            LoanOfficerName: r["lo"] as string,
            ExpectedSigningDate: r["expected_signing_date"] as DateTime?,
            EstClosingDate: r["estimatedclosing_date"] as DateTime?)));

    public Task<IReadOnlyList<CdTurnTimeRow>> GetCdAsync() =>
        Cached("processing:cd", FetchCdAsync);

    private async Task<IReadOnlyList<CdTurnTimeRow>> FetchCdAsync() =>
        (await _sql.QueryAsync(CdSql, r => new CdTurnTimeRow(
            LoanNumber: Convert.ToInt64(r["loan_number"]),
            BorrowerName: r["borrower_name"] as string ?? string.Empty,
            CdStatus: r["cdstatus"] as string,
            EarlyCdAuditDate: r["early_cd_audit_date"] as DateTime?,
            CdAuditDate: r["cd_audit_date"] as DateTime?,
            ProcessorName: r["processor_name"] as string,
            LoanOfficerName: r["lo"] as string,
            ExpectedSigningDate: r["expected_signing_date"] as DateTime?,
            EstClosingDate: r["estimatedclosing_date"] as DateTime?)));

    public Task<IReadOnlyList<ClosingDocsTurnTimeRow>> GetClosingDocsAsync() =>
        Cached("processing:closingdocs", FetchClosingDocsAsync);

    private async Task<IReadOnlyList<ClosingDocsTurnTimeRow>> FetchClosingDocsAsync() =>
        (await _sql.QueryAsync(ClosingDocsSql, r => new ClosingDocsTurnTimeRow(
            LoanNumber: Convert.ToInt64(r["loan_number"]),
            BorrowerName: r["borrower_name"] as string ?? string.Empty,
            LastMilestone: r["last_milestone"] as string,
            LastMilestoneDate: r["last_milestone_date"] as DateTime?,
            ProcessorName: r["processor_name"] as string,
            LoanOfficerName: r["lo"] as string,
            ExpectedSigningDate: r["expected_signing_date"] as DateTime?,
            EstClosingDate: r["estimatedclosing_date"] as DateTime?)));

    public Task<IReadOnlyList<FundingTurnTimeRow>> GetFundingAsync() =>
        Cached($"processing:funding:{DateTime.Today:yyyyMMdd}", FetchFundingAsync);

    private async Task<IReadOnlyList<FundingTurnTimeRow>> FetchFundingAsync() =>
        (await _sql.QueryAsync(FundingSql, r => new FundingTurnTimeRow(
            LoanNumber: Convert.ToInt64(r["loan_number"]),
            BorrowerName: r["borrower_name"] as string ?? string.Empty,
            ProcessorName: r["processor_name"] as string,
            LoanOfficerName: r["lo"] as string,
            ExpectedSigningDate: r["expected_signing_date"] as DateTime?,
            EstClosingDate: r["estimatedclosing_date"] as DateTime?)));
}
