using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class OriginationsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public OriginationsService(SqlService sql, IMemoryCache cache)
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

    // Origination side: CW/MTD/YTD by LO from vw_originations.
    private const string LandingOriginationSql = @"
SELECT
    COALESCE(lo.lo_name, o.team_name, '(Unassigned)') AS lo,
    SUM(CASE WHEN o.hmda_applicationdate >= @cwStart AND o.hmda_applicationdate < @cwEnd THEN 1 ELSE 0 END) AS cw_units,
    SUM(CASE WHEN o.hmda_applicationdate >= @cwStart AND o.hmda_applicationdate < @cwEnd THEN o.originated_amount ELSE 0 END) AS cw_dollars,
    SUM(CASE WHEN o.hmda_applicationdate >= @mStart  AND o.hmda_applicationdate < @mEnd  THEN 1 ELSE 0 END) AS mtd_units,
    SUM(CASE WHEN o.hmda_applicationdate >= @mStart  AND o.hmda_applicationdate < @mEnd  THEN o.originated_amount ELSE 0 END) AS mtd_dollars,
    SUM(CASE WHEN o.hmda_applicationdate >= @yStart  AND o.hmda_applicationdate < @yEnd  THEN 1 ELSE 0 END) AS ytd_units,
    SUM(CASE WHEN o.hmda_applicationdate >= @yStart  AND o.hmda_applicationdate < @yEnd  THEN o.originated_amount ELSE 0 END) AS ytd_dollars
FROM dbo.vw_originations o
LEFT JOIN dbo.DIM_LoanOfficer lo ON o.loanofficer_nmlsid = lo.nmls_id
WHERE o.hmda_applicationdate >= @yStart AND o.hmda_applicationdate < @yEnd
GROUP BY COALESCE(lo.lo_name, o.team_name, '(Unassigned)')";

    // Funded side: MTD/YTD funded by LO from gold.
    private const string LandingFundedSql = @"
SELECT
    COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)') AS lo,
    SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN 1 ELSE 0 END) AS mtd_funded_units,
    SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN g.loanamount_fundedfinal ELSE 0 END) AS mtd_funded_dollars,
    SUM(CASE WHEN g.funding_date >= @yStart AND g.funding_date < @yEnd THEN 1 ELSE 0 END) AS ytd_funded_units,
    SUM(CASE WHEN g.funding_date >= @yStart AND g.funding_date < @yEnd THEN g.loanamount_fundedfinal ELSE 0 END) AS ytd_funded_dollars
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanOfficer lo ON g.loanofficer_nmlsid = lo.nmls_id
WHERE g.funded_flag = 1 AND g.funding_date >= @yStart AND g.funding_date < @yEnd
GROUP BY COALESCE(lo.lo_name, g.loanofficer_name, '(Unassigned)')";

    // Channel + Purpose mix for selected month: origination + funded counts side-by-side.
    private const string MixSql = @"
SELECT 'channel' AS dim, ISNULL(o.loanchannel_summary,'Unknown') AS label,
       SUM(CASE WHEN o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd THEN 1 ELSE 0 END) AS orig_units,
       SUM(CASE WHEN o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd THEN o.originated_amount ELSE 0 END) AS orig_dollars,
       0 AS funded_units, CAST(0 AS DECIMAL(19,2)) AS funded_dollars
FROM dbo.vw_originations o
WHERE o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd
GROUP BY ISNULL(o.loanchannel_summary,'Unknown')
UNION ALL
SELECT 'channel', ISNULL(g.loanchannel_summary,'Unknown'),
       0, CAST(0 AS DECIMAL(19,2)),
       SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN 1 ELSE 0 END),
       SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN g.loanamount_fundedfinal ELSE 0 END)
FROM dbo.EncompassLoan_Gold g
WHERE g.funded_flag = 1 AND g.funding_date >= @mStart AND g.funding_date < @mEnd
GROUP BY ISNULL(g.loanchannel_summary,'Unknown')
UNION ALL
SELECT 'purpose', ISNULL(o.loanpurposecategory,'Other'),
       SUM(CASE WHEN o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd THEN 1 ELSE 0 END),
       SUM(CASE WHEN o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd THEN o.originated_amount ELSE 0 END),
       0, CAST(0 AS DECIMAL(19,2))
