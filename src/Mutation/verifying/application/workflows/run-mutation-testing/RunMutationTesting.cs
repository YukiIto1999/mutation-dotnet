using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>build から報告書き出しまでの一連の変異検査を貫く workflow</summary>
/// <param name="mutantSource">変異一式の提供の port</param>
/// <param name="verifyMutants">判定確定の workflow</param>
/// <param name="workerRuns">worker 実行の port</param>
/// <param name="snapshots">保存と継承の port</param>
/// <param name="reports">報告書き出しの port</param>
public sealed class RunMutationTesting(
    IMutantSource mutantSource,
    VerifyMutants verifyMutants,
    IWorkerRuns workerRuns,
    ISnapshots snapshots,
    IVerificationReports reports
)
{
    /// <summary>全段階の実行による確定結果の算出。成功時は報告も書き出す</summary>
    /// <param name="request">一回の変異検査の入力</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <returns>成功なら確定結果、失敗なら中断理由</returns>
    public async Task<Result<MutationRunResult, PipelineFailure>> ExecuteAsync(
        RunRequest request,
        Action<RunProgress> progress
    )
    {
        var clock = new PhaseClock();
        var layout = new RunLayout(request.OutputDirectory);
        var located = await mutantSource
            .PrepareAsync(request.Target, layout.WorkDirectory, request.WithBaseline ? request.FingerprintSettings : null)
            .ConfigureAwait(false);
        var buildMs = clock.EndPhase();
        progress(new RunProgress.BuildCompleted(buildMs));
        if (located is Result<PreparedTarget, PipelineFailure>.Failed(var locateFailure))
        {
            return new Result<MutationRunResult, PipelineFailure>.Failed(locateFailure);
        }

        var target = ((Result<PreparedTarget, PipelineFailure>.Succeeded)located).Value;
        var session = new PipelineSession(request, layout, target.Fingerprint, buildMs, progress, clock);
        if (request.WithBaseline && TryRebuild(session, target) is { } reused)
        {
            return Publish(request, layout, reused);
        }

        var outcome = await MutateAndTestAsync(session, target).ConfigureAwait(false);
        if (outcome is Result<MutationRunResult, PipelineFailure>.Succeeded(var result))
        {
            return Publish(request, layout, result);
        }

        return outcome;
    }

    /// <summary>成功結果の報告書き出しと返却</summary>
    private Result<MutationRunResult, PipelineFailure> Publish(
        RunRequest request,
        RunLayout layout,
        MutationRunResult result
    )
    {
        reports.Write(result, Path.GetDirectoryName(request.Target.ProjectPath) ?? ".", layout.ReportsDirectory);
        return new Result<MutationRunResult, PipelineFailure>.Succeeded(result);
    }

    /// <summary>保存と生成入力が完全一致したときの、前回結果の再利用。一致しなければ不在</summary>
    private MutationRunResult? TryRebuild(PipelineSession session, PreparedTarget target)
    {
        var timings = new PhaseTimings(session.BuildMs, 0, 0, 0, 0, session.Clock.TotalMs);
        var rebuilt = snapshots.TryRebuild(
            session.Layout.SnapshotPath,
            session.Fingerprint,
            target.TestAssemblyPath,
            Path.Combine(session.Layout.MutatedDirectory, target.TargetAssemblyName + ".dll"),
            timings
        );
        if (rebuilt is not null)
        {
            session.Progress(new RunProgress.SnapshotMatched());
        }

        return rebuilt;
    }

    /// <summary>変異の生成からテスト実行までの後半段階の遂行</summary>
    private async Task<Result<MutationRunResult, PipelineFailure>> MutateAndTestAsync(
        PipelineSession session,
        PreparedTarget target
    )
    {
        var compiled = mutantSource.Generate(session.Request.Selection, session.Layout.MutatedDirectory);
        if (compiled is Result<GeneratedMutants, PipelineFailure>.Failed(var compileFailure))
        {
            return new Result<MutationRunResult, PipelineFailure>.Failed(compileFailure);
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
            session.Layout.MutatedDirectory,
            target.TargetAssemblyName,
            session.Layout.TestResultsDirectory
        );
        return await TestAsync(session, artifact, launch).ConfigureAwait(false);
    }

    /// <summary>baseline 観測から判定確定までの遂行</summary>
    private async Task<Result<MutationRunResult, PipelineFailure>> TestAsync(
        PipelineSession session,
        GeneratedMutants artifact,
        WorkerLaunchPlan launch
    )
    {
        session.Clock.StartPhase();
        var observed = await workerRuns.ObserveAsync(launch, session.Request.Execution.Concurrency).ConfigureAwait(false);
        if (observed is Result<BaselineProfile, PipelineFailure>.Failed(var baselineFailure))
        {
            return new Result<MutationRunResult, PipelineFailure>.Failed(baselineFailure);
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
                    session.Fingerprint,
                    artifact.MutatedAssemblyPath,
                    session.Layout.SnapshotPath,
                    session.Progress
                )
            )
            .ConfigureAwait(false);
        var timings = new PhaseTimings(
            session.BuildMs,
            artifact.MutateMs,
            artifact.CompileMs,
            baselineMs,
            session.Clock.EndPhase(),
            session.Clock.TotalMs
        );
        return new Result<MutationRunResult, PipelineFailure>.Succeeded(
            new MutationRunResult(
                artifact.Mutants,
                verdicts.Verdicts,
                baseline.Tests,
                timings,
                verdicts.Timings,
                verdicts.WorkerRestarts
            )
        );
    }
}
