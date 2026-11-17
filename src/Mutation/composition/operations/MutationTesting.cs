using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Composition;

/// <summary>外の設定を検査の workflow と use-case の語彙へ写して呼ぶだけの、外部へ公開する変異検査の operation</summary>
/// <param name="workflow">一連の変異検査の workflow</param>
/// <param name="judgeChangedLines">変わった行に重なる変異の取り出しの use-case</param>
public sealed class MutationTesting(RunMutationTesting workflow, JudgeChangedLines judgeChangedLines)
{
    /// <summary>一回の変異検査の遂行</summary>
    /// <remarks>被覆する全テストの実行でしか得られない検出行列のための前回の判定の継承と再利用の停止</remarks>
    /// <param name="options">外から受け取った設定</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <param name="cancellation">取り消しの要求。要求を受けた走行は、判定を確定した変異を報告に残して止まる</param>
    /// <returns>成功なら確定結果、失敗なら中断理由</returns>
    public Task<Result<MutationRunResult, PipelineFailure>> RunAsync(
        MutationRunOptions options,
        Action<RunProgress> progress,
        CancellationToken cancellation = default
    )
    {
        var withBaseline = options.WithBaseline && !options.DetectionMatrix;
        return workflow.ExecuteAsync(
            new RunRequest(
                options
                    .Targets.Select(t => new TargetRequest(t.ProjectPath, t.TestProjectPath, options.Configuration))
                    .ToArray(),
                new SelectionRequest(
                    options.MutatePatterns,
                    options.SinceRef,
                    options.ChangedLinesOnly,
                    options.IgnoredOperators,
                    options.IgnoredMethods
                ),
                new ExecutionSettings(
                    options.Concurrency,
                    options.ValidateSurvivors,
                    options.ExcludeStatic,
                    options.DetectionMatrix
                ),
                options.FingerprintSettings(),
                options.OutputDirectory,
                withBaseline
            ),
            progress,
            cancellation
        );
    }

    /// <summary>書き出し済みの報告からの、変わった行に重なる変異の確定結果の取り出し</summary>
    /// <param name="reportPath">mutation-report.json の path</param>
    /// <param name="sinceRef">差分の基点になる git の参照。`run` の `--since` と同じ意味</param>
    /// <returns>成功なら変わった行に重なる変異の確定結果、報告か差分を得られなければ理由</returns>
    public Result<ChangedLineVerdicts, PipelineFailure> OnChangedLines(string reportPath, string sinceRef) =>
        judgeChangedLines.Execute(reportPath, sinceRef);
}
