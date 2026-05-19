using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

/// <summary>
/// Income statement and balance sheet for the executive dashboard.
/// Layouts mirror docs/Mastermind Financials.xlsx (sheets Comp_Summary
/// and Balance Sheet). Both pull from dbo.vw_financialgl; only months
/// listed in dbo.AccountingClosedPeriods are exposed.
///
/// Sign convention in vw_financialgl.amount is DR-positive / CR-negative:
///   - Revenue accounts: stored negative — flip for display.
///   - Expense accounts: stored positive — display as-is.
///   - Income totals (Gross Margin, EBITDA, Net Income): same as revenue, flip.
///   - Balance-sheet Asset balances: cumulative SUM is positive.
///   - Balance-sheet Liability / Equity balances: cumulative SUM is negative — flip.
/// </summary>
public class FinancialService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public FinancialService(SqlService sql, IMemoryCache cache)
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

    // ─── Closed periods ───────────────────────────────────────────────────────

    private const string ClosedPeriodsSql = @"
SELECT period_end, closed_at, closed_by, notes
FROM dbo.AccountingClosedPeriods
ORDER BY period_end DESC";

    public Task<IReadOnlyList<ClosedPeriod>> GetClosedPeriodsAsync() =>
        Cached("financial:closed-periods", FetchClosedPeriodsAsync);

    private async Task<IReadOnlyList<ClosedPeriod>> FetchClosedPeriodsAsync()
    {
        var rows = await _sql.QueryAsync(ClosedPeriodsSql,
            r => new ClosedPeriod(
                PeriodEnd: DateOnly.FromDateTime((DateTime)r["period_end"]!),
                ClosedAtUtc: (DateTime)r["closed_at"]!,
                ClosedBy: r["closed_by"] as string,
                Notes: r["notes"] as string));
        return rows;
    }

    public void InvalidateClosedPeriodsCache() => _cache.Remove("financial:closed-periods");

    // ─── Income statement ─────────────────────────────────────────────────────

    // One UNION ALL query that returns SUM(amount) per (period, level, value) for
    // the current and prior-year-same-month periods. We aggregate at every
    // fin_levelN since rows in the statement filter at different depths.
    private const string IsRollupSql = @"
WITH src AS (
    SELECT endofmonth, fin_level1, fin_level2, fin_level3, fin_level4, fin_level5, amount
    FROM dbo.vw_financialgl
    WHERE finstmt_type = 'IncomeStatement'
      AND endofmonth IN (@a, @b)
)
SELECT endofmonth, 'fin_level1' AS lvl, fin_level1 AS val, SUM(amount) AS amt
  FROM src GROUP BY endofmonth, fin_level1
UNION ALL
SELECT endofmonth, 'fin_level2', fin_level2, SUM(amount) FROM src GROUP BY endofmonth, fin_level2
UNION ALL
SELECT endofmonth, 'fin_level3', fin_level3, SUM(amount) FROM src GROUP BY endofmonth, fin_level3
UNION ALL
SELECT endofmonth, 'fin_level4', fin_level4, SUM(amount) FROM src GROUP BY endofmonth, fin_level4
UNION ALL
SELECT endofmonth, 'fin_level5', fin_level5, SUM(amount) FROM src GROUP BY endofmonth, fin_level5";

    private const string LoanStatsSql = @"
SELECT
    SUM(CASE WHEN funding_date >= @aStart AND funding_date < @aEnd
             THEN 1 ELSE 0 END) AS a_count,
    SUM(CASE WHEN funding_date >= @aStart AND funding_date < @aEnd
             THEN COALESCE(loanamount_fundedfinal, 0) ELSE 0 END) AS a_volume,
    SUM(CASE WHEN funding_date >= @bStart AND funding_date < @bEnd
             THEN 1 ELSE 0 END) AS b_count,
    SUM(CASE WHEN funding_date >= @bStart AND funding_date < @bEnd
             THEN COALESCE(loanamount_fundedfinal, 0) ELSE 0 END) AS b_volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1";

    public Task<IncomeStatementResult> GetIncomeStatementAsync(DateOnly periodA, DateOnly periodB) =>
        Cached($"financial:is:{periodA:yyyy-MM-dd}:{periodB:yyyy-MM-dd}",
            () => FetchIncomeStatementAsync(periodA, periodB));

    private async Task<IncomeStatementResult> FetchIncomeStatementAsync(DateOnly periodA, DateOnly periodB)
    {
        var aEom = periodA.ToDateTime(TimeOnly.MinValue);
        var bEom = periodB.ToDateTime(TimeOnly.MinValue);
        var aStart = new DateTime(periodA.Year, periodA.Month, 1);
        var aEnd   = aStart.AddMonths(1);
        var bStart = new DateTime(periodB.Year, periodB.Month, 1);
        var bEnd   = bStart.AddMonths(1);

        var glTask = _sql.QueryAsync(IsRollupSql,
            ("@a", aEom), ("@b", bEom));
        var loanTask = _sql.QueryAsync(LoanStatsSql,
            ("@aStart", aStart), ("@aEnd", aEnd),
            ("@bStart", bStart), ("@bEnd", bEnd));
        await Task.WhenAll(glTask, loanTask);

        var (lookA, lookB) = BuildLookups(glTask.Result, aEom, bEom);

        var loanRow = loanTask.Result.Single();
        var aCount = ToInt(loanRow["a_count"]);
        var aVol   = ToDec(loanRow["a_volume"]);
        var bCount = ToInt(loanRow["b_count"]);
        var bVol   = ToDec(loanRow["b_volume"]);

        var lines = BuildIsRows(lookA, lookB);
        var bpsLines = BuildBpsRows(lines, aVol, bVol);

        return new IncomeStatementResult(
            PeriodA: periodA,
            PeriodB: periodB,
            LoanCountA: aCount,
            LoanVolumeA: aVol,
            LoanCountB: bCount,
            LoanVolumeB: bVol,
            Lines: lines,
            BpsLines: bpsLines);
    }

    // ─── Balance sheet ────────────────────────────────────────────────────────

    // Cumulative SUM(amount) up to a given period_end. Returns one row per
    // (level, value) pair with both current and prior cumulative balances.
    private const string BsRollupSql = @"
