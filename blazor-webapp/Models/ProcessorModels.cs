namespace SamReporting.Models;

public record ProcessorPipelineRow(
    long LoanNumber,
    string BorrowerName,
    string? UwCurrentStatus,
    DateTime? UwCurrentStatusDate,
    string? ProcessorName,
    string? LoanOfficerName,
    DateTime? EstimatedClosingDate);
