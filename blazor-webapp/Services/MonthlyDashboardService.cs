using System.Data;
using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class MonthlyDashboardService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public MonthlyDashboardService(SqlService sql, IMemoryCache cache)
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

    private const string KpisSql = @"
SELECT
    (SELECT ISNULL(COUNT(*),0) FROM dbo.EncompassLoan_Gold
       WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd) AS funded_units,
    (SELECT ISNULL(SUM(loanamount_fundedfinal),0) FROM dbo.EncompassLoan_Gold
       WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd) AS funded_volume,
    (SELECT ISNULL(COUNT(*),0) FROM dbo.EncompassLoan_Gold
       WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
         AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd) AS proj_units,
    (SELECT ISNULL(SUM(COALESCE(loanamount_fundedfinal, loanamount, 0)),0) FROM dbo.EncompassLoan_Gold
       WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
         AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd) AS proj_volume";

    private const string ProjectedByDaySql = @"
SELECT DAY(estimatedclosing_date) AS day_num,
       SUM(CASE WHEN loanchannel_summary = 'Retail'   THEN 1 ELSE 0 END) AS retail,
       SUM(CASE WHEN loanchannel_summary = 'HECM'     THEN 1 ELSE 0 END) AS hecm,
       SUM(CASE WHEN loanchannel_summary = 'Brokered' THEN 1 ELSE 0 END) AS brokered,
       SUM(COALESCE(loanamount_fundedfinal, loanamount, 0)) AS volume
FROM dbo.EncompassLoan_Gold
WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
  AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd
GROUP BY DAY(estimatedclosing_date) ORDER BY day_num";

    private const string MilestoneOrder = @"
CASE COALESCE(milestone_status, 'Unknown')
    WHEN 'Started' THEN 1 WHEN 'Pre-Processing' THEN 2 WHEN 'LO Qualification' THEN 3
    WHEN 'LOA Review' THEN 4 WHEN 'Processing' THEN 5 WHEN 'Underwriting' THEN 6
    WHEN 'TC Review' THEN 7 WHEN 'Send to Closing' THEN 8 WHEN 'Docs to Title' THEN 9
    WHEN 'Docs Signing' THEN 10 WHEN 'Funding' THEN 11 WHEN 'Shipping' THEN 12
    WHEN 'Completion' THEN 13 ELSE 99
END";

    private static readonly string ProjectedByMilestoneSql = $@"
SELECT COALESCE(milestone_status, 'Unknown') AS milestone,
       SUM(CASE WHEN loanchannel_summary = 'Retail'   THEN 1 ELSE 0 END) AS retail,
       SUM(CASE WHEN loanchannel_summary = 'HECM'     THEN 1 ELSE 0 END) AS hecm,
       SUM(CASE WHEN loanchannel_summary = 'Brokered' THEN 1 ELSE 0 END) AS brokered,
       SUM(COALESCE(loanamount_fundedfinal, loanamount, 0)) AS volume,
       {MilestoneOrder} AS sort_order
FROM dbo.EncompassLoan_Gold
WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
  AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd
GROUP BY milestone_status ORDER BY sort_order";

    private static readonly string PipelineAgingSql = $@"
SELECT COALESCE(milestone_status, 'Unknown') AS milestone,
       COUNT(*) AS units,
       SUM(COALESCE(loanamount_fundedfinal, loanamount, 0)) AS volume,
       AVG(CAST(DATEDIFF(DAY, currentmilestone_date, GETDATE()) AS FLOAT)) AS avg_days,
       MIN(DATEDIFF(DAY, currentmilestone_date, GETDATE())) AS min_days,
       MAX(DATEDIFF(DAY, currentmilestone_date, GETDATE())) AS max_days,
       SUM(CASE WHEN DATEDIFF(DAY, currentmilestone_date, GETDATE()) > 30
                 AND milestone_status IN ('Started','Pre-Processing','LO Qualification')
                 THEN 1 ELSE 0 END) AS stale_count,
       CASE WHEN milestone_status IN ('Docs to Title','Docs Signing','Funding') THEN 'High'
            WHEN milestone_status IN ('Processing','Send to Closing','Underwriting','TC Review') THEN 'Medium'
            ELSE 'Low' END AS confidence,
       {MilestoneOrder} AS sort_order
