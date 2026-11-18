using Mutation.Protocol;
using Mutation.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>取り消しの要求を受けた実行が、確定した判定を残し、worker を止め、残りを未判定にする境界の検査</summary>
/// <remarks>応えない worker を台本で作り、その指示が worker に届いた時点で取り消す</remarks>
public sealed class TestingOrchestratorCancellationFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>worker が run を受けてから 応答を読み終えたと見なして取り消すまでの待ち。出力の待ちより十分に短い</summary>
    private static readonly TimeSpan AnswerSettle = TimeSpan.FromMilliseconds(200);

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, "Calc.Sub"], [10, 10]);

    /// <summary>実行の途中で取り消すと、確定した判定を残し、実行中と取りかからなかった変異を未判定にして worker を止めることの検査</summary>
    /// <remarks>一件目は確定、二件目は応えない実行中、三件目は取りかからない変異</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Cancelling_mid_run_keeps_settled_verdicts_and_leaves_the_rest_unjudged()
    {
        using var run = new ScriptedWorkerRun(
            new Dictionary<string, int> { [ScriptedWorkerRun.SharedKey(2)] = ScriptedWorkerRun.NeverAnswers },
            ScriptedWorkerRun.SharedAnswer(1, Killed()),
            ScriptedWorkerRun.SharedAnswer(2, Survived()),
            ScriptedWorkerRun.SharedAnswer(3, Survived())
        );
        var settled = new ConcurrentQueue<int>();

        var execution = await run.ExecuteCancellingAsync(
            Roster,
            calls => calls.Any(call => call.Request == WorkerCall.RunRequest && call.MutantId == 2),
            (id, _) => settled.Enqueue(id.Value),
            Resident(1, 0),
            Resident(2, 0),
            Resident(3, 1)
        );

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed(AddTest));
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()));
        await Assert.That(execution.Verdict(3)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()));
        await Assert.That(settled.ToArray()).IsEquivalentTo([1]);
        await Assert.That(execution.Calls.Any(call => call.MutantId == 3)).IsFalse();
        await Assert.That(execution.Result.Timings.Select(timing => timing.MutantId.Value).ToArray()).IsEquivalentTo([1]);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>隔離 process の実行中に取り消すと、その変異を未判定にして process を止めることの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Cancelling_an_isolated_run_marks_it_interrupted_and_stops_its_process()
    {
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Isolated(Order(0, 1), BudgetMs));
        using var run = new ScriptedWorkerRun(
            new Dictionary<string, int> { [ScriptedWorkerRun.IsolatedKey(1)] = ScriptedWorkerRun.NeverAnswers },
            ScriptedWorkerRun.IsolatedAnswer(1, Survived())
        );

        var execution = await run.ExecuteCancellingAsync(
            Roster,
            calls => calls.Any(call => call is { Request: WorkerCall.RunRequest, Mode: WorkerCall.IsolatedMode }),
            (_, _) => { },
            plan
        );

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()));
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>実行の前に取り消されていると、worker を起動せず全変異を実行に至らなかった未判定にすることの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_run_cancelled_before_it_starts_leaves_every_mutant_unjudged_without_a_worker()
    {
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.SharedAnswer(1, Killed()),
            ScriptedWorkerRun.IsolatedAnswer(2, Killed())
        );
        var isolated = new MutantPlan(new MutantId(2), new MutantExecution.Isolated(Order(0), BudgetMs));

        var execution = await run.ExecuteAlreadyCancelledAsync(Roster, Resident(1, 0), isolated);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()));
        await Assert.That(execution.Verdict(2)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()));
        await Assert.That(execution.Calls).IsEmpty();
    }

    /// <summary>応答を読んだ後の出力の回収の間に取り消すと、その応答を採らず、worker を止めることの検査</summary>
    /// <remarks>worker は応答を書いた後で、完了の目印の前に出力を書くので、応答を読み終えた後の待ちの間に取り消しが届く</remarks>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_cancellation_while_the_answer_is_being_collected_discards_it_and_stops_the_worker()
    {
        using var run = ScriptedWorkerRun.WithLateOutput(
            new Dictionary<string, string[]> { [ScriptedWorkerRun.SharedKey(1)] = ["written after the answer"] },
            ScriptedWorkerRun.SharedAnswer(1, Killed())
        );
        Stopwatch? sinceReceived = null;

        var execution = await run.ExecuteCancellingAsync(
            Roster,
            calls =>
            {
                if (!calls.Any(call => call.Request == WorkerCall.RunRequest))
                {
                    return false;
                }

                sinceReceived ??= Stopwatch.StartNew();
                return sinceReceived.Elapsed >= AnswerSettle;
            },
            (_, _) => { },
            Resident(1, 0)
        );

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()));
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

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
