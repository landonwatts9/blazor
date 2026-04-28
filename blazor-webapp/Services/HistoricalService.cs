using Microsoft.Extensions.Caching.Memory;
using SamReporting.Models;

namespace SamReporting.Services;

public class HistoricalService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SqlService _sql;
    private readonly IMemoryCache _cache;

    public HistoricalService(SqlService sql, IMemoryCache cache)
    {
        _sql = sql;
        _cache = cache;
    }

    private const string ProductSql = @"
SELECT
    CASE
        WHEN loanchannel_summary = 'HECM'
             OR loan_program LIKE '%HECM%' OR loan_program LIKE '%Reverse%' THEN 'Reverse (HECM)'
        WHEN lien_position = 'SecondLien' OR loan_type = 'HELOC'
             OR loan_program LIKE '%HELOC%' OR loan_program LIKE '%Second%' THEN '2nds'
        WHEN loan_program LIKE '%Non-QM%' OR loan_program LIKE '%NonQM%'
             OR loan_program LIKE '%Non QM%' THEN 'Non-QM'
        WHEN jumbo_status = 'Jumbo' THEN 'Jumbo'
        WHEN loan_type IN ('FHA','VA','FarmersHomeAdministration')
            THEN 'Gov''t Agency (FHA, VA, USDA, etc.)'
        WHEN loan_type = 'Conventional'
             AND (jumbo_status = 'Conforming' OR jumbo_status IS NULL) THEN 'FNMA / FHLMC'
        ELSE 'Non-Agency Conforming'
    END AS product,
    COUNT(*) AS units,
    ISNULL(SUM(loanamount_fundedfinal), 0) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND funding_date >= @start AND funding_date <= @end
GROUP BY
    CASE
        WHEN loanchannel_summary = 'HECM'
             OR loan_program LIKE '%HECM%' OR loan_program LIKE '%Reverse%' THEN 'Reverse (HECM)'
        WHEN lien_position = 'SecondLien' OR loan_type = 'HELOC'
             OR loan_program LIKE '%HELOC%' OR loan_program LIKE '%Second%' THEN '2nds'
        WHEN loan_program LIKE '%Non-QM%' OR loan_program LIKE '%NonQM%'
             OR loan_program LIKE '%Non QM%' THEN 'Non-QM'
        WHEN jumbo_status = 'Jumbo' THEN 'Jumbo'
        WHEN loan_type IN ('FHA','VA','FarmersHomeAdministration')
            THEN 'Gov''t Agency (FHA, VA, USDA, etc.)'
        WHEN loan_type = 'Conventional'
             AND (jumbo_status = 'Conforming' OR jumbo_status IS NULL) THEN 'FNMA / FHLMC'
        ELSE 'Non-Agency Conforming'
    END";

    private const string PurposeSql = @"
SELECT
    CASE
        WHEN loan_purpose = 'Purchase' THEN 'Purchase'
        WHEN loan_purpose IN ('Cash-Out Refinance','NoCash-Out Refinance') THEN 'Refi'
        ELSE 'Other'
    END AS purpose,
    COUNT(*) AS units,
    ISNULL(SUM(loanamount_fundedfinal), 0) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND funding_date >= @start AND funding_date <= @end
GROUP BY
    CASE
        WHEN loan_purpose = 'Purchase' THEN 'Purchase'
        WHEN loan_purpose IN ('Cash-Out Refinance','NoCash-Out Refinance') THEN 'Refi'
        ELSE 'Other'
    END";

    private const string ChannelSql = @"
SELECT
    CASE
        WHEN loanchannel_summary = 'Retail' THEN 'Retail'
        WHEN loanchannel_summary = 'HECM' THEN 'Retail'
        WHEN loanchannel_summary = 'Brokered' THEN 'Wholesale'
        ELSE 'Correspondent'
    END AS channel,
    COUNT(*) AS units,
    ISNULL(SUM(loanamount_fundedfinal), 0) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND funding_date >= @start AND funding_date <= @end