DECLARE @upper DATE = CASE WHEN @a >= @b THEN @a ELSE @b END;
WITH src AS (
    SELECT endofmonth, fin_level1, fin_level2, fin_level3, fin_level4, fin_level5, amount
    FROM dbo.vw_financialgl
    WHERE finstmt_type = 'BalanceSheet'
      AND endofmonth <= @upper
)
SELECT 'fin_level1' AS lvl, fin_level1 AS val,
       SUM(CASE WHEN endofmonth <= @a THEN amount ELSE 0 END) AS a_amt,
       SUM(CASE WHEN endofmonth <= @b THEN amount ELSE 0 END) AS b_amt
  FROM src GROUP BY fin_level1
UNION ALL
SELECT 'fin_level2', fin_level2,
       SUM(CASE WHEN endofmonth <= @a THEN amount ELSE 0 END),
       SUM(CASE WHEN endofmonth <= @b THEN amount ELSE 0 END)
  FROM src GROUP BY fin_level2
UNION ALL
SELECT 'fin_level3', fin_level3,
       SUM(CASE WHEN endofmonth <= @a THEN amount ELSE 0 END),
       SUM(CASE WHEN endofmonth <= @b THEN amount ELSE 0 END)
  FROM src GROUP BY fin_level3
UNION ALL
SELECT 'fin_level4', fin_level4,
       SUM(CASE WHEN endofmonth <= @a THEN amount ELSE 0 END),
       SUM(CASE WHEN endofmonth <= @b THEN amount ELSE 0 END)
  FROM src GROUP BY fin_level4
UNION ALL
SELECT 'fin_level5', fin_level5,
       SUM(CASE WHEN endofmonth <= @a THEN amount ELSE 0 END),
       SUM(CASE WHEN endofmonth <= @b THEN amount ELSE 0 END)
  FROM src GROUP BY fin_level5";

    // YTD Net Income for the balance sheet's equity section. Each period uses
    // its own calendar-year start (so comparing Dec 2025 vs Aug 2024 gets each
    // their right YTD slice). Flip sign because revenue is stored credit-negative.
    private const string BsYtdNiSql = @"
SELECT
    SUM(CASE WHEN endofmonth >= @aYearStart AND endofmonth <= @a
             THEN amount ELSE 0 END) AS a_ytd,
    SUM(CASE WHEN endofmonth >= @bYearStart AND endofmonth <= @b
             THEN amount ELSE 0 END) AS b_ytd
