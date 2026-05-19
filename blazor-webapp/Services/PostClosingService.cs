using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class PostClosingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public PostClosingService(SqlService sql, IMemoryCache cache)
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

    // KPIs:
    //  - "Last 30 days" KPIs are date-windowed via @start/@end on shipping_date or purchased_date
    //  - Lock-risk KPIs are a *now* snapshot of currently shipped-not-purchased loans, so they
    //    ignore the date window. Lock-type and channel filters apply to both groups.
    private const string KpisSql = @"
SELECT
    SUM(CASE WHEN shipping_date >= @start AND shipping_date < @end THEN 1 ELSE 0 END) AS shipped_window,
    AVG(CASE WHEN shipping_date >= @start AND shipping_date < @end AND turntime_funding_to_shipping IS NOT NULL
             THEN CAST(turntime_funding_to_shipping AS FLOAT) END) AS avg_fund_ship,
    SUM(CASE WHEN purchased_date >= @start AND purchased_date < @end THEN 1 ELSE 0 END) AS purchased_window,
    AVG(CASE WHEN purchased_date >= @start AND purchased_date < @end AND turntime_shipping_to_purchase IS NOT NULL
             THEN CAST(turntime_shipping_to_purchase AS FLOAT) END) AS avg_ship_purchase,
    SUM(CASE WHEN shipping_date IS NOT NULL AND purchased_date IS NULL
              AND sellside_lockexpirationdate IS NOT NULL
              AND sellside_lockexpirationdate < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END) AS lock_expired,
    SUM(CASE WHEN shipping_date IS NOT NULL AND purchased_date IS NULL
              AND sellside_lockexpirationdate IS NOT NULL
              AND sellside_lockexpirationdate >= CAST(GETDATE() AS DATE)
              AND sellside_lockexpirationdate <= DATEADD(DAY, 7, CAST(GETDATE() AS DATE)) THEN 1 ELSE 0 END) AS lock_expiring
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND loanchannel_summary <> 'Brokered'
  AND (@lockType = 'All' OR sellside_investorlocktype = @lockType)
  AND (@channel  = 'All' OR loanchannel_summary       = @channel)";

    // Business-day counts come from dbo.DIM_Dates.relativebusinessday — a running
    // counter that increments only on business days (excludes weekends AND holidays).
    // Subtract two values to get business-days-between. @today_brd is the value for
    // CAST(GETDATE() AS DATE), captured once per query.

    private const string FundedNotShippedSql = @"
DECLARE @today_brd int = (SELECT relativebusinessday FROM dbo.DIM_Dates WHERE date = CAST(GETDATE() AS DATE));

SELECT g.loan_number, g.borrower_lastname, g.investor_name, g.funding_date,
       DATEDIFF(DAY, g.funding_date, CAST(GETDATE() AS DATE)) AS days_since_funding,
       @today_brd - df.relativebusinessday AS bus_days_since_funding
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_Dates df ON df.date = g.funding_date
WHERE g.funded_flag = 1
  AND g.loanchannel_summary <> 'Brokered'
  AND g.funding_date IS NOT NULL
  AND g.shipping_date IS NULL
  AND g.purchased_date IS NULL
  AND (@lockType = 'All' OR g.sellside_investorlocktype = @lockType)
  AND (@channel  = 'All' OR g.loanchannel_summary       = @channel)
ORDER BY g.funding_date DESC";

    private const string ShippedNotPurchasedSql = @"
DECLARE @today_brd int = (SELECT relativebusinessday FROM dbo.DIM_Dates WHERE date = CAST(GETDATE() AS DATE));

SELECT g.loan_number, g.borrower_lastname, g.investor_name,
       g.sellside_investorlocktype AS lock_type,
       g.loanchannel_summary AS channel,
       g.sellside_lockdate AS investor_lock_date,
       g.shipping_date,
       g.sellside_lockexpirationdate AS lock_expiration_date,
       DATEDIFF(DAY, g.shipping_date, CAST(GETDATE() AS DATE)) AS days_since_shipped,
       @today_brd - ds.relativebusinessday AS bus_days_since_shipped,
       CASE WHEN g.sellside_lockexpirationdate IS NULL THEN NULL
            ELSE DATEDIFF(DAY, CAST(GETDATE() AS DATE), g.sellside_lockexpirationdate) END AS days_until_lock_expires,
       g.loanamount_fundedfinal AS funded_amount
