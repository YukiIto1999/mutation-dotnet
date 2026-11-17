using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Workers;
using System.Collections.Concurrent;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>実行時間の期限を超えた変異を、検出と競合による遅れに分ける境界の検査</summary>
/// <remarks>
/// 応えない worker を台本で作り、予算と猶予を数百 ms に縮めた期限で本物の WorkerClient に殺させる
/// 期限を超えたら、変異のない対照の実行と新規 process での再実行で確かめる
/// 数百 ms の期限は他の検査の process 起動と競り合うと超えるので、他の検査と並行に走らせない
/// </remarks>
[NotInParallel]
public sealed class TestingOrchestratorTimeoutFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>変異一件の実行時間予算。台本の応えない実行がこの期限で殺される</summary>
    private const double BudgetMs = 300;

    /// <summary>変異のない対照の run を指す連番</summary>
    private const int ControlId = -1;

    /// <summary>変異のない対照の run を予算より遅らせる時間。猶予の長さにも使い、対照は期限に収まって予算を超える</summary>
    private const int ControlDelayMs = 600;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, "Calc.Sub"], [10, 10]);

    /// <summary>猶予を持たず、予算がそのまま期限になる設定</summary>
    private static readonly RunDeadlines BudgetOnly = new(TimeSpan.Zero);

    /// <summary>期限を超えても、変異のない対照の実行が予算に収まり、再実行も同じく期限を超えれば、検出に数える timeout になること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_that_reproduces_while_the_machine_meets_the_budget_is_a_detection()
    {
        using var run = Scripted(
            new Dictionary<string, int>
            {
                [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers,
                [ScriptedWorkerRun.IsolatedKey(1)] = ScriptedWorkerRun.NeverAnswers,
            },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived())
        );
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteSettlingAsync(Roster, (id, _) => settled.Enqueue(id.Value), Resident(1, 0));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.TimedOut());
        await Assert.That(string.Join(',', execution.IsolatedMutants())).IsEqualTo("-1,1");
        await Assert.That(settled.ToArray()).IsEquivalentTo([1]);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>期限を超えたとき、変異のない対照の実行が予算を超えて遅ければ、検出に数えず、再実行もせず未判定にすること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_while_unmutated_tests_run_slower_than_the_budget_is_unjudged()
    {
        using var run = Scripted(
            new Dictionary<string, int>
            {
                [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers,
                [ScriptedWorkerRun.IsolatedKey(ControlId)] = 600,
            },
            new RunDeadlines(TimeSpan.FromMilliseconds(600)),
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived()),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed())
        );
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteSettlingAsync(Roster, (id, _) => settled.Enqueue(id.Value), Resident(1, 0));

        var cause = execution.Verdict(1) is MutantVerdict.Unjudged { Cause: UnjudgedCause.ContendedTimeout contended } ? contended : null;
        await Assert.That(cause).IsNotNull();
        await Assert.That(cause!.BudgetMs).IsEqualTo(BudgetMs);
        await Assert.That(cause.ControlMs!.Value).IsGreaterThan(BudgetMs);
        await Assert.That(string.Join(',', execution.IsolatedMutants())).IsEqualTo("-1");
        await Assert.That(settled).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>期限を超えたとき、変異のない対照の実行も終わらなければ、検出に数えず所要時間のない未判定にすること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_while_the_control_run_does_not_finish_is_unjudged_without_a_control_time()
    {
        using var run = Scripted(
            new Dictionary<string, int>
            {
                [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers,
                [ScriptedWorkerRun.IsolatedKey(ControlId)] = ScriptedWorkerRun.NeverAnswers,
            },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(1, Killed())
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.ContendedTimeout(null, BudgetMs)));
        await Assert.That(string.Join(',', execution.IsolatedMutants())).IsEqualTo("-1");
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>期限を超えても再実行が期限内に終われば、遅れは一時のものとして、再実行の判定を採ること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_that_does_not_reproduce_takes_the_verdict_of_the_re_run()
    {
        using var run = Scripted(
            new Dictionary<string, int> { [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived()),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed())
        );
        var settled = new ConcurrentQueue<(int Id, MutantVerdict Verdict)>();

        var execution = await run.ExecuteSettlingAsync(Roster, (id, verdict) => settled.Enqueue((id.Value, verdict)), Resident(1, 0));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(settled.ToArray()).IsEquivalentTo([(1, (MutantVerdict)new MutantVerdict.Killed(AddTest))]);
    }

    /// <summary>probe 上限の超過は時間によらない検出なので、対照の実行も再実行もせず、検出に数えること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_probe_limit_timeout_is_not_re_examined()
    {
        using var run = Scripted(
            [],
            BudgetOnly,
            ScriptedWorkerRun.SharedAnswer(1, new WorkerResponse.RunCompleted(WorkerContract.OutcomeHitLimit, null, 1, 1, true))
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.TimedOut());
        await Assert.That(execution.Calls.Count(call => call.Request == WorkerCall.RunRequest)).IsEqualTo(1);
    }

    /// <summary>隔離 process の実行が期限を超えたときも、同じく対照の実行と再実行で確かめること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task An_isolated_run_that_times_out_is_examined_the_same_way()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0, 1), BudgetMs));
        using var run = Scripted(
            new Dictionary<string, int> { [ScriptedWorkerRun.IsolatedKey(1)] = ScriptedWorkerRun.NeverAnswers },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived())
        );

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.TimedOut());
        await Assert.That(string.Join(',', execution.IsolatedMutants())).IsEqualTo("1,-1,1");
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>対照の実行は、最初の失敗で打ち切らず全テストを走らせるよう worker に求めること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task The_control_run_asks_the_worker_to_run_every_test_even_when_one_fails()
    {
        using var run = Scripted(
            new Dictionary<string, int>
            {
                [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers,
                [ScriptedWorkerRun.IsolatedKey(1)] = ScriptedWorkerRun.NeverAnswers,
            },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived())
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Calls.Single(call => call.MutantId == ControlId).AllFailures).IsTrue();
    }

    /// <summary>競合で判定できなかった変異の所要時間に、対照の実行に費やした時間が入ること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task The_time_spent_on_the_control_run_is_recorded_for_the_contended_mutant()
    {
        using var run = Contended();

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Result.Timings.Single().ElapsedMs).IsGreaterThan(BudgetMs);
    }

    /// <summary>検出行列を求める走行で、競合で判定できなかった変異の行が、求めた全テストを未実行として残すこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_contended_timeout_in_a_detection_matrix_run_leaves_every_planned_test_not_run()
    {
        using var run = Contended();

        var execution = await run.ExecuteDetectionMatrixAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Row(1).Failed).IsEmpty();
        await Assert.That(string.Join(',', execution.Row(1).NotRun.Select(index => index.Value))).IsEqualTo("0,1");
    }

    /// <summary>既定の走行では、競合で判定できなかった変異にも検出行列の行を残さないこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_contended_timeout_in_a_default_run_records_no_detection_row()
    {
        using var run = Contended();

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0, 1));

        await Assert.That(execution.Result.Detections).IsEmpty();
    }

    /// <summary>応えない指示と猶予を決めた台本の実行</summary>
    private static ScriptedWorkerRun Scripted(
        Dictionary<string, int> delays,
        RunDeadlines deadlines,
        params (string Key, WorkerResponse Answer)[] script
    ) => new(delays, script) { Deadlines = deadlines };

    /// <summary>期限を超えた共有の実行と、予算より遅いが期限に収まる変異のない対照の実行を持つ台本の実行</summary>
    private static ScriptedWorkerRun Contended() =>
        Scripted(
            new Dictionary<string, int>
            {
                [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers,
                [ScriptedWorkerRun.IsolatedKey(ControlId)] = ControlDelayMs,
            },
            new RunDeadlines(TimeSpan.FromMilliseconds(ControlDelayMs)),
            ScriptedWorkerRun.IsolatedAnswer(ControlId, Survived()),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed())
        );

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(Order(tests), BudgetMs));

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();

    /// <summary>先頭のテストが失敗した、到達済みの run 応答</summary>
    private static WorkerResponse.RunCompleted Killed() => new(WorkerContract.OutcomeKilled, AddTest, 1, 1, true);

    /// <summary>全テストが成功した、到達済みの run 応答</summary>
    private static WorkerResponse.RunCompleted Survived() => new(WorkerContract.OutcomeSurvived, null, 1, 1, true);
}