FROM dbo.vw_originations o
WHERE o.hmda_applicationdate >= @mStart AND o.hmda_applicationdate < @mEnd
GROUP BY ISNULL(o.loanpurposecategory,'Other')
UNION ALL
SELECT 'purpose', ISNULL(lp.loanpurposecategory,'Other'),
       0, CAST(0 AS DECIMAL(19,2)),
       SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN 1 ELSE 0 END),
       SUM(CASE WHEN g.funding_date >= @mStart AND g.funding_date < @mEnd THEN g.loanamount_fundedfinal ELSE 0 END)
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_LoanPurpose lp ON g.loan_purpose = lp.loanpurpose
WHERE g.funded_flag = 1 AND g.funding_date >= @mStart AND g.funding_date < @mEnd
GROUP BY ISNULL(lp.loanpurposecategory,'Other')";

    // Funnel: cohort = loans whose hmda_applicationdate is in the selected month.
    // Replicates vw_originations folder filter inline. Each stage is a count of
    // the cohort whose corresponding stage date is non-null. @channel is optional
    // (NULL = all channels) so a single query handles both All and per-channel views.
    private const string FunnelSql = @"
WITH cohort AS (
    SELECT loan_number, lock_date, underwritingsubmission_date, underwritingapproval_date, funding_date
    FROM dbo.EncompassLoan_Gold
    WHERE hmda_applicationdate >= @mStart AND hmda_applicationdate < @mEnd
      AND loan_number IS NOT NULL AND loanofficer_nmlsid IS NOT NULL
      AND LOWER(LTRIM(RTRIM(COALESCE(currentloanfolder, '')))) NOT IN
          ('prospects','test','ob_test','ob test','adverse','adverse prospects')
      AND (@channel IS NULL OR loanchannel_summary = @channel)
)
SELECT
    (SELECT COUNT(*) FROM cohort)                                            AS apps,
    (SELECT COUNT(*) FROM cohort WHERE lock_date IS NOT NULL)                AS locked,
    (SELECT COUNT(*) FROM cohort WHERE underwritingsubmission_date IS NOT NULL) AS submitted,
    (SELECT COUNT(*) FROM cohort WHERE underwritingapproval_date IS NOT NULL)   AS approved,
    (SELECT COUNT(*) FROM cohort WHERE funding_date IS NOT NULL)             AS funded";

    // Leaderboard: top 25 LOs by YTD originations, with prior-year YTD same range for ±.
    private const string LeaderboardSql = @"
WITH cur AS (
    SELECT COALESCE(lo.lo_name, o.team_name, '(Unassigned)') AS lo,
           COUNT(*) AS units,
           SUM(o.originated_amount) AS volume
    FROM dbo.vw_originations o
    LEFT JOIN dbo.DIM_LoanOfficer lo ON o.loanofficer_nmlsid = lo.nmls_id
    WHERE o.hmda_applicationdate >= @yStart AND o.hmda_applicationdate < @yEndForLeader
    GROUP BY COALESCE(lo.lo_name, o.team_name, '(Unassigned)')
), prior AS (
    SELECT COALESCE(lo.lo_name, o.team_name, '(Unassigned)') AS lo,
           COUNT(*) AS units,
           SUM(o.originated_amount) AS volume
    FROM dbo.vw_originations o
    LEFT JOIN dbo.DIM_LoanOfficer lo ON o.loanofficer_nmlsid = lo.nmls_id
    WHERE o.hmda_applicationdate >= @priorYStart AND o.hmda_applicationdate < @priorYEndForLeader
    GROUP BY COALESCE(lo.lo_name, o.team_name, '(Unassigned)')
)
SELECT TOP 25
    c.lo,
    c.units,
    c.volume,
    ISNULL(p.units, 0) AS prior_units,
    ISNULL(p.volume, 0) AS prior_volume
FROM cur c
LEFT JOIN prior p ON p.lo = c.lo
ORDER BY c.units DESC, c.volume DESC";

    // Trend: 12 months ending in the selected month. Two metrics per month:
    // origination units (apps with hmda_applicationdate in that month) +
    // funded units (funded loans with funding_date in that month).
    private const string TrendSql = @"
