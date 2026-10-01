using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>書き出し済みの報告から、変わった行に重なる変異の確定結果を取り出す use-case</summary>
/// <param name="reports">報告の読み戻しの port</param>
/// <param name="changes">変わった行の解決の port</param>
public sealed class JudgeChangedLines(IRecordedReports reports, IChangedLines changes)
{
    /// <summary>報告の読み込み、報告の root での差分の解決、変わった行に重なる変異の選別の遂行</summary>
    /// <param name="reportPath">mutation-report.json の path</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変わった行に重なる変異の確定結果、報告か差分を得られなければ理由</returns>
    public Result<ChangedLineVerdicts, PipelineFailure> Execute(string reportPath, string sinceRef)
    {
        var read = reports.Read(reportPath);
        if (read is Result<RecordedReport, PipelineFailure>.Failed(var readFailure))
        {
            return new Result<ChangedLineVerdicts, PipelineFailure>.Failed(readFailure);
        }

        var report = ((Result<RecordedReport, PipelineFailure>.Succeeded)read).Value;
        var resolved = changes.Resolve(report.ProjectRoot, sinceRef);
        if (resolved is Result<ChangedLines, PipelineFailure>.Failed(var diffFailure))
        {
            return new Result<ChangedLineVerdicts, PipelineFailure>.Failed(diffFailure);
        }

        return new Result<ChangedLineVerdicts, PipelineFailure>.Succeeded(
            ChangedLineVerdicts.Select(report.Mutants, ((Result<ChangedLines, PipelineFailure>.Succeeded)resolved).Value)
        );
    }
}
