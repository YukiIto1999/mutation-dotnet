using Mutation.Domain;
using Mutation.Application;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Reports;

/// <summary>file 読み込みによる IRecordedReports の adapter</summary>
public sealed class RecordedReports : IRecordedReports
{
    /// <inheritdoc />
    public Result<RecordedReport, PipelineFailure> Read(string reportPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(reportPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result.Failed<PipelineFailure>(new PipelineFailure.ReportUnreadable(reportPath, exception.Message));
        }

        return StrykerReport.Parse(json, reportPath);
    }
}
