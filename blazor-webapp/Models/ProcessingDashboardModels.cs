namespace SamReporting.Models;

public record UwTurnTimeRow(
    long LoanNumber,
    string BorrowerName,
    string? UwStatus,
    DateTime? UwStatusDate,
    string? ProcessorName,
    string? LoanOfficerName,
    DateTime? ExpectedSigningDate,
    DateTime? EstClosingDate);

public record CdTurnTimeRow(
    long LoanNumber,
    string BorrowerName,
    string? CdStatus,
    DateTime? EarlyCdAuditDate,
    DateTime? CdAuditDate,
    string? ProcessorName,
    string? LoanOfficerName,
    DateTime? ExpectedSigningDate,
    DateTime? EstClosingDate);

public record ClosingDocsTurnTimeRow(
    long LoanNumber,
    string BorrowerName,
    string? LastMilestone,
    DateTime? LastMilestoneDate,
    string? ProcessorName,
    string? LoanOfficerName,
    DateTime? ExpectedSigningDate,
    DateTime? EstClosingDate);

public record FundingTurnTimeRow(
    long LoanNumber,
    string BorrowerName,
    string? ProcessorName,
    string? LoanOfficerName,
    DateTime? ExpectedSigningDate,
    DateTime? EstClosingDate);
