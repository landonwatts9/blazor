using System.Data;
using SamReporting.Models;

namespace SamReporting.Services;

public class ProcessorService
{
    private readonly SqlService _sql;

    public ProcessorService(SqlService sql) => _sql = sql;

    private const string MyPipelineSql = @"
SELECT
    loan_number,
    LTRIM(RTRIM(CONCAT(borrower_firstname, ' ', borrower_lastname))) AS borrower_name,
    uw_currentstatus,
    current_underwritingstatus_date,
    processor_name,
    loanofficer_name,
    estimatedclosing_date
FROM vw_EncompassLoan_Silver
WHERE currentloanfolder = 'My Pipeline'
  AND uw_currentstatus IN ('To Be Assigned','In Review','Assigned','Submitted for Final Approval')
ORDER BY estimatedclosing_date, loan_number";

    public Task<List<ProcessorPipelineRow>> GetMyPipelineAsync() =>
        _sql.QueryAsync(MyPipelineSql, Map);

    private static ProcessorPipelineRow Map(IDataRecord r) => new(
        LoanNumber: Convert.ToInt64(r["loan_number"]),
        BorrowerName: r["borrower_name"] as string ?? string.Empty,
        UwCurrentStatus: r["uw_currentstatus"] as string,
        UwCurrentStatusDate: r["current_underwritingstatus_date"] as DateTime?,
        ProcessorName: r["processor_name"] as string,
        LoanOfficerName: r["loanofficer_name"] as string,
        EstimatedClosingDate: r["estimatedclosing_date"] as DateTime?);
}
