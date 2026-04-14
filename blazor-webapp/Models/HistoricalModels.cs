namespace SamReporting.Models;

public record Bucket(long Units, decimal Volume)
{
    public static Bucket Empty => new(0, 0m);
}

public class HistoricalBreakdown
{
    public Dictionary<string, Bucket> Product { get; set; } = new();
    public Dictionary<string, Bucket> Purpose { get; set; } = new();
    public Dictionary<string, Bucket> Channel { get; set; } = new();
    public Dictionary<string, Bucket> SalesTxn { get; set; } = new();
}

public class HistoricalResponse
{
    public HistoricalBreakdown Current { get; set; } = new();
    public HistoricalBreakdown? Prior { get; set; }
    public HistoricalBreakdown CurrentSales { get; set; } = new();
    public HistoricalBreakdown? PriorSales { get; set; }
}