FROM dbo.vw_financialgl
WHERE finstmt_type = 'IncomeStatement'";

    public Task<BalanceSheetResult> GetBalanceSheetAsync(DateOnly periodA, DateOnly periodB) =>
        Cached($"financial:bs:{periodA:yyyy-MM-dd}:{periodB:yyyy-MM-dd}",
            () => FetchBalanceSheetAsync(periodA, periodB));

    private async Task<BalanceSheetResult> FetchBalanceSheetAsync(DateOnly periodA, DateOnly periodB)
    {
        var aEom = periodA.ToDateTime(TimeOnly.MinValue);
        var bEom = periodB.ToDateTime(TimeOnly.MinValue);
        var aYearStart = new DateTime(periodA.Year, 1, 1);
        var bYearStart = new DateTime(periodB.Year, 1, 1);

        var glTask = _sql.QueryAsync(BsRollupSql,
            ("@a", aEom), ("@b", bEom));
        var niTask = _sql.QueryAsync(BsYtdNiSql,
            ("@a", aEom), ("@b", bEom),
            ("@aYearStart", aYearStart), ("@bYearStart", bYearStart));
        await Task.WhenAll(glTask, niTask);

        var dictA = new Dictionary<(string, string), decimal>();
        var dictB = new Dictionary<(string, string), decimal>();
        foreach (var r in glTask.Result)
        {
            var lvl = (string)r["lvl"]!;
            var val = r["val"] as string;
            if (val is null) continue;
            dictA[(lvl, val)] = ToDec(r["a_amt"]);
            dictB[(lvl, val)] = ToDec(r["b_amt"]);
        }

        var ytdRow = niTask.Result.Single();
        var aYtdNi = -ToDec(ytdRow["a_ytd"]);
        var bYtdNi = -ToDec(ytdRow["b_ytd"]);

        var (lines, checkA, checkB) = BuildBsRows(dictA, dictB, periodA, periodB, aYtdNi, bYtdNi);

        return new BalanceSheetResult(
            PeriodA: periodA,
            PeriodB: periodB,
            Lines: lines,
            CheckA: checkA,
            CheckB: checkB);
    }

    // ─── Expense Detail ───────────────────────────────────────────────────────

    // Display order of fin_level4 expense categories with their fin_level5
    // subcategories underneath. Drives both the SQL filter and the UI rendering
    // sequence (categories shown top-to-bottom in this order, subcategories
    // shown left-to-right in this order within each).
    private static readonly (string Category, string[] Subcategories)[] ExpenseLayout =
    {
        ("Direct Loan Costs", new[] { "Commission", "Loan Costs" }),
        ("Personnel Costs",   new[] { "Wages", "Overtime", "Bonus", "Benefits" }),
        ("G&A", new[]
        {
            "Facility Expense", "IT Related Costs", "Marketing Expense",
            "Professional Fees", "Office Supplies", "Travel Expense",
            "Insurance Expense", "License Expense", "Employee Goodwill", "Other G&A"
        })
    };

    // Hardcoded into the SQL string — values are compile-time-known, never
    // user input, so SQL-injection safe.
    private static readonly string ExpenseCategoryInList =
        string.Join(",", ExpenseLayout.Select(c => $"'{c.Category.Replace("'", "''")}'"));

    private static readonly string ExpenseDetailSql = $@"
SELECT
    fin_level4 AS category,
    fin_level5 AS subcategory,
    CASE WHEN vendor_name IS NULL OR LTRIM(RTRIM(vendor_name)) = ''
         THEN '(Unspecified)' ELSE vendor_name END AS vendor,
    SUM(CASE WHEN endofmonth = @a THEN amount ELSE 0 END) AS a_amount,
    SUM(CASE WHEN endofmonth = @a THEN COALESCE(transaction_count, 0) ELSE 0 END) AS a_count,
    SUM(CASE WHEN endofmonth = @b THEN amount ELSE 0 END) AS b_amount,
    SUM(CASE WHEN endofmonth = @b THEN COALESCE(transaction_count, 0) ELSE 0 END) AS b_count
FROM dbo.vw_financialgl
WHERE finstmt_type = 'IncomeStatement'
  AND endofmonth IN (@a, @b)
  AND fin_level4 IN ({ExpenseCategoryInList})
GROUP BY fin_level4, fin_level5,
         CASE WHEN vendor_name IS NULL OR LTRIM(RTRIM(vendor_name)) = ''
              THEN '(Unspecified)' ELSE vendor_name END
HAVING SUM(amount) <> 0";

    public Task<ExpenseDetailResult> GetExpenseDetailAsync(DateOnly periodA, DateOnly periodB) =>
        Cached($"financial:exp:{periodA:yyyy-MM-dd}:{periodB:yyyy-MM-dd}",
            () => FetchExpenseDetailAsync(periodA, periodB));

    private async Task<ExpenseDetailResult> FetchExpenseDetailAsync(DateOnly periodA, DateOnly periodB)
    {
        var rows = await _sql.QueryAsync(ExpenseDetailSql,
            ("@a", periodA.ToDateTime(TimeOnly.MinValue)),
            ("@b", periodB.ToDateTime(TimeOnly.MinValue)));

        // Index by (category, subcategory) for fast lookup while preserving
        // canonical order.
        var bySub = rows
            .GroupBy(r => ((string)r["category"]!, (string)r["subcategory"]!))
            .ToDictionary(g => g.Key, g => g.ToList());

        var groups = new List<ExpenseCategoryGroup>(ExpenseLayout.Length);
        foreach (var (category, subs) in ExpenseLayout)
        {
            var subRows = new List<ExpenseSubcategoryRow>(subs.Length);
            foreach (var sub in subs)
            {
                if (!bySub.TryGetValue((category, sub), out var vendorRows))
                {
                    subRows.Add(new ExpenseSubcategoryRow(
                        Category: category, Label: sub,
                        ValueA: 0, ValueB: 0, Variance: 0, VariancePct: null,
                        VendorCountA: 0, VendorCountB: 0,
                        Vendors: Array.Empty<ExpenseVendorRow>()));
                    continue;
                }

                var vendors = vendorRows.Select(r =>
                {
                    var aAmt = ToDec(r["a_amount"]);
                    var bAmt = ToDec(r["b_amount"]);
                    return new ExpenseVendorRow(
                        Category: category,
                        Subcategory: sub,
                        Vendor: (string)r["vendor"]!,
                        ValueA: aAmt,
                        ValueB: bAmt,
                        Variance: aAmt - bAmt,
                        TxnCountA: ToInt(r["a_count"]),
                        TxnCountB: ToInt(r["b_count"]));
                })
                .OrderByDescending(v => v.ValueA != 0m)
                .ThenByDescending(v => v.ValueA != 0m ? v.ValueA : v.ValueB)
                .ToList();

                var subA = vendors.Sum(v => v.ValueA);
                var subB = vendors.Sum(v => v.ValueB);
                var subVar = subA - subB;
                decimal? subPct = subB == 0m ? null : subVar / Math.Abs(subB);

                subRows.Add(new ExpenseSubcategoryRow(
                    Category: category, Label: sub,
                    ValueA: subA, ValueB: subB, Variance: subVar, VariancePct: subPct,
                    VendorCountA: vendors.Count(v => v.ValueA != 0m),
                    VendorCountB: vendors.Count(v => v.ValueB != 0m),
                    Vendors: vendors));
            }

            var catA = subRows.Sum(s => s.ValueA);
            var catB = subRows.Sum(s => s.ValueB);
            var catVar = catA - catB;
            decimal? catPct = catB == 0m ? null : catVar / Math.Abs(catB);

            groups.Add(new ExpenseCategoryGroup(
                Category: category,
                ValueA: catA, ValueB: catB, Variance: catVar, VariancePct: catPct,
                Subcategories: subRows));
        }

        return new ExpenseDetailResult(periodA, periodB, groups);
    }

    private const string ExpenseTransactionsSql = @"
SELECT post_date, journal_number, gl_account, account_desc, branch, print_desc, amount
FROM dbo.vw_GLTransaction_Silver
WHERE endofmonth = @period
  AND fin_level5 = @subcategory
  AND ((@unspecified = 1 AND (vendor_name IS NULL OR LTRIM(RTRIM(vendor_name)) = ''))
       OR (@unspecified = 0 AND vendor_name = @vendor))
ORDER BY post_date DESC, journal_number DESC";

    public Task<IReadOnlyList<ExpenseTransactionRow>> GetExpenseTransactionsAsync(
        string subcategory, string vendor, DateOnly period) =>
        Cached($"financial:exp-tx:{period:yyyy-MM-dd}:{subcategory}:{vendor}",
            () => FetchExpenseTransactionsAsync(subcategory, vendor, period));

    private async Task<IReadOnlyList<ExpenseTransactionRow>> FetchExpenseTransactionsAsync(
        string subcategory, string vendor, DateOnly period)
    {
        var unspecified = vendor == "(Unspecified)" ? 1 : 0;
        var rows = await _sql.QueryAsync(ExpenseTransactionsSql,
            r => new ExpenseTransactionRow(
                PostDate: DateOnly.FromDateTime(Convert.ToDateTime(r["post_date"])),
                JournalNumber: Convert.ToInt64(r["journal_number"]),
                GlAccount: Convert.ToInt32(r["gl_account"]),
                AccountDesc: r["account_desc"] as string,
                Branch: r["branch"] as string,
                Memo: r["print_desc"] as string,
                Amount: ToDec(r["amount"])),
            ("@period", period.ToDateTime(TimeOnly.MinValue)),
            ("@subcategory", subcategory),
            ("@vendor", vendor),
            ("@unspecified", unspecified));
        return rows;
    }

    // ─── Trending Income Statement ────────────────────────────────────────────

    private const string TrendIsRollupSql = @"
WITH src AS (
    SELECT endofmonth, fin_level1, fin_level2, fin_level3, fin_level4, fin_level5, amount
    FROM dbo.vw_financialgl
    WHERE finstmt_type = 'IncomeStatement'
      AND endofmonth >= @start AND endofmonth <= @end
)
SELECT endofmonth, 'fin_level1' AS lvl, fin_level1 AS val, SUM(amount) AS amt
  FROM src GROUP BY endofmonth, fin_level1
UNION ALL
SELECT endofmonth, 'fin_level2', fin_level2, SUM(amount) FROM src GROUP BY endofmonth, fin_level2
UNION ALL
SELECT endofmonth, 'fin_level3', fin_level3, SUM(amount) FROM src GROUP BY endofmonth, fin_level3
UNION ALL
SELECT endofmonth, 'fin_level4', fin_level4, SUM(amount) FROM src GROUP BY endofmonth, fin_level4
UNION ALL
SELECT endofmonth, 'fin_level5', fin_level5, SUM(amount) FROM src GROUP BY endofmonth, fin_level5";

    private const string TrendLoanStatsSql = @"
SELECT YEAR(funding_date) AS yr, MONTH(funding_date) AS mo,
       COUNT(*) AS cnt,
       SUM(COALESCE(loanamount_fundedfinal, 0)) AS vol
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND funding_date >= @start
  AND funding_date <  @endExclusive
GROUP BY YEAR(funding_date), MONTH(funding_date)";

    public Task<TrendingIncomeStatementResult> GetTrendingIncomeStatementAsync(DateOnly anchor) =>
        Cached($"financial:trend-is:{anchor:yyyy-MM-dd}",
            () => FetchTrendingIncomeStatementAsync(anchor));

    private async Task<TrendingIncomeStatementResult> FetchTrendingIncomeStatementAsync(DateOnly anchor)
    {
        // Build the candidate window of 12 calendar months ending at anchor.
        var anchorFirst = new DateTime(anchor.Year, anchor.Month, 1);
        var candidates = Enumerable.Range(0, 12)
            .Select(i => DateOnly.FromDateTime(
                anchorFirst.AddMonths(-11 + i).AddMonths(1).AddDays(-1)))
            .ToList();

        // Intersect with closed periods so we never expose pre-close data.
        var closed = (await GetClosedPeriodsAsync()).Select(c => c.PeriodEnd).ToHashSet();
        var months = candidates.Where(closed.Contains).ToList();

        if (months.Count == 0)
        {
            return new TrendingIncomeStatementResult(
                anchor,
                Array.Empty<DateOnly>(),
                Array.Empty<int>(),
                Array.Empty<decimal>(),
                Array.Empty<TrendingFinRow>());
        }

        var earliest = months.First();
        var latest = months.Last();
        var earliestFirst = new DateTime(earliest.Year, earliest.Month, 1);
        var latestFirstNext = new DateTime(latest.Year, latest.Month, 1).AddMonths(1);

        var glTask = _sql.QueryAsync(TrendIsRollupSql,
            ("@start", earliest.ToDateTime(TimeOnly.MinValue)),
            ("@end",   latest.ToDateTime(TimeOnly.MinValue)));
        var loanTask = _sql.QueryAsync(TrendLoanStatsSql,
            ("@start", earliestFirst),
            ("@endExclusive", latestFirstNext));
        await Task.WhenAll(glTask, loanTask);

        var byMonth = BuildMonthlyLookups(glTask.Result, months);

        var loanByMonth = loanTask.Result
            .ToDictionary(r => (Convert.ToInt32(r["yr"]), Convert.ToInt32(r["mo"])),
                          r => (cnt: ToInt(r["cnt"]), vol: ToDec(r["vol"])));
        var counts = months
            .Select(m => loanByMonth.TryGetValue((m.Year, m.Month), out var v) ? v.cnt : 0)
            .ToArray();
        var volumes = months
            .Select(m => loanByMonth.TryGetValue((m.Year, m.Month), out var v) ? v.vol : 0m)
            .ToArray();

        var lines = BuildTrendingIsRows(months, byMonth);

        return new TrendingIncomeStatementResult(anchor, months, counts, volumes, lines);
    }

    // ─── Row builders ─────────────────────────────────────────────────────────

    private delegate decimal Lookup(string col, string val);

    private static (Lookup cur, Lookup prior) BuildLookups(
        IReadOnlyList<Dictionary<string, object?>> rows, DateTime curEom, DateTime priorEom)
    {
        var curDict = new Dictionary<(string, string), decimal>();
        var priorDict = new Dictionary<(string, string), decimal>();
        foreach (var r in rows)
        {
            var lvl = (string)r["lvl"]!;
            var val = r["val"] as string;
            if (val is null) continue;
            var amt = ToDec(r["amt"]);
            var when = (DateTime)r["endofmonth"]!;
            var target = when == curEom ? curDict : when == priorEom ? priorDict : null;
            if (target is null) continue;
            target[(lvl, val)] = amt;
        }
        decimal Get(IDictionary<(string, string), decimal> d, string c, string v) =>
            d.TryGetValue((c, v), out var x) ? x : 0m;
        return ((c, v) => Get(curDict, c, v), (c, v) => Get(priorDict, c, v));
    }

    /// <summary>Builds one Lookup per month from a multi-month rollup result.</summary>
    private static Dictionary<DateOnly, Lookup> BuildMonthlyLookups(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<DateOnly> months)
    {
        var dicts = months.ToDictionary(m => m,
            _ => new Dictionary<(string, string), decimal>());
        foreach (var r in rows)
        {
            var lvl = (string)r["lvl"]!;
            var val = r["val"] as string;
            if (val is null) continue;
            var when = DateOnly.FromDateTime((DateTime)r["endofmonth"]!);
            if (!dicts.TryGetValue(when, out var d)) continue;
            d[(lvl, val)] = ToDec(r["amt"]);
        }
        decimal Get(IDictionary<(string, string), decimal> d, string c, string v) =>
            d.TryGetValue((c, v), out var x) ? x : 0m;
        return dicts.ToDictionary(
            kv => kv.Key,
            kv => (Lookup)((c, v) => Get(kv.Value, c, v)));
    }

    /// <summary>
    /// Same row layout as BuildIsRows but produces one TrendingFinRow per line
    /// with a Values list parallel to the input months. Sign conventions match
    /// the single-period IS exactly so a column from this matches the IS tab.
    /// </summary>
    private static IReadOnlyList<TrendingFinRow> BuildTrendingIsRows(
        IReadOnlyList<DateOnly> months,
        IReadOnlyDictionary<DateOnly, Lookup> byMonth)
    {
        var rows = new List<TrendingFinRow>();

        void Add(string label, int indent, FinStyle style, Func<Lookup, decimal> compute)
        {
            var vals = months.Select(m => compute(byMonth[m])).ToList();
            rows.Add(new TrendingFinRow(label, indent, style, vals));
        }
        void Header(string label) => Add(label, 0, FinStyle.Header, _ => 0m);
        void Blank() => Add("", 0, FinStyle.Blank, _ => 0m);
        void Line(string label, int indent, Func<Lookup, decimal> f) => Add(label, indent, FinStyle.Line, f);
        void Sub(string label, int indent, Func<Lookup, decimal> f) => Add(label, indent, FinStyle.Subtotal, f);
        void Total(string label, Func<Lookup, decimal> f) => Add(label, 0, FinStyle.Total, f);

        // Revenue accounts stored credit-negative; flip for display.
        Header("REVENUE");
        Line("Origination Revenue",   1, l => -l("fin_level4", "Origination Revenue"));
        Line("Margin Revenue",        1, l => -l("fin_level4", "Margin Revenue"));
        Line("Secondary Revenue",     1, l => -l("fin_level4", "Secondary Revenue"));
        Line("Servicing Revenue",     1, l => -l("fin_level4", "Servicing Revenue"));
        Line("Interest Revenue",      1, l => -l("fin_level4", "Interest Revenue"));
        Line("Brokered Loan Revenue", 1, l => -l("fin_level4", "Brokered Loan Revenue"));
        Sub ("Revenue",               0, l => -l("fin_level3", "Revenue"));
        Blank();

        Header("DIRECT LOAN COSTS");
        Line("Commission",            1, l => l("fin_level5", "Commission"));
        Line("Loan Costs",            1, l => l("fin_level5", "Loan Costs"));
        Sub ("Direct Loan Costs",     0, l => l("fin_level4", "Direct Loan Costs"));
        Blank();

        // Gross Margin = -L3.Revenue - L4.DirectLoanCosts (both display-positive).
        Sub("Gross Margin", 0, l => -l("fin_level3", "Revenue") - l("fin_level4", "Direct Loan Costs"));
        Blank();

        Header("PERSONNEL COSTS");
        Line("Wages",            1, l => l("fin_level5", "Wages"));
        Line("Overtime",         1, l => l("fin_level5", "Overtime"));
        Line("Bonus",            1, l => l("fin_level5", "Bonus"));
        Line("Benefits",         1, l => l("fin_level5", "Benefits"));
        Sub ("Personnel Costs",  0, l => l("fin_level4", "Personnel Costs"));
        Blank();

        Header("G&A EXPENSE");
        Line("Facility Expense",  1, l => l("fin_level5", "Facility Expense"));
        Line("IT Related Costs",  1, l => l("fin_level5", "IT Related Costs"));
        Line("Marketing Expense", 1, l => l("fin_level5", "Marketing Expense"));
        Line("Professional Fees", 1, l => l("fin_level5", "Professional Fees"));
        Line("Office Supplies",   1, l => l("fin_level5", "Office Supplies"));
        Line("Travel Expense",    1, l => l("fin_level5", "Travel Expense"));
        Line("Insurance Expense", 1, l => l("fin_level5", "Insurance Expense"));
        Line("License Expense",   1, l => l("fin_level5", "License Expense"));
        Line("Employee Goodwill", 1, l => l("fin_level5", "Employee Goodwill"));
        Line("Other G&A",         1, l => l("fin_level5", "Other G&A"));
        Sub ("G&A",               0, l => l("fin_level4", "G&A"));
        Blank();

        Sub("Total Expense", 0, l => l("fin_level3", "Expense"));
        Blank();

        Total("EBITDA", l => -l("fin_level2", "EBITDA"));
        Blank();

        Line("Other Income and Expense", 0, l => l("fin_level3", "Other Income and Expense"));
        Blank();

        Total("Net Income", l => -l("fin_level1", "Net Income"));

        return rows;
    }

    private static IReadOnlyList<FinRow> BuildIsRows(Lookup cur, Lookup prior)
    {
        var rows = new List<FinRow>();

        void Header(string label) => rows.Add(Row(label, 0, FinStyle.Header, 0, 0));
        void Blank() => rows.Add(Row("", 0, FinStyle.Blank, 0, 0));
        void Line(string label, int indent, decimal c, decimal p) =>
            rows.Add(Row(label, indent, FinStyle.Line, c, p));
        void Subtotal(string label, int indent, decimal c, decimal p) =>
            rows.Add(Row(label, indent, FinStyle.Subtotal, c, p));
        void Total(string label, decimal c, decimal p) =>
            rows.Add(Row(label, 0, FinStyle.Total, c, p));

        // Revenue accounts are stored credit-negative in the GL; flip for display.
        decimal RevC(string col, string val) => -cur(col, val);
        decimal RevP(string col, string val) => -prior(col, val);

        Header("REVENUE");
        Line("Origination Revenue",     1, RevC("fin_level4", "Origination Revenue"),  RevP("fin_level4", "Origination Revenue"));
        Line("Margin Revenue",          1, RevC("fin_level4", "Margin Revenue"),       RevP("fin_level4", "Margin Revenue"));
        Line("Secondary Revenue",       1, RevC("fin_level4", "Secondary Revenue"),    RevP("fin_level4", "Secondary Revenue"));
        Line("Servicing Revenue",       1, RevC("fin_level4", "Servicing Revenue"),    RevP("fin_level4", "Servicing Revenue"));
        Line("Interest Revenue",        1, RevC("fin_level4", "Interest Revenue"),     RevP("fin_level4", "Interest Revenue"));
        Line("Brokered Loan Revenue",   1, RevC("fin_level4", "Brokered Loan Revenue"),RevP("fin_level4", "Brokered Loan Revenue"));
        Subtotal("Revenue",             0, RevC("fin_level3", "Revenue"),              RevP("fin_level3", "Revenue"));
        Blank();

        Header("DIRECT LOAN COSTS");
        Line("Commission",              1, cur("fin_level5", "Commission"),            prior("fin_level5", "Commission"));
        Line("Loan Costs",              1, cur("fin_level5", "Loan Costs"),            prior("fin_level5", "Loan Costs"));
        Subtotal("Direct Loan Costs",   0, cur("fin_level4", "Direct Loan Costs"),     prior("fin_level4", "Direct Loan Costs"));
        Blank();

        // Gross Margin = Revenue - Direct Loan Costs (both display-signed positive).
        var gmCur   = RevC("fin_level3", "Revenue") - cur("fin_level4", "Direct Loan Costs");
        var gmPrior = RevP("fin_level3", "Revenue") - prior("fin_level4", "Direct Loan Costs");
        Subtotal("Gross Margin", 0, gmCur, gmPrior);
        Blank();

        Header("PERSONNEL COSTS");
        Line("Wages",                   1, cur("fin_level5", "Wages"),     prior("fin_level5", "Wages"));
        Line("Overtime",                1, cur("fin_level5", "Overtime"),  prior("fin_level5", "Overtime"));
        Line("Bonus",                   1, cur("fin_level5", "Bonus"),     prior("fin_level5", "Bonus"));
        Line("Benefits",                1, cur("fin_level5", "Benefits"),  prior("fin_level5", "Benefits"));
        Subtotal("Personnel Costs",     0, cur("fin_level4", "Personnel Costs"), prior("fin_level4", "Personnel Costs"));
        Blank();

        Header("G&A EXPENSE");
        Line("Facility Expense",        1, cur("fin_level5", "Facility Expense"),    prior("fin_level5", "Facility Expense"));
        Line("IT Related Costs",        1, cur("fin_level5", "IT Related Costs"),    prior("fin_level5", "IT Related Costs"));
        Line("Marketing Expense",       1, cur("fin_level5", "Marketing Expense"),   prior("fin_level5", "Marketing Expense"));
        Line("Professional Fees",       1, cur("fin_level5", "Professional Fees"),   prior("fin_level5", "Professional Fees"));
        Line("Office Supplies",         1, cur("fin_level5", "Office Supplies"),     prior("fin_level5", "Office Supplies"));
        Line("Travel Expense",          1, cur("fin_level5", "Travel Expense"),      prior("fin_level5", "Travel Expense"));
        Line("Insurance Expense",       1, cur("fin_level5", "Insurance Expense"),   prior("fin_level5", "Insurance Expense"));
        Line("License Expense",         1, cur("fin_level5", "License Expense"),     prior("fin_level5", "License Expense"));
        Line("Employee Goodwill",       1, cur("fin_level5", "Employee Goodwill"),   prior("fin_level5", "Employee Goodwill"));
        Line("Other G&A",               1, cur("fin_level5", "Other G&A"),           prior("fin_level5", "Other G&A"));
        Subtotal("G&A",                 0, cur("fin_level4", "G&A"),                 prior("fin_level4", "G&A"));
        Blank();

        Subtotal("Total Expense",       0, cur("fin_level3", "Expense"),             prior("fin_level3", "Expense"));
        Blank();

        // EBITDA and Net Income are roll-ups that include revenue, so they flip sign.
        var ebitdaCur   = -cur("fin_level2", "EBITDA");
        var ebitdaPrior = -prior("fin_level2", "EBITDA");
        Total("EBITDA", ebitdaCur, ebitdaPrior);
        Blank();

        // Other Income and Expense — expenses-style (no flip in the spreadsheet).
        Line("Other Income and Expense", 0,
            cur("fin_level3", "Other Income and Expense"),
            prior("fin_level3", "Other Income and Expense"));
        Blank();

        var niCur   = -cur("fin_level1", "Net Income");
        var niPrior = -prior("fin_level1", "Net Income");
        Total("Net Income", niCur, niPrior);

        return rows;
    }

    /// <summary>
    /// "BPS" section from the Excel: each major line as basis points of loan volume.
    /// Pulls values out of the IS rows by label so it stays in sync.
    /// </summary>
    private static IReadOnlyList<FinRow> BuildBpsRows(IReadOnlyList<FinRow> lines, decimal volA, decimal volB)
    {
        var by = lines
            .Where(r => r.Style is not FinStyle.Blank and not FinStyle.Header)
            .ToDictionary(r => r.Label, r => r, StringComparer.Ordinal);

        decimal Bps(decimal amount, decimal volume) =>
            volume == 0m ? 0m : Math.Round(amount / volume * 10_000m, 2);

        FinRow Make(string label, int indent, FinStyle style)
        {
            if (!by.TryGetValue(label, out var src))
                return Row(label, indent, style, 0, 0);
            return Row(label, indent, style, Bps(src.ValueA, volA), Bps(src.ValueB, volB));
        }

        var rows = new List<FinRow>
        {
            Make("Origination Revenue", 1, FinStyle.Line),
            Make("Margin Revenue",      1, FinStyle.Line),
            Make("Secondary Revenue",   1, FinStyle.Line),
            Make("Servicing Revenue",   1, FinStyle.Line),
            Make("Interest Revenue",    1, FinStyle.Line),
            Make("Revenue",             0, FinStyle.Subtotal),
            Make("Commission",          1, FinStyle.Line),
            Make("Direct Loan Costs",   0, FinStyle.Subtotal),
            Make("Gross Margin",        0, FinStyle.Subtotal),
            Make("Wages",               1, FinStyle.Line),
            Make("Bonus",               1, FinStyle.Line),
            Make("Benefits",            1, FinStyle.Line),
            Make("Personnel Costs",     0, FinStyle.Subtotal),
            Make("G&A",                 0, FinStyle.Subtotal),
            Make("EBITDA",              0, FinStyle.Total),
            Make("Net Income",          0, FinStyle.Total),
        };
        return rows;
    }

    private static (IReadOnlyList<FinRow> rows, decimal checkA, decimal checkB) BuildBsRows(
        IDictionary<(string, string), decimal> dictA,
        IDictionary<(string, string), decimal> dictB,
        DateOnly periodA,
        DateOnly periodB,
        decimal aYtdNi,
        decimal bYtdNi)
    {
        decimal C(string col, string val) => dictA.TryGetValue((col, val), out var v) ? v : 0m;
        decimal P(string col, string val) => dictB.TryGetValue((col, val), out var v) ? v : 0m;

        var rows = new List<FinRow>();
        void Header(string label) => rows.Add(Row(label, 0, FinStyle.Header, 0, 0));
        void Blank() => rows.Add(Row("", 0, FinStyle.Blank, 0, 0));
        void Line(string label, int indent, decimal c, decimal p) =>
            rows.Add(Row(label, indent, FinStyle.Line, c, p));
        void Sub(string label, int indent, decimal c, decimal p) =>
            rows.Add(Row(label, indent, FinStyle.Subtotal, c, p));
        void Total(string label, decimal c, decimal p) =>
            rows.Add(Row(label, 0, FinStyle.Total, c, p));

        // ASSETS — naturally debit-positive, display as-is.
        Header("ASSETS");
        Header("Current Assets");
        Line("Cash",                    2, C("fin_level4", "Cash"),                    P("fin_level4", "Cash"));
        Line("Restricted Cash",         2, C("fin_level4", "Restricted Cash"),         P("fin_level4", "Restricted Cash"));
        Sub ("Cash",                    1, C("fin_level3", "Cash"),                    P("fin_level3", "Cash"));
        Line("Accounts Receivable",    2, C("fin_level4", "Accounts Receivable"),     P("fin_level4", "Accounts Receivable"));
        Line("Loans Held For Sale",    2, C("fin_level4", "Loans Held For Sale"),     P("fin_level4", "Loans Held For Sale"));
        Line("Note Receivable",        2, C("fin_level4", "Note Receivable"),         P("fin_level4", "Note Receivable"));
        Sub ("Receivables",            1, C("fin_level3", "Receivables"),             P("fin_level3", "Receivables"));
        Line("Prepaid Insurance",      2, C("fin_level4", "Prepaid Insurance"),       P("fin_level4", "Prepaid Insurance"));
        Line("Prepaid Expenses",       2, C("fin_level5", "Prepaid Expenses"),        P("fin_level5", "Prepaid Expenses"));
        Sub ("Prepaid Expenses",       1, C("fin_level3", "Prepaid Expenses"),        P("fin_level3", "Prepaid Expenses"));
        Line("IRLCs",                  2, C("fin_level5", "IRLCs"),                   P("fin_level5", "IRLCs"));
        Line("Open Trades",            2, C("fin_level5", "Open Trades"),             P("fin_level5", "Open Trades"));
        Line("Pair Offs",              2, C("fin_level5", "Pair Offs"),               P("fin_level5", "Pair Offs"));
        Sub ("Derivative Assets",      1, C("fin_level4", "Derivative Assets"),       P("fin_level4", "Derivative Assets"));
        Line("IRLC",                   2, C("fin_level5", "IRLC"),                    P("fin_level5", "IRLC"));
        Line("Open Trade",             2, C("fin_level5", "Open Trade"),              P("fin_level5", "Open Trade"));
        Line("Pair Off",               2, C("fin_level5", "Pair Off"),                P("fin_level5", "Pair Off"));
        Sub ("Derivative Liabilities", 1, C("fin_level4", "Derivative Liabilities"),  P("fin_level4", "Derivative Liabilities"));
        Sub ("Investments",            1, C("fin_level3", "Investments"),             P("fin_level3", "Investments"));
        Sub ("Total Current Assets",   0, C("fin_level2", "Current Assets"),          P("fin_level2", "Current Assets"));
        Blank();

        Header("Noncurrent Assets");
        Line("Equipment",                            2, C("fin_level5", "Equipment"),                 P("fin_level5", "Equipment"));
        Line("Vehicles",                             2, C("fin_level5", "Vehicles"),                  P("fin_level5", "Vehicles"));
        Line("Leasehold Improvements",               2, C("fin_level5", "Leasehold Improvements"),    P("fin_level5", "Leasehold Improvements"));
        Sub ("Gross Fixed Assets",                   1, C("fin_level4", "Gross Fixed Assets"),        P("fin_level4", "Gross Fixed Assets"));
        Line("AD Equipment",                         2, C("fin_level5", "AD Equipment"),              P("fin_level5", "AD Equipment"));
        Line("AD Vehicles",                          2, C("fin_level5", "AD Vehicles"),               P("fin_level5", "AD Vehicles"));
        Line("AD Leasehold Improvements",            2, C("fin_level5", "AD Leasehold Improvements"), P("fin_level5", "AD Leasehold Improvements"));
        Sub ("Accumulated Depreciation",             1, C("fin_level4", "Accumulated Depreciation"),  P("fin_level4", "Accumulated Depreciation"));
        Sub ("Fixed Assets",                         1, C("fin_level3", "Fixed Assets"),              P("fin_level3", "Fixed Assets"));
        Line("Gross Mortgage Servicing Rights",      2, C("fin_level5", "Gross Mortgage Servicing Rights"),  P("fin_level5", "Gross Mortgage Servicing Rights"));
        Line("MSR Accumulated Amortization",         2, C("fin_level5", "MSR Accumulated Amortization"),     P("fin_level5", "MSR Accumulated Amortization"));
        Sub ("Mortgage Servicing Rights",            1, C("fin_level3", "Mortgage Servicing Rights"),        P("fin_level3", "Mortgage Servicing Rights"));
        Line("Gross Lease ROU Asset",                2, C("fin_level5", "Gross Lease ROU Asset"),            P("fin_level5", "Gross Lease ROU Asset"));
        Line("ROU Asset Accumulated Amortization",   2, C("fin_level5", "ROU Asset Accumulated Amortization"), P("fin_level5", "ROU Asset Accumulated Amortization"));
        Sub ("Lease ROU Asset",                      1, C("fin_level4", "Lease ROU Asset"),                  P("fin_level4", "Lease ROU Asset"));
        Sub ("Total Noncurrent Assets",              0, C("fin_level2", "Noncurrent Assets"),               P("fin_level2", "Noncurrent Assets"));
        Blank();

        var totalAssetsC = C("fin_level1", "Asset");
        var totalAssetsP = P("fin_level1", "Asset");
        Total("Total Assets", totalAssetsC, totalAssetsP);
        Blank();

        // LIABILITIES — naturally credit-positive (stored negative); flip for display.
        Header("LIABILITIES");
        Header("Current Liabilities");
        Line("Accounts Payable",       2, -C("fin_level4", "Accounts Payable"),     -P("fin_level4", "Accounts Payable"));
        Line("Escrow Payable",         2, -C("fin_level4", "Escrow Payable"),       -P("fin_level4", "Escrow Payable"));
        Line("Purchase Clearing",      2, -C("fin_level4", "Purchase Clearing"),    -P("fin_level4", "Purchase Clearing"));
        Line("MI Payable",             2, -C("fin_level4", "MI Payable"),           -P("fin_level4", "MI Payable"));
        Line("Intercompany Payable",   2, -C("fin_level4", "Intercompany Payable"), -P("fin_level4", "Intercompany Payable"));
        Sub ("Accounts Payable",       1, -C("fin_level3", "Accounts Payable"),     -P("fin_level3", "Accounts Payable"));
        Line("Accrued 401k",           2, -C("fin_level4", "Accrued 401k"),         -P("fin_level4", "Accrued 401k"));
        Line("Accrued Bonus",          2, -C("fin_level4", "Accrued Bonus"),        -P("fin_level4", "Accrued Bonus"));
        Line("Accrued Commissions",    2, -C("fin_level4", "Accrued Commissions"),  -P("fin_level4", "Accrued Commissions"));
        Line("Accrued Payroll",        2, -C("fin_level4", "Accrued Payroll"),      -P("fin_level4", "Accrued Payroll"));
        Line("Accrued Payroll Taxes",  3, -C("fin_level5", "Accrued Payroll Taxes"),-P("fin_level5", "Accrued Payroll Taxes"));
        Line("Accrued FSA-HSA",        3, -C("fin_level5", "Accrued FSA-HAS"),      -P("fin_level5", "Accrued FSA-HAS"));
        Line("Accrued WC",             3, -C("fin_level5", "Accrued WC"),           -P("fin_level5", "Accrued WC"));
        Sub ("Accrued Payroll Taxes",  2, -C("fin_level4", "Accrued Payroll Taxes"),-P("fin_level4", "Accrued Payroll Taxes"));
        Line("Accrued Property Taxes", 2, -C("fin_level4", "Accrued Property Taxes"),-P("fin_level4", "Accrued Property Taxes"));
        Line("Warehouse LOC - Alliance Interest", 3, -C("fin_level5", "Warehouse LOC - Alliance Interest"), -P("fin_level5", "Warehouse LOC - Alliance Interest"));
        Line("Warehouse LOC - AB Interest",       3, -C("fin_level5", "Warehouse LOC - AB Interest"),       -P("fin_level5", "Warehouse LOC - AB Interest"));
        Line("Warehouse LOC - VTX Interest",      3, -C("fin_level5", "Warehouse LOC - VTX Interest"),      -P("fin_level5", "Warehouse LOC - VTX Interest"));
        Sub ("Accrued Warehouse Interest",        2, -C("fin_level4", "Accrued Warehouse Interest"),        -P("fin_level4", "Accrued Warehouse Interest"));
        Sub ("Accrued Expenses",                  1, -C("fin_level3", "Accrued Expenses"),                   -P("fin_level3", "Accrued Expenses"));
        Line("Warehouse LOC - AB",       2, -C("fin_level4", "Warehouse LOC - AB"),       -P("fin_level4", "Warehouse LOC - AB"));
        Line("Warehouse LOC - VTEX",     2, -C("fin_level4", "Warehouse LOC - VTEX"),     -P("fin_level4", "Warehouse LOC - VTEX"));
        Line("Warehouse LOC - Alliance", 2, -C("fin_level4", "Warehouse LOC - Alliance"), -P("fin_level4", "Warehouse LOC - Alliance"));
        Sub ("Warehouse LOC",            1, -C("fin_level3", "Warehouse LOC"),            -P("fin_level3", "Warehouse LOC"));
        Sub ("Loan Repurchase Reserve",  1, -C("fin_level3", "Loan Repurchase Reserve"),  -P("fin_level3", "Loan Repurchase Reserve"));
        Sub ("ST ROU Liability",         1, -C("fin_level3", "ST ROU Liability"),         -P("fin_level3", "ST ROU Liability"));
        Sub ("Other Current Liabilities",1, -C("fin_level3", "Other Current Liabilities"),-P("fin_level3", "Other Current Liabilities"));
        Sub ("Total Current Liabilities",0, -C("fin_level2", "Current Liabilities"),      -P("fin_level2", "Current Liabilities"));
        Blank();

        Header("Noncurrent Liabilities");
        Line("PPP Loan",                 2, -C("fin_level4", "PPP Loan"),                 -P("fin_level4", "PPP Loan"));
        Line("Note Payable",             2, -C("fin_level4", "Note Payable"),             -P("fin_level4", "Note Payable"));
        Line("Stockholder Note Payable", 2, -C("fin_level4", "Stockholder Note Payable"), -P("fin_level4", "Stockholder Note Payable"));
        Sub ("Note Payable",             1, -C("fin_level3", "Note Payable"),             -P("fin_level3", "Note Payable"));
        Sub ("LT Lease ROU Liability",   1, -C("fin_level3", "LT Lease ROU Liability"),   -P("fin_level3", "LT Lease ROU Liability"));
        Line("Unamortized Origination Costs", 2, -C("fin_level4", "Unamortized Origination Costs"), -P("fin_level4", "Unamortized Origination Costs"));
        Line("Deferred Revenue",         2, -C("fin_level4", "Deferred Revenue"),         -P("fin_level4", "Deferred Revenue"));
        Sub ("Other Noncurrent Liabilities", 1, -C("fin_level3", "Other Noncurrent Liabilities"), -P("fin_level3", "Other Noncurrent Liabilities"));
        Sub ("Total Noncurrent Liabilities", 0, -C("fin_level2", "Noncurrent Liabilities"), -P("fin_level2", "Noncurrent Liabilities"));
        Blank();

        var totalLiabC = -C("fin_level1", "Liability");
        var totalLiabP = -P("fin_level1", "Liability");
        Total("Total Liabilities", totalLiabC, totalLiabP);
        Blank();

        // EQUITY — flip sign like liabilities. Special handling for Dec: in
        // December the YTD Net Income has not yet been closed to Retained Earnings,
        // so we subtract YTD NI from the displayed RE balances to avoid double-counting.
        // (Matches the IF(MONTH(D10)=12, ...) branch in the Excel.)
        Header("EQUITY");
        var pyReA = -C("fin_level3", "PY Retained Earnings");
        var pyReB = -P("fin_level3", "PY Retained Earnings");
        if (periodA.Month == 12) pyReA -= aYtdNi;
        if (periodB.Month == 12) pyReB -= bYtdNi;
        Line("PY Retained Earnings", 1, pyReA, pyReB);
        Line("Distributions",        1, -C("fin_level3", "Distributions"), -P("fin_level3", "Distributions"));

        var reA = -C("fin_level2", "Retained Earnings");
        var reB = -P("fin_level2", "Retained Earnings");
        if (periodA.Month == 12) reA -= aYtdNi;
        if (periodB.Month == 12) reB -= bYtdNi;
        Sub("Retained Earnings", 0, reA, reB);

        Line("YTD Net Income", 0, aYtdNi, bYtdNi);
        Sub ("Common Stock",   0, -C("fin_level2", "Common Stock"), -P("fin_level2", "Common Stock"));

        // Total equity per the Excel = Retained Earnings + YTD NI + Common Stock.
        var totalEqA = reA + aYtdNi + (-C("fin_level2", "Common Stock"));
        var totalEqB = reB + bYtdNi + (-P("fin_level2", "Common Stock"));
        Total("Total Equity", totalEqA, totalEqB);

        // Check: Assets should equal Liabilities + Equity. Computed per period.
        var checkA = totalAssetsC - totalLiabC - totalEqA;
        var checkB = totalAssetsP - totalLiabP - totalEqB;
        return (rows, checkA, checkB);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static FinRow Row(string label, int indent, FinStyle style, decimal a, decimal b)
    {
        var v = a - b;
        decimal? pct = b == 0m ? null : v / Math.Abs(b);
        return new FinRow(label, indent, style, a, b, v, pct);
    }

    private static decimal ToDec(object? o) => o is null or DBNull ? 0m : Convert.ToDecimal(o);
    private static int ToInt(object? o) => o is null or DBNull ? 0 : Convert.ToInt32(o);
}
