using Mutation.Domain;



namespace Mutation.Cli;

/// <summary>想定内の失敗から端末へ出す説明への変換</summary>
public static class FailureLines
{
    /// <summary>一つの失敗の、人が読む説明への変換</summary>
    /// <param name="failure">実行を中断した想定内の失敗</param>
    /// <returns>端末へ出す説明</returns>
    public static string Describe(PipelineFailure failure) =>
        failure switch
        {
            PipelineFailure.BuildFailed failed => $"dotnet build が失敗した:\n{failed.Detail}",
            PipelineFailure.CompilerArgumentsNotFound notFound =>
                $"binlog に {notFound.ProjectPath} の csc 呼び出しが見つからない",
            PipelineFailure.CompileFailed compileFailed => $"変異コンパイルが失敗した:\n{compileFailed.Diagnostics}",
            PipelineFailure.WorkerFailed workerFailed => $"テスト worker が失敗した: {workerFailed.Reason}",
            PipelineFailure.SinceUnavailable since => $"--since の差分を解決できない: {since.Reason}",
            PipelineFailure.NoTestsFound noTests => $"{noTests.TestAssembly} からテストが見つからない",
            PipelineFailure.ReportUnreadable unreadable =>
                $"{unreadable.ReportPath} を変異検査の報告として読めない: {unreadable.Reason}",
            PipelineFailure.Interrupted => "取り消しを受けて止まった",
            PipelineFailure.UnjudgedOnChangedLines unjudged => Unjudged(unjudged),
            PipelineFailure.ReportOfFailedRun failedRun =>
                $"{failedRun.ReportPath} は判定に至らなかった対象を残して失敗した走行の報告で、変わった行の合否の根拠にならない{Targets(failedRun.Targets)}。失敗の原因を直して `run` をやり直す",
            PipelineFailure.ReportOfInterruptedRun interruptedRun =>
                $"{interruptedRun.ReportPath} は取り消しを受けて止まった走行の報告で、変わった行の合否の根拠にならない{Targets(interruptedRun.Targets)}。`run --with-baseline` で全対象の判定を確定させる",
        };

    /// <summary>変わった行の判定を確定できなかった失敗の説明。未分析の影響範囲があれば、判定がそれを含まないことも添える</summary>
    private static string Unjudged(PipelineFailure.UnjudgedOnChangedLines unjudged)
    {
        var headline =
            $"変わった行の変異 {unjudged.Count} 件の判定が確定しておらず、未検出の有無を決められない(途中で止まった走行か、競合で遅れた timeout か、test runner の失敗の報告)";
        return unjudged.Unanalysed.Count == 0
            ? headline
            : $"{headline}\n{ChangedLineSummary.OmittedImpact(unjudged.Unanalysed)}";
    }

    /// <summary>根拠にならない報告の走行で、判定に至らなかった対象の添え書き。対象がなければ空</summary>
    private static string Targets(IReadOnlyList<string> targets) =>
        targets.Count == 0 ? "" : $"(判定に至らなかった対象: {string.Join(", ", targets)})";
}
