namespace SamReporting.Models;

public record OriginationKpis(
    int CwUnits,
    decimal CwVolume,
    int MtdUnits,
    decimal MtdVolume);

public record LoProductionRow(
    string LoanOfficer,
    int CwUnits,
    decimal CwDollars,
    int MtdUnits,
    decimal MtdDollars,
    int YtdUnits,
    decimal YtdDollars,
    int MtdFundedUnits,
    decimal MtdFundedDollars,
    int YtdFundedUnits,
    decimal YtdFundedDollars);

public record OriginationsLanding(
    OriginationKpis Kpis,
    IReadOnlyList<LoProductionRow> Rows);

public record MixBucket(string Label, int OriginationUnits, decimal OriginationDollars, int FundedUnits, decimal FundedDollars);

public record ChannelPurposeMix(
    IReadOnlyList<MixBucket> ByChannel,
    IReadOnlyList<MixBucket> ByPurpose);

public record FunnelStage(string Label, int Count, double PctOfApps);

public record LeaderboardRow(
    int Rank,
    string LoanOfficer,
    int Units,
    decimal Volume,
    int UnitsPriorYear,
    decimal VolumePriorYear)
{
    public int UnitsDelta => Units - UnitsPriorYear;
    public decimal VolumeDelta => Volume - VolumePriorYear;
}

public record MonthlyTrendPoint(string Month, int OriginationUnits, int FundedUnits);