FROM dbo.EncompassLoan_Gold g
LEFT JOIN dbo.DIM_Dates ds ON ds.date = g.shipping_date
WHERE g.funded_flag = 1
  AND g.loanchannel_summary <> 'Brokered'
  AND g.shipping_date IS NOT NULL
  AND g.purchased_date IS NULL
  AND (@lockType = 'All' OR g.sellside_investorlocktype = @lockType)
  AND (@channel  = 'All' OR g.loanchannel_summary       = @channel)
ORDER BY g.shipping_date";

    public Task<PostClosingResponse> GetAsync(int windowDays, string lockType, string channel) =>
        Cached(
            $"postclosing:{windowDays}:{lockType}:{channel}",
            () => FetchAsync(windowDays, lockType, channel));

    private async Task<PostClosingResponse> FetchAsync(int windowDays, string lockType, string channel)
    {
        var end = DateTime.Today.AddDays(1); // [start, end) — include today
        var start = end.AddDays(-windowDays);
        var p = new (string, object?)[]
        {
            ("@start", start),
            ("@end", end),
            ("@lockType", lockType),
            ("@channel", channel),
        };

        var kpiTask = _sql.QueryAsync(KpisSql, p);
        var fundedTask = _sql.QueryAsync(FundedNotShippedSql,
            r => new FundedNotShippedRow(
                LoanNumber: Convert.ToInt64(r["loan_number"]),
                BorrowerLastname: r["borrower_lastname"] as string,
                InvestorName: r["investor_name"] as string,
                FundingDate: r["funding_date"] as DateTime?,
                DaysSinceFunding: r["days_since_funding"] is DBNull ? 0 : Convert.ToInt32(r["days_since_funding"]),
                BusinessDaysSinceFunding: r["bus_days_since_funding"] is DBNull ? null : Convert.ToInt32(r["bus_days_since_funding"])),
            p);
        var shippedTask = _sql.QueryAsync(ShippedNotPurchasedSql,
            r => new ShippedNotPurchasedRow(
                LoanNumber: Convert.ToInt64(r["loan_number"]),
                BorrowerLastname: r["borrower_lastname"] as string,
                InvestorName: r["investor_name"] as string,
                LockType: r["lock_type"] as string,
                Channel: r["channel"] as string,
                InvestorLockDate: r["investor_lock_date"] as DateTime?,
                ShippingDate: r["shipping_date"] as DateTime?,
                LockExpirationDate: r["lock_expiration_date"] as DateTime?,
                DaysSinceShipped: r["days_since_shipped"] is DBNull ? 0 : Convert.ToInt32(r["days_since_shipped"]),
                BusinessDaysSinceShipped: r["bus_days_since_shipped"] is DBNull ? null : Convert.ToInt32(r["bus_days_since_shipped"]),
                DaysUntilLockExpires: r["days_until_lock_expires"] is DBNull ? null : Convert.ToInt32(r["days_until_lock_expires"]),
                FundedAmount: r["funded_amount"] as decimal?),
            p);

        await Task.WhenAll(kpiTask, fundedTask, shippedTask);

        var k = kpiTask.Result.Single();
        var kpis = new PostClosingKpis(
            ShippedLast30: k["shipped_window"] is DBNull ? 0 : Convert.ToInt32(k["shipped_window"]),
            AvgFundToShipDays: ToDouble(k["avg_fund_ship"]),
            PurchasedLast30: k["purchased_window"] is DBNull ? 0 : Convert.ToInt32(k["purchased_window"]),
            AvgShipToPurchaseDays: ToDouble(k["avg_ship_purchase"]),
            LockExpiredCount: k["lock_expired"] is DBNull ? 0 : Convert.ToInt32(k["lock_expired"]),
            LockExpiringSoonCount: k["lock_expiring"] is DBNull ? 0 : Convert.ToInt32(k["lock_expiring"]));

        return new PostClosingResponse(kpis, fundedTask.Result, shippedTask.Result);
    }

    private static double? ToDouble(object? o) =>
        o is null or DBNull ? null : Math.Round(Convert.ToDouble(o), 2);
}
