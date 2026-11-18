using Mutation.Domain;
using Mutation.Application;
using Mutation.Infrastructure.Snapshot;
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
        await Assert.That(snapshots.TryRebuild(fixture.SnapshotPath, SnapshotFixture.Identity, fixture.TestAssemblyPath, fixture.MutatedAssemblyPath, "T"))
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

    /// <summary>入力の閉包の同一性が前回と違う、または示せない走行が、前回の判定を継承せず、全ての変異を実行し直すこと</summary>
    /// <remarks>前回の保存は同じファイルと同じ被覆を持つので、閉包の同一性だけが継承を止める</remarks>
    /// <param name="closureIdentity">今回の入力の閉包の同一性。入力を読めなかったときは不在</param>
    /// <returns>走行まで含む非同期検査</returns>
    [Test]
    [Arguments("another-closure")]
    [Arguments(null)]
    public async Task A_closure_identity_that_differs_or_is_unknown_makes_the_next_run_execute_every_mutant(string? closureIdentity)
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var first = new ScriptedRuns((plans, _) => Results(plans, new MutantVerdict.Killed("first")));
        await new VerifyMutants(first, snapshots).ExecuteAsync(Request(fixture, withBaseline: false));

        var second = new ScriptedRuns((plans, _) => Results(plans, new MutantVerdict.Killed("second")));
        var result = await new VerifyMutants(second, snapshots).ExecuteAsync(
            Request(fixture, withBaseline: true, closureIdentity is null ? null : WithClosure(closureIdentity))
        );

        await Assert.That(second.Requested.Single()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(result.Verdicts.Values.All(verdict => verdict is MutantVerdict.Killed { KillerTest: "second" })).IsTrue();
    }

    /// <summary>止まった走行の保存が、入力の閉包の同一性を持ち、同じ同一性の走行だけが続きを実行できること</summary>
    [Test]
    public async Task A_stopped_run_is_continued_only_by_a_run_with_the_same_closure_identity()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var dying = new ScriptedRuns((plans, control) =>
        {
            control.Settled(plans[0].MutantId, new MutantVerdict.Killed(SnapshotFixture.TestName));
            throw new InvalidOperationException("worker host died");
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new VerifyMutants(dying, snapshots).ExecuteAsync(Request(fixture, withBaseline: false))
        );

        var other = new ScriptedRuns((plans, _) => Results(plans, new MutantVerdict.Killed("other")));
        await new VerifyMutants(other, snapshots).ExecuteAsync(Request(fixture, withBaseline: true, WithClosure("another-closure")));

        await Assert.That(other.Requested.Single()).IsEquivalentTo([0, 1, 2]);
    }

    /// <summary>計画した全件に同じ判定を返す実行結果</summary>
    private static OrchestrationResult Results(IReadOnlyList<MutantPlan> plans, MutantVerdict verdict) =>
        new(plans.ToDictionary(plan => plan.MutantId, _ => verdict), [], 0, new Dictionary<MutantId, DetectionRow>());

    /// <summary>この環境の変異三件の判定確定の入力。再利用の同一性はこの環境の対</summary>
    private static VerificationRequest Request(SnapshotFixture fixture, bool withBaseline) =>
        Request(fixture, withBaseline, SnapshotFixture.Identity);

    /// <summary>この環境の変異三件の判定確定の入力。再利用の同一性は指定の値で、示せなかったときは不在</summary>
    private static VerificationRequest Request(SnapshotFixture fixture, bool withBaseline, ReuseIdentity? identity) =>
        new(
            fixture.Mutants,
            new HashSet<MutantId>(),
            fixture.Baseline,
            fixture.Launch,
            new ExecutionSettings(1, false, false, false),
            withBaseline,
            identity,
            fixture.MutatedAssemblyPath,
            fixture.SnapshotPath,
            _ => { },
            CancellationToken.None
        );

    /// <summary>この環境の対のうち、閉包の同一性だけを置き換えた再利用の同一性</summary>
    private static ReuseIdentity WithClosure(string closure) => SnapshotFixture.Identity with { Closure = closure };

    /// <summary>受けた計画を記録し、決めた挙動で応える worker 実行の port</summary>
    private sealed class ScriptedRuns(Func<IReadOnlyList<MutantPlan>, ExecutionControl, OrchestrationResult> behavior) : IWorkerRuns
    {
        /// <inheritdoc />
        public WorkerHost Host { get; } = new("host", "worker.dll");

        /// <summary>実行を求められた変異の連番の列。呼び出しごとに一つ</summary>
        public List<int[]> Requested { get; } = [];

        /// <inheritdoc />
        public Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(
            WorkerLaunchPlan launch,
            int concurrency,
            CancellationToken cancellation
        ) => throw new NotSupportedException();

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