FROM dbo.EncompassLoan_Gold
WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
  AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd
GROUP BY milestone_status ORDER BY sort_order";

    private const string TurnTimesCurrentSql = @"
SELECT
    AVG(CAST(turntime_hmdaapp_to_fund AS FLOAT)) AS avg_app_to_fund,
    AVG(CAST(turntime_lock_to_fund AS FLOAT)) AS avg_lock_to_fund,
    AVG(CAST(turntime_uw_to_sendtoclosing AS FLOAT)) AS avg_uw_to_stc,
    AVG(CAST(turntime_sendtoclosing_to_docstotitle AS FLOAT)) AS avg_stc_to_docs,
    COUNT(*) AS loan_count
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd";

    private const string TurnTimesTrailingSql = @"
SELECT
    AVG(CAST(turntime_hmdaapp_to_fund AS FLOAT)) AS avg_app_to_fund,
    AVG(CAST(turntime_lock_to_fund AS FLOAT)) AS avg_lock_to_fund,
    AVG(CAST(turntime_uw_to_sendtoclosing AS FLOAT)) AS avg_uw_to_stc,
    AVG(CAST(turntime_sendtoclosing_to_docstotitle AS FLOAT)) AS avg_stc_to_docs
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND funding_date >= @trailStart AND funding_date < @mStart";

    private const string TurnTimeWaterfallSql = @"
SELECT
    AVG(CAST(turntime_app_to_loqual AS FLOAT))                AS app_to_loqual,
    AVG(CAST(turntime_loqual_to_preprocessing AS FLOAT))      AS loqual_to_preproc,
    AVG(CAST(turntime_preprocessing_to_processing AS FLOAT))  AS preproc_to_proc,
    AVG(CAST(turntime_processing_to_uw AS FLOAT))             AS proc_to_uw,
    AVG(CAST(turntime_uw_to_sendtoclosing AS FLOAT))          AS uw_to_stc,
    AVG(CAST(turntime_sendtoclosing_to_docstotitle AS FLOAT)) AS stc_to_docs,
    AVG(CAST(turntime_docstotitle_to_docsigning AS FLOAT))    AS docs_to_signing,
    AVG(CAST(turntime_docsigning_to_funding AS FLOAT))        AS signing_to_funding
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd";

    private const string TurnTimeTrendSql = @"
SELECT MONTH(funding_date) AS mo,
       AVG(CAST(turntime_hmdaapp_to_fund AS FLOAT)) AS avg_app_to_fund,
       AVG(CAST(turntime_lock_to_fund AS FLOAT))    AS avg_lock_to_fund
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1 AND funding_date >= @yStart AND funding_date < @yEnd
GROUP BY MONTH(funding_date) ORDER BY mo";

    private const string ChannelSql = @"
SELECT loanchannel_summary AS label, COUNT(*) AS units, SUM(loanamount_fundedfinal) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd
GROUP BY loanchannel_summary ORDER BY volume DESC";

    // loanpurposecategory lives on the DIM_LoanPurpose dimension table; gold has only loan_purpose,
    // so we join (matches what vw_funded_loans does internally).
    private const string PurposeSql = @"
SELECT COALESCE(lp.loanpurposecategory, 'Other') AS label, COUNT(*) AS units,
       SUM(g.loanamount_fundedfinal) AS volume
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanPurpose lp ON g.loan_purpose = lp.loanpurpose
WHERE g.funded_flag = 1 AND g.funding_date >= @mStart AND g.funding_date < @mEnd
GROUP BY lp.loanpurposecategory ORDER BY volume DESC";

    private const string LoSummaryFundedSql = @"
SELECT loanofficer_name AS lo, COUNT(*) AS units, SUM(loanamount_fundedfinal) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1 AND funding_date >= @mStart AND funding_date < @mEnd
GROUP BY loanofficer_name";

    private const string LoSummaryProjSql = @"
SELECT loanofficer_name AS lo, COUNT(*) AS units,
       SUM(COALESCE(loanamount_fundedfinal, loanamount, 0)) AS volume
FROM dbo.EncompassLoan_Gold
WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
  AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd
GROUP BY loanofficer_name";

    private const string FundedDetailSql = @"
