using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>共有 worker の判定を隔離で再確認する境界の検査</summary>
/// <remarks>不要な隔離も最終判定の逆転で検出できる共有側と隔離側の異なる応答</remarks>
public sealed class TestingOrchestratorFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>名簿の二番目のテスト名</summary>
    private const string SubTest = "Calc.Sub";

    /// <summary>汚れた共有 worker が誤って検出したテスト名</summary>
    private const string SpuriousTest = "Spurious.Test";

    /// <summary>変異の枝へ入ったと観測した応答を表す引数</summary>
    private const string ReachedLabel = "reached";

    /// <summary>枝への到達を観測しない応答を表す引数</summary>
    private const string UnobservedLabel = "unobserved";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, SubTest], [10, 10]);

    /// <summary>未到達の生存の再判定後も共有 worker を継続利用することの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Unreached_survival_is_settled_by_the_isolated_run_and_keeps_the_shared_worker()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Survived(reached: false)),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed(AddTest, reached: true)),
            ScriptedWorkerRun.SharedAnswer(2, Survived(reached: true)),
            ScriptedWorkerRun.IsolatedAnswer(2, Killed(SubTest, reached: true))
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1), Resident(2, 1));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Survived());
        await Assert.That(execution.Result.WorkerRestarts).IsEqualTo(0);
        await Assert.That(execution.Result.Timings.Count).IsEqualTo(2);
        await Assert.That(execution.Result.Timings.Where(t => t.Isolated).Select(t => t.MutantId.Value).ToArray())
            .IsEquivalentTo([1]);
        await Assert.That(execution.SharedRun(2).Pid).IsEqualTo(execution.SharedRun(1).Pid);
        await Assert.That(execution.IsolatedMutants()).IsEquivalentTo([1]);
        await Assert.That(TestIds(execution.IsolatedRuns()[0])).IsEqualTo($"{AddTest},{SubTest}");
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>未到達の検出の再判定後に汚れた共有 worker を流用しないことの検査</summary>
    /// <param name="isolatedKills">新規 process でも検出されるかの指定</param>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Unreached_kill_is_reconfirmed_in_a_fresh_process_and_the_polluted_worker_is_replaced(bool isolatedKills)
    {
        var confirmed = isolatedKills ? Killed(AddTest, reached: true) : Survived(reached: true);
        MutantVerdict expected = isolatedKills ? new MutantVerdict.Killed(AddTest) : new MutantVerdict.Survived();
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Killed(SpuriousTest, reached: false)),
            ScriptedWorkerRun.IsolatedAnswer(1, confirmed),
            ScriptedWorkerRun.SharedAnswer(2, Survived(reached: true)),
            ScriptedWorkerRun.IsolatedAnswer(2, Killed(SubTest, reached: true))
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0), Resident(2, 1));

        await Assert.That(execution.Verdict(1)).IsEqualTo(expected);
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Survived());
        await Assert.That(execution.Result.WorkerRestarts).IsEqualTo(1);
        await Assert.That(execution.Result.Timings.Count).IsEqualTo(2);
        await Assert.That(execution.SharedRun(2).Pid).IsNotEqualTo(execution.SharedRun(1).Pid);
        await Assert.That(execution.IsolatedMutants()).IsEquivalentTo([1]);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>到達済みと未観測の結果に不要な隔離を加えないことの検査</summary>
    /// <param name="outcome">共有 worker の結末</param>
    /// <param name="reachability">到達または未観測の識別子</param>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    [Arguments(WorkerContract.OutcomeSurvived, ReachedLabel)]
    [Arguments(WorkerContract.OutcomeKilled, ReachedLabel)]
    [Arguments(WorkerContract.OutcomeSurvived, UnobservedLabel)]
    [Arguments(WorkerContract.OutcomeKilled, UnobservedLabel)]
    public async Task Reached_or_unobserved_results_are_settled_by_the_shared_worker_alone(string outcome, string reachability)
    {
        bool? reached = reachability == ReachedLabel ? true : null;
        var killed = outcome == WorkerContract.OutcomeKilled;
        var resident = killed ? Killed(AddTest, reached) : Survived(reached);
        var contrary = killed ? Survived(reached: true) : Killed(SubTest, reached: true);
        MutantVerdict expected = killed ? new MutantVerdict.Killed(AddTest) : new MutantVerdict.Survived();
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, resident),
            ScriptedWorkerRun.IsolatedAnswer(1, contrary)
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Verdict(1)).IsEqualTo(expected);
        await Assert.That(execution.Result.WorkerRestarts).IsEqualTo(0);
        await Assert.That(execution.StartedProcesses()).IsEqualTo(1);
        await Assert.That(execution.Result.Timings.Single().Isolated).IsFalse();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>未到達の再判定に隔離側のテスト列を一度だけ使うことの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Unreached_survival_of_a_resident_then_isolated_plan_is_rejudged_once_on_its_isolated_tests()
    {
        var plan = new MutantPlan(
            new MutantId(1),
            new MutantExecution.ResidentThenIsolated(Order(0), BudgetMs, Order(0, 1), BudgetMs)
        );
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Survived(reached: false)),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed(SubTest, reached: true))
        );

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(SubTest));
        await Assert.That(execution.IsolatedMutants()).IsEquivalentTo([1]);
        await Assert.That(TestIds(execution.SharedRun(1))).IsEqualTo(AddTest);
        await Assert.That(TestIds(execution.IsolatedRuns()[0])).IsEqualTo($"{AddTest},{SubTest}");
        await Assert.That(execution.Result.Timings.Single().Isolated).IsTrue();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <param name="tests">名簿の連番によるテストの列</param>
    /// <returns>共有実行だけを指定する計画</returns>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    /// <param name="tests">名簿の連番</param>
    /// <returns>指定順のテスト連番の列</returns>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();

    /// <summary>run に求められたテストの識別子の連結</summary>
    /// <param name="call">記録された worker 指示</param>
    /// <returns>指定順の識別子をコンマで区切った文字列</returns>
    private static string TestIds(WorkerCall call) => string.Join(',', call.TestIds);

    /// <summary>生存の run 応答の組み立て</summary>
    /// <param name="reached">活性の変異の枝へ到達したかの観測</param>
    /// <returns>指定した到達観測を持つ生存の応答</returns>
    private static WorkerResponse.RunCompleted Survived(bool? reached) =>
        new(WorkerContract.OutcomeSurvived, null, 1, 1, reached);

    /// <summary>検出の run 応答の組み立て</summary>
    /// <param name="killerTest">変異を検出したテストの名前</param>
    /// <param name="reached">活性の変異の枝へ到達したかの観測</param>
    /// <returns>指定した検出元と到達観測を持つ応答</returns>
    private static WorkerResponse.RunCompleted Killed(string killerTest, bool? reached) =>
        new(WorkerContract.OutcomeKilled, killerTest, 1, 1, reached);
}
