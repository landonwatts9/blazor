namespace SamReporting.Models;

public record PostClosingKpis(
    int ShippedLast30,
    double? AvgFundToShipDays,
    int PurchasedLast30,
    double? AvgShipToPurchaseDays,
    int LockExpiredCount,
    int LockExpiringSoonCount);

public record FundedNotShippedRow(
    long LoanNumber,
    string? BorrowerLastname,
    string? InvestorName,
    DateTime? FundingDate,
    int DaysSinceFunding,
    int? BusinessDaysSinceFunding);

public record ShippedNotPurchasedRow(
    long LoanNumber,
    string? BorrowerLastname,
    string? InvestorName,
    string? LockType,
    string? Channel,
    DateTime? InvestorLockDate,
    DateTime? ShippingDate,
    DateTime? LockExpirationDate,
    int DaysSinceShipped,
    int? BusinessDaysSinceShipped,
    int? DaysUntilLockExpires,
    decimal? FundedAmount);

public record InvestorRollupRow(
    string InvestorName,
    int LoanCount,
    int MandatoryCount,
    decimal TotalFundedAmount,
    int OldestDaysSinceShipped,
    int LockExpiredCount);

public record PostClosingResponse(
    PostClosingKpis Kpis,
    IReadOnlyList<FundedNotShippedRow> FundedNotShipped,
    IReadOnlyList<ShippedNotPurchasedRow> ShippedNotPurchased);