;WITH months AS (
    SELECT DATEADD(MONTH, n, @trendStart) AS m
    FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11)) AS x(n)
), orig AS (
    SELECT DATEFROMPARTS(YEAR(hmda_applicationdate), MONTH(hmda_applicationdate), 1) AS m,
           COUNT(*) AS units
    FROM dbo.vw_originations
    WHERE hmda_applicationdate >= @trendStart AND hmda_applicationdate < @trendEnd
    GROUP BY DATEFROMPARTS(YEAR(hmda_applicationdate), MONTH(hmda_applicationdate), 1)
), funded AS (
    SELECT DATEFROMPARTS(YEAR(funding_date), MONTH(funding_date), 1) AS m,
           COUNT(*) AS units
    FROM dbo.EncompassLoan_Gold
    WHERE funded_flag = 1 AND funding_date >= @trendStart AND funding_date < @trendEnd
    GROUP BY DATEFROMPARTS(YEAR(funding_date), MONTH(funding_date), 1)
)
SELECT m.m, ISNULL(o.units, 0) AS orig_units, ISNULL(f.units, 0) AS funded_units
FROM months m
LEFT JOIN orig o   ON o.m = m.m
LEFT JOIN funded f ON f.m = m.m
ORDER BY m.m";

    // ─── Per-tab fetchers ────────────────────────────────────────────────────

    public Task<OriginationsLanding> GetLandingAsync(int year, int month, DateTime weekEnding) =>
        Cached($"orig:landing:{year}:{month}:{weekEnding:yyyyMMdd}",
            () => FetchLandingAsync(year, month, weekEnding));

    private async Task<OriginationsLanding> FetchLandingAsync(int year, int month, DateTime weekEnding)
    {
        var p = MakeParams(year, month, weekEnding);

        var origTask = _sql.QueryAsync(LandingOriginationSql, p);
        var fundedTask = _sql.QueryAsync(LandingFundedSql, p);
        await Task.WhenAll(origTask, fundedTask);

        // Merge by LO name, like the LO summary in MonthlyDashboardService.
        var rows = new Dictionary<string, LoAccum>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in origTask.Result)
        {
            var name = r["lo"] as string ?? "(Unassigned)";
            var accum = rows.GetValueOrDefault(name);
            accum.LoanOfficer = name;
            accum.CwUnits         = ToInt(r["cw_units"]);
            accum.CwDollars       = ToDec(r["cw_dollars"]);
            accum.MtdUnits        = ToInt(r["mtd_units"]);
            accum.MtdDollars      = ToDec(r["mtd_dollars"]);
            accum.YtdUnits        = ToInt(r["ytd_units"]);
            accum.YtdDollars      = ToDec(r["ytd_dollars"]);
            rows[name] = accum;
        }
        foreach (var r in fundedTask.Result)
        {
            var name = r["lo"] as string ?? "(Unassigned)";
            var accum = rows.GetValueOrDefault(name);
            accum.LoanOfficer = name;
            accum.MtdFundedUnits   = ToInt(r["mtd_funded_units"]);
            accum.MtdFundedDollars = ToDec(r["mtd_funded_dollars"]);
            accum.YtdFundedUnits   = ToInt(r["ytd_funded_units"]);
            accum.YtdFundedDollars = ToDec(r["ytd_funded_dollars"]);
            rows[name] = accum;
        }

        var rowList = rows.Values
            .Select(a => new LoProductionRow(
                a.LoanOfficer, a.CwUnits, a.CwDollars,
                a.MtdUnits, a.MtdDollars, a.YtdUnits, a.YtdDollars,
                a.MtdFundedUnits, a.MtdFundedDollars, a.YtdFundedUnits, a.YtdFundedDollars))
            .OrderByDescending(x => x.YtdDollars)
            .ToList();

        var kpis = new OriginationKpis(
            CwUnits: rowList.Sum(x => x.CwUnits),
            CwVolume: rowList.Sum(x => x.CwDollars),
            MtdUnits: rowList.Sum(x => x.MtdUnits),
            MtdVolume: rowList.Sum(x => x.MtdDollars));

        return new OriginationsLanding(kpis, rowList);
    }

    public Task<ChannelPurposeMix> GetMixAsync(int year, int month) =>
        Cached($"orig:mix:{year}:{month}", () => FetchMixAsync(year, month));

    private async Task<ChannelPurposeMix> FetchMixAsync(int year, int month)
    {
        var p = MakeParams(year, month, DateTime.Today);
        var rows = await _sql.QueryAsync(MixSql, p);

        var byChannel = new Dictionary<string, MixBucket>(StringComparer.OrdinalIgnoreCase);
        var byPurpose = new Dictionary<string, MixBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var dim = (string)r["dim"]!;
            var label = (string)r["label"]!;
            var bucket = (dim == "channel" ? byChannel : byPurpose);
            var existing = bucket.GetValueOrDefault(label, new MixBucket(label, 0, 0m, 0, 0m));
            bucket[label] = existing with
            {
                OriginationUnits = existing.OriginationUnits + ToInt(r["orig_units"]),
                OriginationDollars = existing.OriginationDollars + ToDec(r["orig_dollars"]),
                FundedUnits = existing.FundedUnits + ToInt(r["funded_units"]),
                FundedDollars = existing.FundedDollars + ToDec(r["funded_dollars"]),
            };
        }

        return new ChannelPurposeMix(
            byChannel.Values.OrderByDescending(b => b.OriginationUnits).ToList(),
            byPurpose.Values.OrderByDescending(b => b.OriginationUnits).ToList());
    }

    public Task<IReadOnlyList<FunnelStage>> GetFunnelAsync(int year, int month, string? channel = null) =>
        Cached($"orig:funnel:{year}:{month}:{channel ?? "*"}", () => FetchFunnelAsync(year, month, channel));

    private async Task<IReadOnlyList<FunnelStage>> FetchFunnelAsync(int year, int month, string? channel)
    {
        var p = MakeParams(year, month, DateTime.Today)
            .Append(("@channel", string.IsNullOrWhiteSpace(channel) ? null : channel))
            .ToArray();
        var row = (await _sql.QueryAsync(FunnelSql, p)).Single();
        var apps = ToInt(row["apps"]);
        FunnelStage Stage(string label, object? raw)
        {
            var n = ToInt(raw);
            return new FunnelStage(label, n, apps == 0 ? 0 : 100.0 * n / apps);
        }
        return new List<FunnelStage>
        {
            Stage("Apps",      row["apps"]),
            Stage("Locked",    row["locked"]),
            Stage("Submitted to UW", row["submitted"]),
            Stage("Approved",  row["approved"]),
            Stage("Funded",    row["funded"]),
        };
    }

    public Task<IReadOnlyList<LeaderboardRow>> GetLeaderboardAsync(int year) =>
        Cached($"orig:leaderboard:{year}", () => FetchLeaderboardAsync(year));

    private async Task<IReadOnlyList<LeaderboardRow>> FetchLeaderboardAsync(int year)
    {
        // YTD-through-today comparison: cap the prior-year window at the same
        // calendar offset so apples-to-apples.
        var today = DateTime.Today;
        var yStart = new DateTime(year, 1, 1);
        var yEndForLeader = today.Year == year ? today.AddDays(1) : new DateTime(year + 1, 1, 1);
        var priorYStart = new DateTime(year - 1, 1, 1);
        var priorYEndForLeader = new DateTime(year - 1, today.Month, today.Day).AddDays(1);

        var rows = await _sql.QueryAsync(LeaderboardSql,
            ("@yStart", yStart),
            ("@yEndForLeader", yEndForLeader),
            ("@priorYStart", priorYStart),
            ("@priorYEndForLeader", priorYEndForLeader));

        return rows.Select((r, i) => new LeaderboardRow(
            Rank: i + 1,
            LoanOfficer: (string)r["lo"]!,
            Units: ToInt(r["units"]),
            Volume: ToDec(r["volume"]),
            UnitsPriorYear: ToInt(r["prior_units"]),
            VolumePriorYear: ToDec(r["prior_volume"]))).ToList();
    }

    public Task<IReadOnlyList<MonthlyTrendPoint>> GetTrendAsync(int year, int month) =>
        Cached($"orig:trend:{year}:{month}", () => FetchTrendAsync(year, month));

    private async Task<IReadOnlyList<MonthlyTrendPoint>> FetchTrendAsync(int year, int month)
    {
        // 12 months ending in the selected month (inclusive). Trend window starts
        // 11 months back from the first day of the selected month.
        var trendEnd = new DateTime(year, month, 1).AddMonths(1);
        var trendStart = trendEnd.AddMonths(-12);

        var rows = await _sql.QueryAsync(TrendSql,
            ("@trendStart", trendStart),
            ("@trendEnd", trendEnd));

        return rows.Select(r =>
        {
            var m = (DateTime)r["m"]!;
            return new MonthlyTrendPoint(
                Month: m.ToString("yyyy-MM"),
                OriginationUnits: ToInt(r["orig_units"]),
                FundedUnits: ToInt(r["funded_units"]));
        }).ToList();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (string, object?)[] MakeParams(int year, int month, DateTime weekEnding)
    {
        var mStart = new DateTime(year, month, 1);
        var mEnd = mStart.AddMonths(1);
        var yStart = new DateTime(year, 1, 1);
        var yEnd = yStart.AddYears(1);
        // CW window is the 7 days ending on weekEnding (inclusive).
        var cwEnd = weekEnding.Date.AddDays(1);
        var cwStart = cwEnd.AddDays(-7);
        return new (string, object?)[]
        {
            ("@mStart", mStart), ("@mEnd", mEnd),
            ("@yStart", yStart), ("@yEnd", yEnd),
            ("@cwStart", cwStart), ("@cwEnd", cwEnd),
        };
    }

    private static int ToInt(object? o) => o is null or DBNull ? 0 : Convert.ToInt32(o);
    private static decimal ToDec(object? o) => o is null or DBNull ? 0m : Convert.ToDecimal(o);

    private struct LoAccum
    {
        public string LoanOfficer;
        public int CwUnits; public decimal CwDollars;
        public int MtdUnits; public decimal MtdDollars;
        public int YtdUnits; public decimal YtdDollars;
        public int MtdFundedUnits; public decimal MtdFundedDollars;
        public int YtdFundedUnits; public decimal YtdFundedDollars;
    }
}
