using Mutation.Shared;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>計画の算出、継承、実行、再検証、保存を貫く判定確定の workflow</summary>
/// <param name="workerRuns">worker 実行の port</param>
/// <param name="snapshots">保存と継承の port</param>
public sealed class VerifyMutants(IWorkerRuns workerRuns, ISnapshots snapshots)
{
    /// <summary>全変異の判定の確定</summary>
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
        var executed = await workerRuns
            .ExecuteAsync(request.Launch, request.Baseline.Roster, plans, request.Execution.Concurrency, request.Progress)
            .ConfigureAwait(false);
        executed = await RevalidateSurvivorsAsync(request, schedule, executed).ConfigureAwait(false);
        var verdicts = MergedVerdicts(request, schedule, inherited, executed);
        snapshots.Save(
            new SnapshotSaveRequest(
                request.Mutants,
                verdicts,
                request.Baseline,
                request.Fingerprint,
                request.Launch.TestAssemblyPath,
                request.MutatedAssemblyPath,
                request.CompileErrorIds,
                request.SnapshotPath
            )
        );
        return new OrchestrationResult(verdicts, executed.Timings, executed.WorkerRestarts);
    }

    /// <summary>後段が前段を上書きする、即時確定・継承・実行の判定の合流</summary>
    private static Dictionary<MutantId, MutantVerdict> MergedVerdicts(
        VerificationRequest request,
        SchedulingOutcome schedule,
        IReadOnlyDictionary<MutantId, MutantVerdict> inherited,
        OrchestrationResult executed
    )
    {
        var verdicts = new Dictionary<MutantId, MutantVerdict>();
        foreach (var id in request.CompileErrorIds)
        {
            verdicts[id] = new MutantVerdict.CompileError();
        }

        foreach (var id in schedule.NoCoverageMutants)
        {
            verdicts[id] = new MutantVerdict.NoCoverage();
        }

        foreach (var id in schedule.ExcludedStaticMutants)
        {
            verdicts[id] = new MutantVerdict.Ignored();
        }

        foreach (var (id, verdict) in inherited)
        {
            verdicts[id] = verdict;
        }

        foreach (var (id, verdict) in executed.Verdicts)
        {
            verdicts[id] = verdict;
        }

        return verdicts;
    }

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
            .ExecuteAsync(request.Launch, request.Baseline.Roster, survivedPlans, request.Execution.Concurrency, request.Progress)
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
