using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>対象一件の、継承の照合から判定確定までの遂行</summary>
/// <param name="mutantSource">変異一式の提供の port</param>
/// <param name="verifyMutants">判定確定の workflow</param>
/// <param name="workerRuns">worker 実行の port</param>
/// <param name="snapshots">保存と継承の port</param>
internal sealed class TargetRun(
    IMutantSource mutantSource,
    VerifyMutants verifyMutants,
    IWorkerRuns workerRuns,
    ISnapshots snapshots
)
{
    /// <summary>継承の照合、変異生成、baseline 観測、判定確定の遂行</summary>
    /// <param name="session">実行で共有する文脈</param>
    /// <param name="target">準備済みの対象</param>
    /// <param name="name">置き場と報告で対象を指す名前</param>
    /// <returns>成功なら対象の確定結果、失敗なら中断理由</returns>
    public async Task<Result<TargetResult, PipelineFailure>> ExecuteAsync(
        PipelineSession session,
        PreparedTarget target,
        string name
    )
    {
        var layout = session.Layout.For(name);
        session.Clock.StartPhase();
        if (session.Request.WithBaseline && TryRebuild(session, layout, target) is { } reused)
        {
            return new Result<TargetResult, PipelineFailure>.Succeeded(reused);
        }

        return await MutateAndTestAsync(session, layout, target).ConfigureAwait(false);
    }

    /// <summary>保存と生成入力が完全一致したときの、前回結果の再利用。一致しなければ不在</summary>
    private TargetResult? TryRebuild(PipelineSession session, TargetLayout layout, PreparedTarget target)
    {
        var rebuilt = snapshots.TryRebuild(
            layout.SnapshotPath,
            target.Fingerprint,
            target.TestAssemblyPath,
            Path.Combine(layout.MutatedDirectory, target.TargetAssemblyName + ".dll"),
            layout.TargetName
        );
        if (rebuilt is not null)
        {
            session.Progress(new RunProgress.SnapshotMatched());
        }

        return rebuilt;
    }

    /// <summary>変異の生成からテスト実行までの後半段階の遂行</summary>
    private async Task<Result<TargetResult, PipelineFailure>> MutateAndTestAsync(
        PipelineSession session,
        TargetLayout layout,
        PreparedTarget target
    )
    {
        var compiled = mutantSource.Generate(target, session.Request.Selection, layout.MutatedDirectory);
        if (compiled is Result<GeneratedMutants, PipelineFailure>.Failed(var compileFailure))
        {
            return new Result<TargetResult, PipelineFailure>.Failed(compileFailure);
        }

        var artifact = ((Result<GeneratedMutants, PipelineFailure>.Succeeded)compiled).Value;
        session.Progress(
            new RunProgress.MutantsGenerated(
                artifact.Mutants.Count,
                artifact.CompileErrorIds.Count,
                artifact.MutateMs,
                artifact.CompileMs
            )
        );
        var launch = new WorkerLaunchPlan(
            Path.Combine(AppContext.BaseDirectory, "Mutation.Worker.dll"),
            target.TestAssemblyPath,
            layout.MutatedDirectory,
            target.TargetAssemblyName,
            layout.TestResultsDirectory
        );
        return await TestAsync(session, layout, target, artifact, launch).ConfigureAwait(false);
    }

    /// <summary>baseline 観測から判定確定までの遂行</summary>
    private async Task<Result<TargetResult, PipelineFailure>> TestAsync(
        PipelineSession session,
        TargetLayout layout,
        PreparedTarget target,
        GeneratedMutants artifact,
        WorkerLaunchPlan launch
    )
    {
        session.Clock.StartPhase();
        var observed = await workerRuns
            .ObserveAsync(launch, session.Request.Execution.Concurrency)
            .ConfigureAwait(false);
        if (observed is Result<BaselineProfile, PipelineFailure>.Failed(var baselineFailure))
        {
            return new Result<TargetResult, PipelineFailure>.Failed(baselineFailure);
        }

        var baseline = ((Result<BaselineProfile, PipelineFailure>.Succeeded)observed).Value;
        var baselineMs = session.Clock.EndPhase();
        session.Progress(
            new RunProgress.BaselineCompleted(
                baselineMs,
                baseline.Tests.Count,
                baseline.Tests.Count(t => !t.BaselinePassed)
            )
        );
        var verdicts = await verifyMutants
            .ExecuteAsync(
                new VerificationRequest(
                    artifact.Mutants,
                    artifact.CompileErrorIds,
                    baseline,
                    launch,
                    session.Request.Execution,
                    session.Request.WithBaseline,
                    target.Fingerprint,
                    artifact.MutatedAssemblyPath,
                    layout.SnapshotPath,
                    session.Progress
                )
            )
            .ConfigureAwait(false);
        return new Result<TargetResult, PipelineFailure>.Succeeded(
            new TargetResult(
                layout.TargetName,
                artifact.Mutants,
                verdicts.Verdicts,
                baseline.Tests,
                new TargetTimings(artifact.MutateMs, artifact.CompileMs, baselineMs, session.Clock.EndPhase()),
                verdicts.Timings,
                verdicts.WorkerRestarts
            )
        );
    }
}
