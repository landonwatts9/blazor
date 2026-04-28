namespace SamReporting.Models;

public record MonthlyKpis(
    int FundedUnits,
    decimal FundedVolume,
    int ProjectedUnits,
    decimal ProjectedVolume)
{
    public int TotalUnits => FundedUnits + ProjectedUnits;
    public decimal TotalVolume => FundedVolume + ProjectedVolume;
}

public record DailyProjection(int Day, int Retail, int HECM, int Brokered, decimal Volume)
{
    public int Units => Retail + HECM + Brokered;
}

public record MilestoneBucket(string Milestone, int Retail, int HECM, int Brokered, decimal Volume, int SortOrder)
{
    public int Units => Retail + HECM + Brokered;
}

public record PipelineAgingRow(
    string Milestone,
    string Confidence,
    int Units,
    decimal Volume,
    double AvgDays,
    int MinDays,
    int MaxDays,
    int StaleCount,
    IReadOnlyList<PipelineLoanRow> Loans);

public record PipelineLoanRow(
    long LoanNumber,
    string? BorrowerLastname,
    string? LoanOfficer,
    string? Processor,
    string? Channel,
    string? Purpose,
    decimal? LoanAmount,
    DateTime? EstimatedClosingDate,
    int DaysInMilestone);

public record PipelineHealth(
    IReadOnlyList<PipelineAgingRow> Milestones,
    ConfidenceTotals Tiers,
    int StaleCount);

public record ConfidenceTotals(
    int HighUnits, decimal HighVolume,
    int MediumUnits, decimal MediumVolume,
    int LowUnits, decimal LowVolume);

public record TurnTimes(
    double? AppToFund,
    double? LockToFund,
    double? UwToStc,
    double? StcToDocs,
    int LoanCount,
    double? AppToFund3Mo,
    double? LockToFund3Mo,
    double? UwToStc3Mo,
    double? StcToDocs3Mo);

public record TurnTimeSegment(string Label, double Days);

public record TurnTimeTrendPoint(string Month, double? AppToFund, double? LockToFund);

public record ChannelPurposeBreakdown(
    IReadOnlyList<BreakdownBucket> Channels,
    IReadOnlyList<BreakdownBucket> Purposes);

public record BreakdownBucket(string Label, int Units, decimal Volume);

public record LoanOfficerRow(
    string LoanOfficer,
    int FundedUnits,
    decimal FundedVolume,
    int ProjectedUnits,
    decimal ProjectedVolume)
{
    public int TotalUnits => FundedUnits + ProjectedUnits;
    public decimal TotalVolume => FundedVolume + ProjectedVolume;
}

public record FundedLoanDetail(
    long LoanNumber,
    string? BorrowerLastname,
    string? LoanOfficer,
    string? Channel,
    string? Purpose,
    decimal? LoanAmount,
    decimal? InterestRate,
    DateTime? FundingDate);

public record ProjectedLoanDetail(
    long LoanNumber,
    string? BorrowerLastname,
    string? LoanOfficer,
    string? Channel,
    string? Purpose,
    string? MilestoneStatus,
    decimal? LoanAmount,
    DateTime? EstimatedClosingDate);

public record MonthlySummary(
    MonthlyKpis Kpis,
    IReadOnlyList<DailyProjection> ProjectedByDay,
    IReadOnlyList<MilestoneBucket> ProjectedByMilestone,
    ChannelPurposeBreakdown ChannelPurpose);

public record TurnTimeBundle(
    TurnTimes Current,
    IReadOnlyList<TurnTimeSegment> Waterfall,
    IReadOnlyList<TurnTimeTrendPoint> Trend);

public record LoanDetailBundle(
    IReadOnlyList<FundedLoanDetail> Funded,
    IReadOnlyList<ProjectedLoanDetail> Projected);
