using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Snapshot;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>止まった走行の確定済みの判定から、同じ入口で続きを実行できることの検査</summary>
public sealed class VerifyMutantsResumeFacts
{
    /// <summary>判定を確定した後に落ちた走行の続きが、確定済みの変異を飛ばして残りだけを実行すること</summary>
    [Test]
    public async Task The_next_run_executes_only_the_mutants_the_dead_run_never_settled()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var dying = new ScriptedRuns((plans, control) =>
        {
            control.Settled(plans[0].MutantId, new MutantVerdict.Killed(SnapshotFixture.TestName));
            control.Settled(plans[1].MutantId, new MutantVerdict.Survived());
            throw new InvalidOperationException("worker host died");
        });
        Exception? caught = null;
        try
        {
            await new VerifyMutants(dying, snapshots).ExecuteAsync(Request(fixture, withBaseline: true));
        }
        catch (InvalidOperationException exception)
        {
            caught = exception;
        }

        var resuming = new ScriptedRuns((plans, _) => Results(plans, new MutantVerdict.Killed("later")));
        var result = await new VerifyMutants(resuming, snapshots).ExecuteAsync(Request(fixture, withBaseline: true));

        await Assert.That(caught).IsNotNull();
        await Assert.That(dying.Requested.Single()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(resuming.Requested.Single()).IsEquivalentTo([2]);
        await Assert.That(result.Verdicts[new MutantId(0)]).IsEqualTo(new MutantVerdict.Killed(SnapshotFixture.TestName));
        await Assert.That(result.Verdicts[new MutantId(1)]).IsEqualTo(new MutantVerdict.Survived());
        await Assert.That(result.Verdicts[new MutantId(2)]).IsEqualTo(new MutantVerdict.Killed("later"));
        await Assert.That(File.Exists(fixture.JournalPath)).IsFalse();
        await Assert.That(snapshots.TryRebuild(fixture.SnapshotPath, SnapshotFixture.Fingerprint, fixture.TestAssemblyPath, fixture.MutatedAssemblyPath, "T"))
            .IsNotNull();
    }

    /// <summary>判定を確定できなかった変異を持つ走行の保存が、続きの実行でその変異だけを実行させること</summary>
    [Test]
    public async Task Unjudged_mutants_of_a_finished_run_are_the_only_ones_the_next_run_executes()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var first = new ScriptedRuns((plans, control) =>
        {
            control.Settled(plans[0].MutantId, new MutantVerdict.Survived());
            return new OrchestrationResult(
                new Dictionary<MutantId, MutantVerdict>
                {
                    [plans[0].MutantId] = new MutantVerdict.Survived(),
                    [plans[1].MutantId] = new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()),
                    [plans[2].MutantId] = new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()),
                },
                [],
                0,
                new Dictionary<MutantId, DetectionRow>()
            );
        });

        var partial = await new VerifyMutants(first, snapshots).ExecuteAsync(Request(fixture, withBaseline: false));
        var second = new ScriptedRuns((plans, _) => Results(plans, new MutantVerdict.Killed("later")));
        var complete = await new VerifyMutants(second, snapshots).ExecuteAsync(Request(fixture, withBaseline: true));

        await Assert.That(partial.Verdicts.Values.Count(v => v is MutantVerdict.Unjudged)).IsEqualTo(2);
        await Assert.That(second.Requested.Single()).IsEquivalentTo([1, 2]);
        await Assert.That(complete.Verdicts.Values.Any(v => v is MutantVerdict.Unjudged)).IsFalse();
        await Assert.That(complete.Verdicts[new MutantId(0)]).IsEqualTo(new MutantVerdict.Survived());
    }

    /// <summary>計画した全件に同じ判定を返す実行結果</summary>
    private static OrchestrationResult Results(IReadOnlyList<MutantPlan> plans, MutantVerdict verdict) =>
        new(plans.ToDictionary(plan => plan.MutantId, _ => verdict), [], 0, new Dictionary<MutantId, DetectionRow>());

    /// <summary>この環境の変異三件の判定確定の入力</summary>
    private static VerificationRequest Request(SnapshotFixture fixture, bool withBaseline) =>
        new(
            fixture.Mutants,
            new HashSet<MutantId>(),
            fixture.Baseline,
            fixture.Launch,
            new ExecutionSettings(1, false, false, false),
            withBaseline,
            SnapshotFixture.Fingerprint,
            fixture.MutatedAssemblyPath,
            fixture.SnapshotPath,
            _ => { }
        );

    /// <summary>受けた計画を記録し、決めた挙動で応える worker 実行の port</summary>
    private sealed class ScriptedRuns(Func<IReadOnlyList<MutantPlan>, ExecutionControl, OrchestrationResult> behavior) : IWorkerRuns
    {
        /// <summary>実行を求められた変異の連番の列。呼び出しごとに一つ</summary>
        public List<int[]> Requested { get; } = [];

        /// <inheritdoc />
        public Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(WorkerLaunchPlan launch, int concurrency) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<OrchestrationResult> ExecuteAsync(
            WorkerLaunchPlan launch,
            TestRoster roster,
            IReadOnlyList<MutantPlan> plans,
            int concurrency,
            bool detectionMatrix,
            Action<RunProgress> progress,
            ExecutionControl control
        )
        {
            Requested.Add(plans.Select(plan => plan.MutantId.Value).ToArray());
            return Task.FromResult(behavior(plans, control));
        }
    }
}
