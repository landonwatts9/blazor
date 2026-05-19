namespace SamReporting.Models;

/// <summary>
/// Row presentation style for the financial statements.
/// Header = section break (REVENUE, LIABILITIES, etc.).
/// Line = a leaf account/line item.
/// Subtotal = a roll-up inside a section (e.g. Personnel Costs total).
/// Total = a top-level total (Net Income, Total Assets).
/// Blank = a visual gap.
/// </summary>
public enum FinStyle { Header, Line, Subtotal, Total, Blank }

/// <summary>
/// A single row in either the income statement or balance sheet output.
/// All values are already display-signed (revenue/income totals positive,
/// expenses positive). Variance is A - B.
/// </summary>
public record FinRow(
    string Label,
    int Indent,
    FinStyle Style,
    decimal ValueA,
    decimal ValueB,
    decimal Variance,
    decimal? VariancePct);

public record ClosedPeriod(DateOnly PeriodEnd, DateTime ClosedAtUtc, string? ClosedBy, string? Notes);

public record IncomeStatementResult(
    DateOnly PeriodA,
    DateOnly PeriodB,
    int LoanCountA,
    decimal LoanVolumeA,
    int LoanCountB,
    decimal LoanVolumeB,
    IReadOnlyList<FinRow> Lines,
    IReadOnlyList<FinRow> BpsLines);

public record BalanceSheetResult(
    DateOnly PeriodA,
    DateOnly PeriodB,
    IReadOnlyList<FinRow> Lines,
    decimal CheckA,
    decimal CheckB);

/// <summary>
/// Trending IS: same row structure as IncomeStatementResult but with N
/// periods (typically 12 most recent closed months ending at Anchor).
/// Values arrays in Lines / LoanCounts / LoanVolumes are all parallel to Months.
/// </summary>
public record TrendingIncomeStatementResult(
    DateOnly Anchor,
    IReadOnlyList<DateOnly> Months,
    IReadOnlyList<int> LoanCounts,
    IReadOnlyList<decimal> LoanVolumes,
    IReadOnlyList<TrendingFinRow> Lines);

public record TrendingFinRow(
    string Label,
    int Indent,
    FinStyle Style,
    IReadOnlyList<decimal> Values);

// ─── Expense Detail ─────────────────────────────────────────────────────────

/// <summary>
/// Expense drilldown grouped by fin_level4 category: Direct Loan Costs,
/// Personnel Costs, G&A. Each category contains subcategory rows; each
/// subcategory has eager-loaded vendor detail. Transaction lines are fetched
/// lazily on click.
/// </summary>
public record ExpenseDetailResult(
    DateOnly PeriodA,
    DateOnly PeriodB,
    IReadOnlyList<ExpenseCategoryGroup> Categories);

public record ExpenseCategoryGroup(
    string Category,
    decimal ValueA,
    decimal ValueB,
    decimal Variance,
    decimal? VariancePct,
    IReadOnlyList<ExpenseSubcategoryRow> Subcategories);

public record ExpenseSubcategoryRow(
    string Category,
    string Label,
    decimal ValueA,
    decimal ValueB,
    decimal Variance,
    decimal? VariancePct,
    int VendorCountA,
    int VendorCountB,
    IReadOnlyList<ExpenseVendorRow> Vendors);

public record ExpenseVendorRow(
    string Category,
    string Subcategory,
    string Vendor,
    decimal ValueA,
    decimal ValueB,
    decimal Variance,
    int TxnCountA,
    int TxnCountB);

public record ExpenseTransactionRow(
    DateOnly PostDate,
    long JournalNumber,
    int GlAccount,
    string? AccountDesc,
    string? Branch,
    string? Memo,
    decimal Amount);
