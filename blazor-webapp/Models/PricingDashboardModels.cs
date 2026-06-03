namespace SamReporting.Models;

public record PricingFilter(
    DateTime Start,
    DateTime End,
    IReadOnlyList<int>? NmlsIds,
    string? CaptureFilter,
    string? Channel)
{
    /// <summary>Stable string for cache keys.</summary>
    public string Key()
    {
        var ids = NmlsIds is null || NmlsIds.Count == 0 ? "all" : string.Join(",", NmlsIds.OrderBy(x => x));
        return $"{Start:yyyyMMdd}:{End:yyyyMMdd}:{CaptureFilter ?? "*"}:{Channel ?? "*"}:{ids}";
    }
}

public record PricingKpis(
    int Locks,
    decimal Volume,
    double AvgMarginBps,
    double AvgRate,
    decimal AvgConcessions,
    double CaptureRatePct);

public record PricingLoanRow(
    long LoanNumber,
    string BorrowerName,
    DateTime? LockDate,
    string? LoName,
    string? Channel,
    string? CommitmentType,
    bool WholeLoan,
    string? Captured,
    decimal? InterestRate,
    decimal? BuyPrice,
    decimal? SellPrice,
    double? MarginBps,
    decimal? Concessions,
    decimal? LoanAmount);

public record LoPricingSummary(
    string LoName,
    int Units,
    decimal Volume,
    double? AvgRate,
    double? AvgMarginBps,
    decimal AvgConcessions,
    double CaptureRatePct,
    int CapturedUnits,
    int NonCapturedUnits);

public record PricingTrendPoint(
    string Month,
    string LoName,
    int Units,
    decimal Volume,
    double? AvgMarginBps,
    double? AvgRate);

public record LoOption(int NmlsId, string DisplayName);
