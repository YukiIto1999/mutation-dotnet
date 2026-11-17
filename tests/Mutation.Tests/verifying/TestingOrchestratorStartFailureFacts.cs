using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Collections.Concurrent;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>worker を開けなかった変異の記録と 取り消しを受けた隔離の要求の帰結の検査</summary>
/// <remarks>台本の worker が init に失敗で応えるか 応えない状況を作り 変異ごとの判定と記録を見る</remarks>
public sealed class TestingOrchestratorStartFailureFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, "Calc.Sub"], [10, 10]);

    /// <summary>共有の worker を開けないとき 計画した変異ごとに 一件も実行しなかった共有の実行として判定が残ること</summary>
    /// <remarks>
    /// 現在は検出として数える判定を残すが その種類は契約として確認できていないので確かめない
    /// 環境の失敗を検出に数えてよいかは別に判断する
    /// </remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_shared_worker_that_cannot_be_opened_leaves_each_planned_mutant_recorded_as_not_executed()
    {
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.SharedInitKey));

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0), Resident(2, 1));

        await Assert.That(execution.Result.Verdicts.Count).IsEqualTo(2);
        await Assert.That(execution.Result.Timings.Select(timing => (timing.MutantId.Value, timing.ExecutedTests, timing.Isolated)).Order().ToArray())
            .IsEquivalentTo([(1, 0, false), (2, 0, false)]);
        await Assert.That(execution.Calls.Any(call => call.Request == WorkerCall.RunRequest)).IsFalse();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>検出行列を求める走行では 共有の worker を開けなかった変異の行が 求めた全テストを未実行として残すこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task In_a_detection_matrix_run_a_shared_worker_that_cannot_be_opened_leaves_every_planned_test_not_run()
    {
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.SharedInitKey));

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Row(1).Failed).IsEmpty();
        await Assert.That(string.Join(',', execution.Row(1).NotRun.Select(index => index.Value))).IsEqualTo("0,1");
    }

    /// <summary>共有の worker を開く間に取り消すと その変異は途中の未判定になり 残りは実行に至らなかった未判定になること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_cancellation_while_the_shared_worker_opens_leaves_the_mutant_interrupted_and_the_rest_not_run()
    {
        using var run = new ScriptedWorkerRun(
            new Dictionary<string, int> { [ScriptedWorkerRun.SharedInitKey] = ScriptedWorkerRun.NeverAnswers }
        );
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteCancellingAsync(
            Roster,
            calls => calls.Any(call => call.Request == WorkerCall.InitRequest),
            (id, _) => settled.Enqueue(id.Value),
            Resident(1, 0),
            Resident(2, 1)
        );

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()));
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()));
        await Assert.That(settled).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>隔離 process を開けなかった変異が 実行を始めずに 失敗を理由にした検出として数えられること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task An_isolated_process_that_cannot_be_opened_counts_the_mutant_as_detected_by_that_failure()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0, 1), BudgetMs));
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.IsolatedInitKey));

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Verdict(1) is MutantVerdict.Killed { KillerTest.Length: > 0 }).IsTrue();
        await Assert.That(execution.Result.Timings.Single().ExecutedTests).IsEqualTo(0);
        await Assert.That(execution.IsolatedRuns()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>取り消し済みの要求で隔離を頼むと process を起こさず 途中の未判定で返すこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_cancelled_isolation_request_is_interrupted_without_starting_a_process()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0), BudgetMs));
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.IsolatedAnswer(1, new WorkerResponse.RunCompleted(WorkerContract.OutcomeSurvived, null, 1, 1, true))
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var (outcome, calls) = await run.IsolateAsync(Roster, plan, cancellation.Token);

        await Assert.That(outcome.Verdict).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()));
        await Assert.That(calls).IsEmpty();
    }

    /// <summary>init に失敗で応える台本の一件</summary>
    private static (string Key, WorkerResponse Answer) CannotOpen(string key) =>
        ScriptedWorkerRun.InitAnswer(key, new WorkerResponse.Failed("the test host did not open"));

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();
}
