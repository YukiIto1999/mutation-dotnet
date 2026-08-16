using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Composition;

/// <summary>外の設定を検査の workflow の語彙へ写して呼ぶだけの、外部へ公開する変異検査の operation</summary>
/// <param name="workflow">一連の変異検査の workflow</param>
public sealed class MutationTesting(RunMutationTesting workflow)
{
    /// <summary>一回の変異検査の遂行</summary>
    /// <param name="options">外から受け取った設定</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <returns>成功なら確定結果、失敗なら中断理由</returns>
    public Task<Result<MutationRunResult, PipelineFailure>> RunAsync(
        MutationRunOptions options,
        Action<RunProgress> progress
    ) =>
        workflow.ExecuteAsync(
            new RunRequest(
                new TargetRequest(options.ProjectPath, options.TestProjectPath, options.Configuration),
                new SelectionRequest(
                    options.MutatePatterns,
                    options.SinceRef,
                    options.IgnoredOperators,
                    options.IgnoredMethods,
                    options.ProjectPath
                ),
                new ExecutionSettings(options.Concurrency, options.ValidateSurvivors, options.ExcludeStatic),
                options.WithBaseline ? options.FingerprintSettings() : null,
                options.OutputDirectory,
                options.WithBaseline
            ),
            progress
        );
}
