using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class PricingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public PricingService(SqlService sql, IMemoryCache cache)
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

    // ─── LO dropdown ─────────────────────────────────────────────────────────

    private const string LoOptionsSql = @"
SELECT DISTINCT
    g.loanofficer_nmlsid AS nmls_id,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo_name
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE g.lock_date >= DATEADD(MONTH, -24, CAST(GETDATE() AS DATE))
  AND g.loanofficer_nmlsid IS NOT NULL
ORDER BY lo_name";

    public Task<IReadOnlyList<LoOption>> GetLoOptionsAsync() =>
        _cache.GetOrCreateAsync("pricing:lo-options", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            var rows = await _sql.QueryAsync(LoOptionsSql,
                r => new LoOption(
                    NmlsId: Convert.ToInt32(r.GetValue(0)),
                    DisplayName: r.GetValue(1) as string ?? "(Unassigned)"));
            return (IReadOnlyList<LoOption>)rows;
        })!;

    // ─── Shared WHERE fragment ───────────────────────────────────────────────

    private const string WhereCore = @"
WHERE g.lock_date >= @start AND g.lock_date < @end
  AND (@captureFilter IS NULL OR g.companylead = @captureFilter)
  AND (@channel IS NULL OR g.loanchannel_summary = @channel)
  AND (@noLoFilter = 1 OR g.loanofficer_nmlsid IN (SELECT TRY_CAST(value AS BIGINT) FROM STRING_SPLIT(@nmlsCsv, ',')))";

    private static (string, object?)[] BuildParams(PricingFilter f, DateTime? startOverride = null, DateTime? endOverride = null)
    {
        var start = startOverride ?? f.Start;
        var end = endOverride ?? f.End;
        var noLo = f.NmlsIds is null || f.NmlsIds.Count == 0 ? 1 : 0;
        var csv = noLo == 1 ? "" : string.Join(",", f.NmlsIds!);
        return new (string, object?)[]
        {
            ("@start", start),
            ("@end", end),
            ("@captureFilter", string.IsNullOrEmpty(f.CaptureFilter) ? null : f.CaptureFilter),
            ("@channel", string.IsNullOrEmpty(f.Channel) ? null : f.Channel),
            ("@noLoFilter", noLo),
            ("@nmlsCsv", csv),
        };
    }

    // ─── KPIs ────────────────────────────────────────────────────────────────

    // Net buy = 2218 (total buy price) − 3371 + NEWHUD.X1150. NULL on 3371/x1150
    // is treated as 0 (no adjustment / no credit); NULL on the base 2218 stays
    // NULL so unlocked-but-still-in-window loans don't poison aggregates.
    private const string NetBuyExpr =
        "(g.buyside_totalbuyprice - ISNULL(g.field_3371, 0) + ISNULL(g.newhud_x1150, 0))";

    private static readonly string KpisSql = $@"
SELECT
    COUNT(*) AS locks,
    ISNULL(SUM(g.loanamount), 0) AS volume,
    AVG(CAST((g.sellside_totalsellprice - {NetBuyExpr}) * 100 AS FLOAT)) AS avg_margin_bps,
    AVG(CAST(g.interest_rate AS FLOAT)) AS avg_rate,
    AVG(CAST(ISNULL(g.cure_amount, 0) + ISNULL(g.lender_credits, 0) AS DECIMAL(19, 2))) AS avg_concessions,
    100.0 * SUM(CASE WHEN g.companylead = 'Yes' THEN 1 ELSE 0 END) /
        NULLIF(SUM(CASE WHEN g.companylead IN ('Yes','No') THEN 1 ELSE 0 END), 0) AS capture_rate_pct
FROM dbo.EncompassLoan_Gold g
{WhereCore}";

    public Task<PricingKpis> GetKpisAsync(PricingFilter f) =>
        Cached($"pricing:kpis:{f.Key()}", () => FetchKpisAsync(f));

    private async Task<PricingKpis> FetchKpisAsync(PricingFilter f)
    {
        var rows = await _sql.QueryAsync(KpisSql, BuildParams(f));
        var r = rows.Single();
        return new PricingKpis(
            Locks: ToInt(r["locks"]),
            Volume: ToDec(r["volume"]),
            AvgMarginBps: ToDouble(r["avg_margin_bps"]) ?? 0,
            AvgRate: ToDouble(r["avg_rate"]) ?? 0,
            AvgConcessions: ToDec(r["avg_concessions"]),
            CaptureRatePct: ToDouble(r["capture_rate_pct"]) ?? 0);
    }

    // ─── Loan detail ─────────────────────────────────────────────────────────

    private static readonly string LoanDetailSql = $@"
