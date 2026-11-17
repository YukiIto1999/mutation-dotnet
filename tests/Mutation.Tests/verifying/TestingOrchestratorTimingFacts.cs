using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Assertions.Enums;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>変異ごとの実行の記録が 数えたテストと所要時間と実行形を何から取るかの検査</summary>
/// <remarks>台本の worker が返す実行数と 応答の前の待ちで 記録の値が決まる状況を作る</remarks>
public sealed class TestingOrchestratorTimingFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>名簿の二番目のテスト名</summary>
    private const string SubTest = "Calc.Sub";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>隔離側の応答を遅らせる時間。記録の所要時間がこれを含むかを見る</summary>
    private const int IsolatedDelayMs = 300;

    /// <summary>遅らせた隔離側の所要時間として 記録に要る下限。待ちの刻みの誤差を見込む</summary>
    private const double MinimumElapsedMs = 250;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, SubTest], [10, 10]);

    /// <summary>共有の実行の記録が worker の数えた実行数を使い 数えられない worker では計画したテスト数を使うこと</summary>
    /// <remarks>どちらも二件のテストを計画し 一件目の変異は一件で打ち切り 二件目の変異の応答は実行数を持たない</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_shared_run_counts_the_tests_the_worker_started_or_the_planned_tests_when_it_cannot_count()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, AddTest, 1, 1, true)),
            ScriptedWorkerRun.SharedAnswer(2, new WorkerResponse.RunCompleted(WorkerContract.OutcomeSurvived, null, null, 1, true))
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1), Resident(2, 0, 1));

        await Assert.That(ExecutedTests(execution)).IsEquivalentTo([1, 2], CollectionOrdering.Matching);
    }

    /// <summary>隔離の実行の記録が worker の数えた実行数を使い 数えられない worker では計画したテスト数を使うこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task An_isolated_run_counts_the_tests_the_worker_started_or_the_planned_tests_when_it_cannot_count()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.IsolatedAnswer(1, new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, AddTest, 1, 1, true)),
            ScriptedWorkerRun.IsolatedAnswer(2, new WorkerResponse.RunCompleted(WorkerContract.OutcomeSurvived, null, null, 1, true))
        );

        var execution = await run.ExecuteAsync(Roster, Isolated(1, 0, 1), Isolated(2, 0, 1));

        await Assert.That(ExecutedTests(execution)).IsEquivalentTo([1, 2], CollectionOrdering.Matching);
    }

    /// <summary>検出行列を求める走行で 共有側の検出と隔離側の実行を合わせた記録が 両方の実行数の和になること</summary>
    /// <remarks>共有側は二件を計画して一件を始め 隔離側は二件を始める</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_detection_matrix_kill_counts_the_tests_started_by_both_executions()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Recorded(failed: [AddTest], completed: [AddTest])),
            ScriptedWorkerRun.IsolatedAnswer(1, Recorded(failed: [SubTest], completed: [AddTest, SubTest]))
        );

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, ResidentThenIsolated(1, 0, 1));

        await Assert.That(ExecutedTests(execution)).IsEquivalentTo([3], CollectionOrdering.Matching);
    }

    /// <summary>検出行列を求める走行で 共有側の検出と隔離側の実行を合わせた記録の所要時間が 隔離側の所要時間を含むこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_detection_matrix_kill_adds_the_time_of_the_isolated_execution_to_the_shared_one()
    {
        using var run = new ScriptedWorkerRun(
            new Dictionary<string, int> { [ScriptedWorkerRun.IsolatedKey(1)] = IsolatedDelayMs },
            ScriptedWorkerRun.SharedAnswer(1, Recorded(failed: [AddTest], completed: [AddTest])),
            ScriptedWorkerRun.IsolatedAnswer(1, Recorded(failed: [SubTest], completed: [AddTest, SubTest]))
        );

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, ResidentThenIsolated(1, 0, 1));

        await Assert.That(execution.Result.Timings.Single().ElapsedMs).IsGreaterThanOrEqualTo(MinimumElapsedMs);
    }

    /// <summary>完了の応答を得られなかった共有の実行が 一件のテストも始めなかった共有の実行として記録されること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_shared_run_that_got_no_completion_is_recorded_as_a_shared_run_that_started_no_tests()
    {
        using var run = new ScriptedWorkerRun(ScriptedWorkerRun.SharedAnswer(1, new WorkerResponse.Failed("the host lost the run")));

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Result.Timings.Single() is { ExecutedTests: 0, Isolated: false }).IsTrue();
        await Assert.That(execution.Result.WorkerRestarts).IsEqualTo(1);
        await Assert.That(execution.Verdict(1) is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
    }

    /// <summary>変異ごとの実行の記録の実行数を変異の連番順に並べた列</summary>
    private static int[] ExecutedTests(ScriptedExecution execution) =>
        execution.Result.Timings.OrderBy(timing => timing.MutantId.Value).Select(timing => timing.ExecutedTests).ToArray();

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>隔離 process で実行する計画の組み立て</summary>
    private static MutantPlan Isolated(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Isolated(Order(tests), BudgetMs));

    /// <summary>共有 worker で先に試し 隔離 process でも実行する計画。どちらも同じテスト列</summary>
    private static MutantPlan ResidentThenIsolated(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.ResidentThenIsolated(Order(tests), BudgetMs, Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();

    /// <summary>全失敗を記録した到達済みの run 応答の組み立て</summary>
    private static WorkerResponse.RunCompleted Recorded(string[] failed, string[] completed) =>
        new(
            failed.Length > 0 ? WorkerContract.OutcomeKilled : WorkerContract.OutcomeSurvived,
            failed.FirstOrDefault(),
            completed.Length,
            1,
            true,
            failed,
            completed
        );
}
