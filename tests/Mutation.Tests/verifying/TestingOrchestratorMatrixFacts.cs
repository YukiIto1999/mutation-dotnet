using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>検出行列と既定の打切りの対比の検査</summary>
public sealed class TestingOrchestratorMatrixFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>名簿の二番目のテスト名</summary>
    private const string SubTest = "Calc.Sub";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, SubTest], [10, 10]);

    /// <summary>検出行列の実行が全失敗の記録を求め失敗したテストと未実行のテストを変異ごとに残すことの検査</summary>
    /// <remarks>二件のテストが検出する変異と一件だけが検出する変異と途中で応答を失った変異の対比</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Detection_matrix_runs_ask_for_every_failure_and_record_failed_and_unrun_tests()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Recorded(failed: [AddTest, SubTest], completed: [AddTest, SubTest])),
            ScriptedWorkerRun.SharedAnswer(2, Recorded(failed: [SubTest], completed: [AddTest, SubTest])),
            ScriptedWorkerRun.SharedAnswer(3, new WorkerResponse.Failed("応答を失った"))
        );

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, Resident(1, 0, 1), Resident(2, 0, 1), Resident(3, 0, 1));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Killed(SubTest));
        await Assert.That(execution.Verdict(3) is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
        await Assert.That(Indexes(execution.Row(1).Failed)).IsEqualTo("0,1");
        await Assert.That(Indexes(execution.Row(2).Failed)).IsEqualTo("1");
        await Assert.That(execution.Row(2).IsComplete).IsTrue();
        await Assert.That(execution.Row(3).Failed).IsEmpty();
        await Assert.That(Indexes(execution.Row(3).NotRun)).IsEqualTo("0,1");
        var runs = execution.Calls.Where(call => call.Request == WorkerCall.RunRequest).ToArray();
        await Assert.That(runs.Length).IsEqualTo(3);
        await Assert.That(runs.All(call => call.AllFailures)).IsTrue();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>検出行列の実行が共有側で検出した変異でも隔離側のテストを走らせて両方の検出テストを行に残すことの検査</summary>
    /// <remarks>共有側では一件目だけが、隔離側では二件目だけが検出する、被覆と static 初期化の両方を持つ変異</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Detection_matrix_runs_the_isolated_tests_even_when_the_shared_run_detects()
    {
        var plan = new MutantPlan(
            new MutantId(1),
            new MutantExecution.ResidentThenIsolated(Order(0), BudgetMs, Order(0, 1), BudgetMs)
        );
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Recorded(failed: [AddTest], completed: [AddTest])),
            ScriptedWorkerRun.IsolatedAnswer(1, Recorded(failed: [SubTest], completed: [AddTest, SubTest]))
        );

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, plan);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.IsolatedMutants()).IsEquivalentTo([1]);
        await Assert.That(Indexes(execution.Row(1).Planned)).IsEqualTo("0,1");
        await Assert.That(Indexes(execution.Row(1).Failed)).IsEqualTo("0,1");
        await Assert.That(execution.Row(1).IsComplete).IsTrue();
        var timing = execution.Result.Timings.Single();
        await Assert.That(timing.Isolated).IsTrue();
        await Assert.That(timing.ExecutedTests).IsEqualTo(3);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>既定の実行が共有側で検出した static 文脈の変異を隔離側で走らせ直さないことの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Default_runs_settle_a_shared_kill_of_a_resident_then_isolated_plan_without_isolation()
    {
        var plan = new MutantPlan(
            new MutantId(1),
            new MutantExecution.ResidentThenIsolated(Order(0), BudgetMs, Order(0, 1), BudgetMs)
        );
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Killed(AddTest, reached: true)),
            ScriptedWorkerRun.IsolatedAnswer(1, Survived(reached: true))
        );

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.IsolatedMutants()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>検出行列を求めない実行が最初の失敗での打ち切りを指示し行を残さないことの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Default_runs_ask_to_stop_at_the_first_failure_and_record_no_rows()
    {
        using var run = new ScriptedWorkerRun(ScriptedWorkerRun.SharedAnswer(1, Killed(AddTest, reached: true)));

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.SharedRun(1).AllFailures).IsFalse();
        await Assert.That(execution.Result.Detections).IsEmpty();
    }

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();

    /// <summary>生存の run 応答の組み立て</summary>
    private static WorkerResponse.RunCompleted Survived(bool? reached) =>
        new(WorkerContract.OutcomeSurvived, null, 1, 1, reached);

    /// <summary>検出の run 応答の組み立て</summary>
    private static WorkerResponse.RunCompleted Killed(string killerTest, bool? reached) =>
        new(WorkerContract.OutcomeKilled, killerTest, 1, 1, reached);

    /// <summary>全失敗を記録した到達済みの run 応答の組み立て</summary>
    /// <param name="failed">先頭を最初の失敗とする失敗したテストの識別子</param>
    /// <param name="completed">結果まで観測したテストの識別子</param>
    /// <returns>失敗があれば検出でなければ生存の応答</returns>
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

    /// <summary>テスト連番の列の順序を保つ連結</summary>
    /// <param name="indexes">テスト連番の列</param>
    /// <returns>連番をコンマで区切った文字列</returns>
    private static string Indexes(IEnumerable<TestIndex> indexes) => string.Join(',', indexes.Select(i => i.Value));
}
