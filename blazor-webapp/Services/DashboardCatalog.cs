namespace SamReporting.Services;

public record DashboardEntry(string Key, string Title, string Description, string Route);

/// <summary>
/// Single source of truth for what dashboards exist. Adding a new dashboard
/// means adding an entry here AND granting users access via the
/// dbo.DashboardAccess table (key matches the row's dashboard_key column).
/// </summary>
public static class DashboardCatalog
{
    public static readonly DashboardEntry Monthly = new(
        Key: "monthly",
        Title: "Monthly Dashboard",
        Description: "MTD funded volume, projected pipeline by channel, turn times, LO summary.",
        Route: "/monthly");

    public static readonly DashboardEntry Historical = new(
        Key: "historical",
        Title: "Historical Production",
        Description: "Historical funded production analysis with prior-period comparisons.",
        Route: "/historical");

    public static readonly DashboardEntry ProcessorPipeline = new(
        Key: "processor",
        Title: "Processor Pipeline",
        Description: "My Pipeline loans by underwriting status.",
        Route: "/processor");

    public static readonly DashboardEntry Originations = new(
        Key: "originations",
        Title: "Originations",
        Description: "Loan officer production with CW/MTD/YTD breakdown plus channel, funnel, leaderboard, and trend.",
        Route: "/originations");

    public static readonly IReadOnlyList<DashboardEntry> All =
        new[] { Monthly, Historical, ProcessorPipeline, Originations };
}
