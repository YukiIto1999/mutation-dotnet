using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>書き出し済みの報告から、変わった行に重なる変異の確定結果を取り出す use-case</summary>
/// <param name="reports">報告の読み戻しの port</param>
/// <param name="changes">変わった行の解決の port</param>
public sealed class JudgeChangedLines(IRecordedReports reports, IChangedLines changes)
{
    /// <summary>報告の読み込み、報告の root での差分の解決、変わった行に重なる変異の選別の遂行</summary>
    /// <param name="reportPath">mutation-report.json の path</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変わった行に重なる変異の確定結果、報告か差分を得られない、または報告が合否の根拠にならなければ理由</returns>
    public Result<ChangedLineVerdicts, PipelineFailure> Execute(string reportPath, string sinceRef)
    {
        var read = reports.Read(reportPath);
        if (read.IsFailed(out var readFailure, out var report))
        {
            return readFailure;
        }

        var resolved = changes.Resolve(report.ProjectRoot, sinceRef);
        if (resolved.IsFailed(out var diffFailure, out var changedLines))
        {
            return diffFailure;
        }

        var verdicts = ChangedLineVerdicts.Select(report.Mutants, changedLines, report.Unanalysed);
        return Indeterminate(reportPath, report.Run, verdicts) is { } failure
            ? Result.Failed<PipelineFailure>(failure)
            : Result.Succeeded(verdicts);
    }

    /// <summary>変わった行の合否を決められない理由。決められるなら不在</summary>
    /// <remarks>
    /// 未検出が一件でもあれば、他に何があっても合否は失敗と決まる
    /// それ以外では、判定を確定できなかった変異か、判定に至らなかった対象を残して止まった走行の報告が、合格と言えない理由になる
    /// この道具が走行の状態を書かなかった報告は、止まった走行と見なさない
    /// </remarks>
    private static PipelineFailure? Indeterminate(string reportPath, RecordedRun? run, ChangedLineVerdicts verdicts)
    {
        if (verdicts.Undetected.Count > 0)
        {
            return null;
        }

        if (verdicts.Unjudged.Count > 0)
        {
            return new PipelineFailure.UnjudgedOnChangedLines(verdicts.Unjudged.Count, verdicts.Unanalysed);
        }

        return run switch
        {
            RecordedRun.Failed failed => new PipelineFailure.ReportOfFailedRun(reportPath, failed.Targets),
            RecordedRun.Interrupted interrupted => new PipelineFailure.ReportOfInterruptedRun(reportPath, interrupted.Targets),
            RecordedRun.Finished or null => null,
        };
    }
}
