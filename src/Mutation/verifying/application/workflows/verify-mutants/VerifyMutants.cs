using Mutation.Shared;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>計画の算出、継承、実行、再検証、保存を貫く判定確定の workflow</summary>
/// <param name="workerRuns">worker 実行の port</param>
/// <param name="snapshots">保存と継承の port</param>
public sealed class VerifyMutants(IWorkerRuns workerRuns, ISnapshots snapshots)
{
    /// <summary>全変異の判定の確定。判定の確定ごとに追記し、最後に次回の継承元として保存する</summary>
    /// <param name="request">判定確定に要る入力一式</param>
    /// <returns>変異ごとの判定と実行の記録</returns>
    public async Task<OrchestrationResult> ExecuteAsync(VerificationRequest request)
    {
        var alive = request.Mutants.Where(m => !request.CompileErrorIds.Contains(m.Id)).ToArray();
        var schedule = MutantScheduler.Plan(
            alive,
            request.Baseline.Coverage,
            request.Baseline.Tests,
            request.Execution.ExcludeStatic
        );
        request.Progress(
            new RunProgress.ExecutionPlanned(
                schedule.Plans.Count,
                schedule.NoCoverageMutants.Count,
                schedule.ExcludedStaticMutants.Count
            )
        );
        var inherited = Inherited(request);
        var plans = schedule.Plans.Where(p => !inherited.ContainsKey(p.MutantId)).ToArray();
        using var journal = snapshots.Begin(Checkpoint(request, schedule, inherited, plans));
        var executed = await workerRuns
            .ExecuteAsync(
                request.Launch,
                request.Baseline.Roster,
                plans,
                request.Execution.Concurrency,
                request.Execution.DetectionMatrix,
                request.Progress,
                new ExecutionControl(journal.Append)
            )
            .ConfigureAwait(false);
        executed = await RevalidateSurvivorsAsync(request, schedule, executed).ConfigureAwait(false);
        var verdicts = VerdictMerge.Merge(request.CompileErrorIds, schedule, inherited, executed.Verdicts);
        journal.Commit(SaveRequest(request, verdicts));
        return new OrchestrationResult(verdicts, executed.Timings, executed.WorkerRestarts, executed.Detections);
    }

    /// <summary>実行の前に分かっている判定を持つ保存の内容。これから実行する変異は未判定で置く</summary>
    private static SnapshotSaveRequest Checkpoint(
        VerificationRequest request,
        SchedulingOutcome schedule,
        IReadOnlyDictionary<MutantId, MutantVerdict> inherited,
        IReadOnlyList<MutantPlan> plans
    ) =>
        SaveRequest(
            request,
            VerdictMerge.Merge(
                request.CompileErrorIds,
                schedule,
                inherited,
                plans.ToDictionary(plan => plan.MutantId, _ => (MutantVerdict)new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()))
            )
        );

    /// <summary>判定の集まりを次回の継承元として保存する内容</summary>
    private static SnapshotSaveRequest SaveRequest(
        VerificationRequest request,
        IReadOnlyDictionary<MutantId, MutantVerdict> verdicts
    ) =>
        new(
            request.Mutants,
            verdicts,
            request.Baseline,
            request.Fingerprint,
            request.Launch.TestAssemblyPath,
            request.MutatedAssemblyPath,
            request.CompileErrorIds,
            request.SnapshotPath
        );

    /// <summary>前回の保存から継承できる判定の取得。--with-baseline でなければ空</summary>
    private IReadOnlyDictionary<MutantId, MutantVerdict> Inherited(VerificationRequest request)
    {
        if (!request.WithBaseline)
        {
            return new Dictionary<MutantId, MutantVerdict>();
        }

        var inherited = snapshots.Inherit(
            request.SnapshotPath,
            request.Mutants,
            request.Launch.TestAssemblyPath,
            request.Baseline.Coverage,
            request.Baseline.Roster
        );
        request.Progress(new RunProgress.VerdictsInherited(inherited.Count));
        return inherited;
    }

    /// <summary>生存した変異の隔離 process での再検証。--validate-survivors でなければ素通し</summary>
    private async Task<OrchestrationResult> RevalidateSurvivorsAsync(
        VerificationRequest request,
        SchedulingOutcome schedule,
        OrchestrationResult executed
    )
    {
        if (!request.Execution.ValidateSurvivors)
        {
            return executed;
        }

        var survivedPlans = schedule
            .Plans.Where(p =>
                p.Execution is not MutantExecution.Isolated
                && executed.Verdicts.TryGetValue(p.MutantId, out var v)
                && v is MutantVerdict.Survived
            )
            .Select(p => p with { Execution = p.Execution.ToIsolated() })
            .ToArray();
        if (survivedPlans.Length == 0)
        {
            return executed;
        }

        var revalidated = await workerRuns
            .ExecuteAsync(
                request.Launch,
                request.Baseline.Roster,
                survivedPlans,
                request.Execution.Concurrency,
                request.Execution.DetectionMatrix,
                request.Progress,
                ExecutionControl.None
            )
            .ConfigureAwait(false);
        return executed.Overlaid(revalidated);
    }
}

/// <summary>判定確定に要る入力一式</summary>
/// <param name="Mutants">全変異</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="Baseline">基準実行の観測</param>
/// <param name="Launch">worker の起動情報</param>
/// <param name="Execution">実行と判定の制御</param>
/// <param name="WithBaseline">前回の保存から判定を継承するか</param>
/// <param name="Fingerprint">変異の生成入力の指紋</param>
/// <param name="MutatedAssemblyPath">変異 assembly の絶対 path</param>
/// <param name="SnapshotPath">保存 file の絶対 path</param>
/// <param name="Progress">進行を伝える通知先</param>
public sealed record VerificationRequest(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlySet<MutantId> CompileErrorIds,
    BaselineProfile Baseline,
    WorkerLaunchPlan Launch,
    ExecutionSettings Execution,
    bool WithBaseline,
    string? Fingerprint,
    string MutatedAssemblyPath,
    string SnapshotPath,
    Action<RunProgress> Progress
);