SELECT
    g.loan_number,
    LTRIM(RTRIM(CONCAT(g.borrower_firstname, ' ', g.borrower_lastname))) AS borrower_name,
    g.lock_date,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo_name,
    g.loanchannel_summary AS channel,
    g.sellside_investorlocktype AS commitment_type,
    g.wholeloan_flag,
    g.companylead AS captured,
    g.interest_rate,
    {NetBuyExpr} AS buy_price,
    g.sellside_totalsellprice AS sell_price,
    CAST((g.sellside_totalsellprice - {NetBuyExpr}) * 100 AS FLOAT) AS margin_bps,
    (ISNULL(g.cure_amount, 0) + ISNULL(g.lender_credits, 0)) AS concessions,
    g.loanamount
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
{WhereCore}
ORDER BY g.lock_date DESC, g.loan_number";

    public Task<IReadOnlyList<PricingLoanRow>> GetLoanDetailAsync(PricingFilter f) =>
        Cached($"pricing:loans:{f.Key()}", () => FetchLoanDetailAsync(f));

    private async Task<IReadOnlyList<PricingLoanRow>> FetchLoanDetailAsync(PricingFilter f) =>
        await _sql.QueryAsync(LoanDetailSql,
            r => new PricingLoanRow(
                LoanNumber: Convert.ToInt64(r["loan_number"]),
                BorrowerName: r["borrower_name"] as string ?? string.Empty,
                LockDate: r["lock_date"] as DateTime?,
                LoName: r["lo_name"] as string,
                Channel: r["channel"] as string,
                CommitmentType: r["commitment_type"] as string,
                WholeLoan: r["wholeloan_flag"] is int wl && wl == 1
                           || r["wholeloan_flag"] is bool b && b
                           || (r["wholeloan_flag"]?.ToString() == "1"),
                Captured: r["captured"] as string,
                InterestRate: r["interest_rate"] as decimal?,
                BuyPrice: r["buy_price"] as decimal?,
                SellPrice: r["sell_price"] as decimal?,
                MarginBps: ToDouble(r["margin_bps"]),
                Concessions: r["concessions"] as decimal?,
                LoanAmount: r["loanamount"] as decimal?),
            BuildParams(f));

    // ─── LO summary ──────────────────────────────────────────────────────────

    private static readonly string LoSummarySql = $@"
SELECT
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    COUNT(*) AS units,
    ISNULL(SUM(g.loanamount), 0) AS volume,
    AVG(CAST(g.interest_rate AS FLOAT)) AS avg_rate,
    AVG(CAST((g.sellside_totalsellprice - {NetBuyExpr}) * 100 AS FLOAT)) AS avg_margin_bps,
    AVG(CAST(ISNULL(g.cure_amount, 0) + ISNULL(g.lender_credits, 0) AS DECIMAL(19, 2))) AS avg_concessions,
    100.0 * SUM(CASE WHEN g.companylead = 'Yes' THEN 1 ELSE 0 END) /
        NULLIF(SUM(CASE WHEN g.companylead IN ('Yes','No') THEN 1 ELSE 0 END), 0) AS capture_rate_pct,
    SUM(CASE WHEN g.companylead = 'Yes' THEN 1 ELSE 0 END) AS captured_units,
    SUM(CASE WHEN g.companylead = 'No' THEN 1 ELSE 0 END) AS noncaptured_units
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
{WhereCore}
GROUP BY COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)')
ORDER BY avg_margin_bps DESC";

    public Task<IReadOnlyList<LoPricingSummary>> GetLoSummaryAsync(PricingFilter f) =>
        Cached($"pricing:lo-summary:{f.Key()}", () => FetchLoSummaryAsync(f));

    private async Task<IReadOnlyList<LoPricingSummary>> FetchLoSummaryAsync(PricingFilter f) =>
        await _sql.QueryAsync(LoSummarySql,
            r => new LoPricingSummary(
                LoName: r["lo"] as string ?? "(Unassigned)",
                Units: ToInt(r["units"]),
                Volume: ToDec(r["volume"]),
                AvgRate: ToDouble(r["avg_rate"]),
                AvgMarginBps: ToDouble(r["avg_margin_bps"]),
                AvgConcessions: ToDec(r["avg_concessions"]),
                CaptureRatePct: ToDouble(r["capture_rate_pct"]) ?? 0,
                CapturedUnits: ToInt(r["captured_units"]),
                NonCapturedUnits: ToInt(r["noncaptured_units"])),
            BuildParams(f));

    // ─── Trend (12-month rolling) ────────────────────────────────────────────

    private static readonly string TrendSql = $@"
SELECT
    DATEFROMPARTS(YEAR(g.lock_date), MONTH(g.lock_date), 1) AS month_start,
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    COUNT(*) AS units,
    ISNULL(SUM(g.loanamount), 0) AS volume,
    AVG(CAST((g.sellside_totalsellprice - {NetBuyExpr}) * 100 AS FLOAT)) AS avg_margin_bps,
    AVG(CAST(g.interest_rate AS FLOAT)) AS avg_rate
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
{WhereCore}
GROUP BY DATEFROMPARTS(YEAR(g.lock_date), MONTH(g.lock_date), 1),
         COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)')
ORDER BY month_start, lo";

    public Task<IReadOnlyList<PricingTrendPoint>> GetTrendAsync(PricingFilter f, int monthsBack = 12) =>
        Cached($"pricing:trend:{f.Key()}:{monthsBack}",
            () => FetchTrendAsync(f, monthsBack));

    private async Task<IReadOnlyList<PricingTrendPoint>> FetchTrendAsync(PricingFilter f, int monthsBack)
    {
        // Trend window always = trailing N months ending at f.End
        var trendStart = new DateTime(f.End.Year, f.End.Month, 1).AddMonths(-(monthsBack - 1));
        var rows = await _sql.QueryAsync(TrendSql, BuildParams(f, startOverride: trendStart, endOverride: f.End));
        return rows.Select(r => new PricingTrendPoint(
            Month: ((DateTime)r["month_start"]!).ToString("yyyy-MM"),
            LoName: r["lo"] as string ?? "(Unassigned)",
            Units: ToInt(r["units"]),
            Volume: ToDec(r["volume"]),
            AvgMarginBps: ToDouble(r["avg_margin_bps"]),
            AvgRate: ToDouble(r["avg_rate"]))).ToList();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static int ToInt(object? o) => o is null or DBNull ? 0 : Convert.ToInt32(o);
    private static decimal ToDec(object? o) => o is null or DBNull ? 0m : Convert.ToDecimal(o);
    private static double? ToDouble(object? o) => o is null or DBNull ? null : Convert.ToDouble(o);
}