SELECT g.loan_number, g.borrower_lastname, g.loanofficer_name, g.loanchannel_summary,
       lp.loanpurposecategory AS purpose, g.loanamount_fundedfinal, g.interest_rate, g.funding_date
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanPurpose lp ON g.loan_purpose = lp.loanpurpose
WHERE g.funded_flag = 1 AND g.funding_date >= @mStart AND g.funding_date < @mEnd
ORDER BY g.funding_date DESC";

    private const string ProjectedDetailSql = @"
SELECT loan_number, borrower_lastname, loanofficer_name, loanchannel_summary,
       loan_purpose AS purpose, milestone_status,
       COALESCE(loanamount_fundedfinal, loanamount, 0) AS loan_amount,
       estimatedclosing_date
FROM dbo.EncompassLoan_Gold
WHERE currentloanfolder = 'My Pipeline' AND funded_flag = 0
  AND estimatedclosing_date >= @mStart AND estimatedclosing_date < @mEnd
ORDER BY estimatedclosing_date";

    // ─── Per-tab fetchers ────────────────────────────────────────────────────

    public Task<MonthlySummary> GetSummaryAsync(int year, int month) =>
        Cached($"monthly:summary:{year}:{month}", () => FetchSummaryAsync(year, month));

    private async Task<MonthlySummary> FetchSummaryAsync(int year, int month)
    {
        var p = MonthParams(year, month);

        var kpiTask = _sql.QueryAsync(KpisSql, p);
        var byDayTask = _sql.QueryAsync(ProjectedByDaySql,
            r => new DailyProjection(
                Day: Convert.ToInt32(r["day_num"]),
                Retail: Convert.ToInt32(r["retail"]),
                HECM: Convert.ToInt32(r["hecm"]),
                Brokered: Convert.ToInt32(r["brokered"]),
                Volume: Convert.ToDecimal(r["volume"] ?? 0m)),
            p);
        var byMsTask = _sql.QueryAsync(ProjectedByMilestoneSql,
            r => new MilestoneBucket(
                Milestone: r["milestone"] as string ?? "Unknown",
                Retail: Convert.ToInt32(r["retail"]),
                HECM: Convert.ToInt32(r["hecm"]),
                Brokered: Convert.ToInt32(r["brokered"]),
                Volume: Convert.ToDecimal(r["volume"] ?? 0m),
                SortOrder: Convert.ToInt32(r["sort_order"])),
            p);
        var channelsTask = _sql.QueryAsync(ChannelSql,
            r => new BreakdownBucket(
                r["label"] as string ?? "Unknown",
                Convert.ToInt32(r["units"]),
                Convert.ToDecimal(r["volume"] ?? 0m)),
            p);
        var purposesTask = _sql.QueryAsync(PurposeSql,
            r => new BreakdownBucket(
                r["label"] as string ?? "Other",
                Convert.ToInt32(r["units"]),
                Convert.ToDecimal(r["volume"] ?? 0m)),
            p);
        var fundedByLoTask = _sql.QueryAsync(LoSummaryFundedSql,
            r => new BreakdownBucket(
                r["lo"] as string is { Length: > 0 } name ? name : "(Unassigned)",
                Convert.ToInt32(r["units"]),
                Convert.ToDecimal(r["volume"] ?? 0m)),
            p);

        await Task.WhenAll(kpiTask, byDayTask, byMsTask, channelsTask, purposesTask, fundedByLoTask);

        var k = kpiTask.Result.Single();
        var kpis = new MonthlyKpis(
            FundedUnits: Convert.ToInt32(k["funded_units"] ?? 0),
            FundedVolume: Convert.ToDecimal(k["funded_volume"] ?? 0m),
            ProjectedUnits: Convert.ToInt32(k["proj_units"] ?? 0),
            ProjectedVolume: Convert.ToDecimal(k["proj_volume"] ?? 0m));

        var fundedByLo = fundedByLoTask.Result
            .OrderByDescending(b => b.Units)
            .ThenByDescending(b => b.Volume)
            .ToList();

        return new MonthlySummary(
            kpis, byDayTask.Result, byMsTask.Result,
            new ChannelPurposeBreakdown(channelsTask.Result, purposesTask.Result),
            fundedByLo);
    }

    public Task<PipelineHealth> GetPipelineAsync(int year, int month) =>
        Cached($"monthly:pipeline:{year}:{month}", () => FetchPipelineAsync(year, month));

    private async Task<PipelineHealth> FetchPipelineAsync(int year, int month)
    {
        var rows = await _sql.QueryAsync(PipelineAgingSql,
            r => new PipelineAgingRow(
                Milestone: r["milestone"] as string ?? "Unknown",
                Confidence: r["confidence"] as string ?? "Low",
                Units: Convert.ToInt32(r["units"]),
                Volume: Convert.ToDecimal(r["volume"] ?? 0m),
                AvgDays: r["avg_days"] is DBNull ? 0d : Math.Round(Convert.ToDouble(r["avg_days"]), 1),
                MinDays: r["min_days"] is DBNull ? 0 : Convert.ToInt32(r["min_days"]),
                MaxDays: r["max_days"] is DBNull ? 0 : Convert.ToInt32(r["max_days"]),
                StaleCount: r["stale_count"] is DBNull ? 0 : Convert.ToInt32(r["stale_count"])),
            MonthParams(year, month));

        return new PipelineHealth(
            Milestones: rows,
            Tiers: new ConfidenceTotals(
                HighUnits: rows.Where(x => x.Confidence == "High").Sum(x => x.Units),
                HighVolume: rows.Where(x => x.Confidence == "High").Sum(x => x.Volume),
                MediumUnits: rows.Where(x => x.Confidence == "Medium").Sum(x => x.Units),
                MediumVolume: rows.Where(x => x.Confidence == "Medium").Sum(x => x.Volume),
                LowUnits: rows.Where(x => x.Confidence == "Low").Sum(x => x.Units),
                LowVolume: rows.Where(x => x.Confidence == "Low").Sum(x => x.Volume)),
            StaleCount: rows.Sum(x => x.StaleCount));
    }

    public Task<TurnTimeBundle> GetTurnTimesAsync(int year, int month) =>
        Cached($"monthly:turntimes:{year}:{month}", () => FetchTurnTimesAsync(year, month));

    private async Task<TurnTimeBundle> FetchTurnTimesAsync(int year, int month)
    {
        var p = MonthParams(year, month);

        var curTask = _sql.QueryAsync(TurnTimesCurrentSql, p);
        var trailTask = _sql.QueryAsync(TurnTimesTrailingSql, p);
        var wfTask = _sql.QueryAsync(TurnTimeWaterfallSql, p);
        var trendTask = _sql.QueryAsync(TurnTimeTrendSql,
            r => new TurnTimeTrendPoint(
                MonthNames[Convert.ToInt32(r["mo"]) - 1],
                ToDouble(r["avg_app_to_fund"]),
                ToDouble(r["avg_lock_to_fund"])),
            p);

        await Task.WhenAll(curTask, trailTask, wfTask, trendTask);

        var cur = curTask.Result.Single();
        var trail = trailTask.Result.Single();
        var times = new TurnTimes(
            AppToFund: ToDouble(cur["avg_app_to_fund"]),
            LockToFund: ToDouble(cur["avg_lock_to_fund"]),
            UwToStc: ToDouble(cur["avg_uw_to_stc"]),
            StcToDocs: ToDouble(cur["avg_stc_to_docs"]),
            LoanCount: cur["loan_count"] is DBNull ? 0 : Convert.ToInt32(cur["loan_count"]),
            AppToFund3Mo: ToDouble(trail["avg_app_to_fund"]),
            LockToFund3Mo: ToDouble(trail["avg_lock_to_fund"]),
            UwToStc3Mo: ToDouble(trail["avg_uw_to_stc"]),
            StcToDocs3Mo: ToDouble(trail["avg_stc_to_docs"]));

        var wfRow = wfTask.Result.Single();
        var waterfall = new (string Label, object? Val)[]
        {
            ("App \u2192 LO Qual",          wfRow["app_to_loqual"]),
            ("LO Qual \u2192 Pre-Proc",     wfRow["loqual_to_preproc"]),
            ("Pre-Proc \u2192 Processing",  wfRow["preproc_to_proc"]),
            ("Processing \u2192 UW",        wfRow["proc_to_uw"]),
            ("UW \u2192 Send to Closing",   wfRow["uw_to_stc"]),
            ("STC \u2192 Docs to Title",    wfRow["stc_to_docs"]),
            ("Docs \u2192 Signing",         wfRow["docs_to_signing"]),
            ("Signing \u2192 Funding",      wfRow["signing_to_funding"]),
        }
        .Where(x => x.Val is not null and not DBNull)
        .Select(x => new TurnTimeSegment(x.Label, Math.Round(Convert.ToDouble(x.Val), 1)))
        .ToList();

        return new TurnTimeBundle(times, waterfall, trendTask.Result);
    }

    public Task<IReadOnlyList<LoanOfficerRow>> GetLoanOfficersAsync(int year, int month) =>
        Cached($"monthly:los:{year}:{month}", () => FetchLoanOfficersAsync(year, month));

    private async Task<IReadOnlyList<LoanOfficerRow>> FetchLoanOfficersAsync(int year, int month)
    {
        var p = MonthParams(year, month);
        var fundedTask = _sql.QueryAsync(LoSummaryFundedSql, p);
        var projTask = _sql.QueryAsync(LoSummaryProjSql, p);
        await Task.WhenAll(fundedTask, projTask);

        var loMap = new Dictionary<string, (int fu, decimal fv, int pu, decimal pv)>();
        foreach (var r in fundedTask.Result)
        {
            var name = r["lo"] as string ?? "(Unassigned)";
            var row = loMap.GetValueOrDefault(name);
            loMap[name] = (Convert.ToInt32(r["units"]), Convert.ToDecimal(r["volume"] ?? 0m), row.pu, row.pv);
        }
        foreach (var r in projTask.Result)
        {
            var name = r["lo"] as string ?? "(Unassigned)";
            var row = loMap.GetValueOrDefault(name);
            loMap[name] = (row.fu, row.fv, Convert.ToInt32(r["units"]), Convert.ToDecimal(r["volume"] ?? 0m));
        }
        return loMap
            .Select(kv => new LoanOfficerRow(kv.Key, kv.Value.fu, kv.Value.fv, kv.Value.pu, kv.Value.pv))
            .OrderByDescending(x => x.TotalVolume)
            .ToList();
    }

    public Task<LoanDetailBundle> GetDetailAsync(int year, int month) =>
        Cached($"monthly:detail:{year}:{month}", () => FetchDetailAsync(year, month));

    private async Task<LoanDetailBundle> FetchDetailAsync(int year, int month)
    {
        var p = MonthParams(year, month);
        var fundedTask = _sql.QueryAsync(FundedDetailSql,
            r => new FundedLoanDetail(
                Convert.ToInt64(r["loan_number"]),
                r["borrower_lastname"] as string,
                r["loanofficer_name"] as string,
                r["loanchannel_summary"] as string,
                r["purpose"] as string,
                r["loanamount_fundedfinal"] as decimal?,
                r["interest_rate"] as decimal?,
                r["funding_date"] as DateTime?),
            p);
        var projTask = _sql.QueryAsync(ProjectedDetailSql,
            r => new ProjectedLoanDetail(
                Convert.ToInt64(r["loan_number"]),
                r["borrower_lastname"] as string,
                r["loanofficer_name"] as string,
                r["loanchannel_summary"] as string,
                r["purpose"] as string,
                r["milestone_status"] as string,
                r["loan_amount"] as decimal?,
                r["estimatedclosing_date"] as DateTime?),
            p);
        await Task.WhenAll(fundedTask, projTask);
        return new LoanDetailBundle(fundedTask.Result, projTask.Result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (string, object?)[] MonthParams(int year, int month)
    {
        var mStart = new DateTime(year, month, 1);
        return new (string, object?)[]
        {
            ("@mStart", mStart),
            ("@mEnd", mStart.AddMonths(1)),
            ("@trailStart", mStart.AddMonths(-3)),
            ("@yStart", new DateTime(year, 1, 1)),
            ("@yEnd", new DateTime(year + 1, 1, 1)),
        };
    }

    private static double? ToDouble(object? o) =>
        o is null or DBNull ? null : Math.Round(Convert.ToDouble(o), 1);

    private static readonly string[] MonthNames =
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };
}
