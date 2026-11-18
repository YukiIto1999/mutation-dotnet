using Mutation.Protocol;
using Mutation.Domain;
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

    /// <summary>共有の worker を開けないとき 計画した変異ごとに 一件も実行しなかった共有の実行として記録され 判定は検出に数えない runner の失敗の未判定になること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_shared_worker_that_cannot_be_opened_leaves_each_planned_mutant_unjudged_as_a_runner_failure()
    {
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.SharedInitKey));
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteSettlingAsync(Roster, (id, _) => settled.Enqueue(id.Value), Resident(1, 0), Resident(2, 1));

        await Assert.That(execution.Result.Verdicts.Count).IsEqualTo(2);
        await Assert.That(execution.Result.Verdicts.Values.All(IsRunnerFailure)).IsTrue();
        await Assert.That(RunnerFailureDetail(execution.Verdict(1))).Contains("the test host did not open");
        await Assert.That(RunnerFailureDetail(execution.Verdict(2))).Contains("the test host did not open");
        await Assert.That(settled).IsEmpty();
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

    /// <summary>隔離 process を開けなかった変異が 実行を始めずに 検出ではなく runner の失敗の未判定として残ること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task An_isolated_process_that_cannot_be_opened_leaves_the_mutant_unjudged_as_a_runner_failure()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0, 1), BudgetMs));
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.IsolatedInitKey));
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteSettlingAsync(Roster, (id, _) => settled.Enqueue(id.Value), plan);

        await Assert.That(IsRunnerFailure(execution.Verdict(1))).IsTrue();
        await Assert.That(RunnerFailureDetail(execution.Verdict(1))).Contains("the test host did not open");
        await Assert.That(settled).IsEmpty();
        await Assert.That(execution.Result.Timings.Single().ExecutedTests).IsEqualTo(0);
        await Assert.That(execution.IsolatedRuns()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>隔離 process が run に失敗で応えた変異が 検出にも時間超過にも数えられず runner の失敗の未判定になり 期限切れの確かめにも回らないこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task An_isolated_run_answered_with_a_failure_is_unjudged_without_a_timeout_examination()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0), BudgetMs));
        using var run = new ScriptedWorkerRun(ScriptedWorkerRun.IsolatedAnswer(1, new WorkerResponse.Failed("the runner lost the run")));

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(RunnerFailureDetail(execution.Verdict(1))).Contains("the runner lost the run");
        await Assert.That(execution.IsolatedMutants()).IsEquivalentTo([1]);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>runner の失敗の未判定が、検出にも未検出にも数えられず、走行を完了としないこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_runner_failure_is_neither_detected_nor_undetected_and_the_run_is_not_complete()
    {
        using var run = new ScriptedWorkerRun(CannotOpen(ScriptedWorkerRun.SharedInitKey));

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0), Resident(2, 1));
        var target = new TargetResult("T", [], execution.Result.Verdicts, [], TargetTimings.None, [], 0, new Dictionary<MutantId, DetectionRow>());
        var result = new MutationRunResult([new TargetOutcome.Completed(target)], new PhaseTimings(0, 0, 0, 0, 0, 0));

        await Assert.That(target.DetectedCount).IsEqualTo(0);
        await Assert.That(target.UndetectedCount).IsEqualTo(0);
        await Assert.That(target.RunnerFailureCount).IsEqualTo(2);
        await Assert.That(result.Score).IsNull();
        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.State).IsEqualTo("unjudged");
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

    /// <summary>判定が runner の失敗の未判定か</summary>
    private static bool IsRunnerFailure(MutantVerdict verdict) =>
        verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure };

    /// <summary>runner の失敗の未判定が持つ理由。失敗でなければ空</summary>
    private static string RunnerFailureDetail(MutantVerdict verdict) =>
        verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure failure } ? failure.Detail : "";

    /// <summary>init に失敗で応える台本の一件</summary>
    private static (string Key, WorkerResponse Answer) CannotOpen(string key) =>
        ScriptedWorkerRun.InitAnswer(key, new WorkerResponse.Failed("the test host did not open"));

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();
}