GROUP BY
    CASE
        WHEN loanchannel_summary = 'Retail' THEN 'Retail'
        WHEN loanchannel_summary = 'HECM' THEN 'Retail'
        WHEN loanchannel_summary = 'Brokered' THEN 'Wholesale'
        ELSE 'Correspondent'
    END";

    private const string SalesTxnSql = @"
SELECT
    CASE
        WHEN sellside_investorlocktype = 'Mandatory' THEN 'Bulk'
        WHEN sellside_investorlocktype = 'Best Efforts' THEN 'Flow'
        ELSE 'Flow'
    END AS txn_type,
    COUNT(*) AS units,
    ISNULL(SUM(loanamount_fundedfinal), 0) AS volume
FROM dbo.EncompassLoan_Gold
WHERE funded_flag = 1
  AND loanchannel_summary <> 'Brokered'
  AND sellside_investordeliverydate >= @start AND sellside_investordeliverydate <= @end
GROUP BY
    CASE
        WHEN sellside_investorlocktype = 'Mandatory' THEN 'Bulk'
        WHEN sellside_investorlocktype = 'Best Efforts' THEN 'Flow'
        ELSE 'Flow'
    END";

    private async Task<Dictionary<string, Bucket>> FetchBucket(string sql, string keyCol, DateTime start, DateTime end)
    {
        var rows = await _sql.QueryAsync(sql,
            r => (Key: r.GetString(0), Units: Convert.ToInt64(r.GetValue(1)), Volume: Convert.ToDecimal(r.GetValue(2))),
            ("@start", start), ("@end", end));
        return rows.ToDictionary(x => x.Key, x => new Bucket(x.Units, x.Volume));
    }

    public async Task<HistoricalBreakdown> FetchAsync(DateTime start, DateTime end)
    {
        var product = await FetchBucket(ProductSql, "product", start, end);
        var purpose = await FetchBucket(PurposeSql, "purpose", start, end);
        var channel = await FetchBucket(ChannelSql, "channel", start, end);
        var sales = await FetchBucket(SalesTxnSql, "txn_type", start, end);
        return new HistoricalBreakdown
        {
            Product = product,
            Purpose = purpose,
            Channel = channel,
            SalesTxn = sales,
        };
    }

    public Task<HistoricalResponse> FetchFullAsync(
        DateTime prodStart, DateTime prodEnd,
        DateTime? priorStart, DateTime? priorEnd,
        DateTime? salesStart, DateTime? salesEnd,
        DateTime? salesPriorStart, DateTime? salesPriorEnd)
    {
        var key = $"historical:{prodStart:yyyyMMdd}:{prodEnd:yyyyMMdd}:" +
                  $"{priorStart:yyyyMMdd}:{priorEnd:yyyyMMdd}:" +
                  $"{salesStart:yyyyMMdd}:{salesEnd:yyyyMMdd}:" +
                  $"{salesPriorStart:yyyyMMdd}:{salesPriorEnd:yyyyMMdd}";
        return _cache.GetOrCreateAsync(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return FetchFullCore(prodStart, prodEnd, priorStart, priorEnd, salesStart, salesEnd, salesPriorStart, salesPriorEnd);
        })!;
    }

    private async Task<HistoricalResponse> FetchFullCore(
        DateTime prodStart, DateTime prodEnd,
        DateTime? priorStart, DateTime? priorEnd,
        DateTime? salesStart, DateTime? salesEnd,
        DateTime? salesPriorStart, DateTime? salesPriorEnd)
    {
        salesStart ??= prodStart;
        salesEnd ??= prodEnd;

        var current = await FetchAsync(prodStart, prodEnd);
        var currentSales = await FetchAsync(salesStart.Value, salesEnd.Value);

        HistoricalBreakdown? prior = null;
        if (priorStart.HasValue && priorEnd.HasValue)
            prior = await FetchAsync(priorStart.Value, priorEnd.Value);

        HistoricalBreakdown? priorSales = null;
        if (salesPriorStart.HasValue && salesPriorEnd.HasValue)
            priorSales = await FetchAsync(salesPriorStart.Value, salesPriorEnd.Value);

        return new HistoricalResponse
        {
            Current = current,
            Prior = prior,
            CurrentSales = currentSales,
            PriorSales = priorSales,
        };
    }
}
