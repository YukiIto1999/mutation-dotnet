using Mutation.Protocol;
using Mutation.Domain;
using Mutation.Infrastructure.Workers;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>timeout の対照実行の障害を検出や競合と区別する検査</summary>
[NotInParallel]
public sealed class TestingOrchestratorControlFailureFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>変異の実行時間予算</summary>
    private const double BudgetMs = 300;

    /// <summary>変異のない対照の run を指す連番</summary>
    private const int ControlId = -1;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, "Calc.Sub"], [10, 10]);

    /// <summary>予算をそのまま期限とする設定</summary>
    private static readonly RunDeadlines BudgetOnly = new(TimeSpan.Zero);

    /// <summary>期限を超えたとき、変異のない対照の実行が失敗の応答で終わったら、競合による遅れでなく runner の失敗の未判定にして、再実行もしないこと</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_while_the_control_run_answers_a_failure_is_a_runner_failure_not_contention()
    {
        using var run = Scripted(
            new Dictionary<string, int> { [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers },
            BudgetOnly,
            ScriptedWorkerRun.IsolatedAnswer(ControlId, new WorkerResponse.Failed("the control host broke")),
            ScriptedWorkerRun.IsolatedAnswer(1, Killed())
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Verdict(1) is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure { Detail: var detail } } && detail.Contains("the control host broke", StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Join(',', execution.IsolatedMutants())).IsEqualTo("-1");
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>期限を超えたとき、変異のない対照の実行の process が host を開けなければ、競合による遅れでなく runner の失敗の未判定にすること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_timeout_while_the_control_host_cannot_open_is_a_runner_failure_not_contention()
    {
        using var run = Scripted(
            new Dictionary<string, int> { [ScriptedWorkerRun.SharedKey(1)] = ScriptedWorkerRun.NeverAnswers },
            BudgetOnly,
            ScriptedWorkerRun.InitAnswer(ScriptedWorkerRun.IsolatedInitKey, new WorkerResponse.Failed("no host"))
        );

        var execution = await run.ExecuteAsync(Roster, Resident(1, 0));

        await Assert.That(execution.Verdict(1) is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
        await Assert.That(execution.IsolatedMutants()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>応えない指示と猶予を決めた台本の実行</summary>
    private static ScriptedWorkerRun Scripted(
        Dictionary<string, int> delays,
        RunDeadlines deadlines,
        params (string Key, WorkerResponse Answer)[] script
    ) => new(delays, script) { Deadlines = deadlines };

    /// <summary>共有 worker で実行する計画の組み立て</summary>
    private static MutantPlan Resident(int mutantId, params int[] tests) =>
        new(new MutantId(mutantId), new MutantExecution.Resident(tests.Select(test => new TestIndex(test)).ToArray(), BudgetMs));

    /// <summary>先頭のテストが失敗した到達済みの応答</summary>
    private static WorkerResponse.RunCompleted Killed() => new(WorkerContract.OutcomeKilled, AddTest, 1, 1, true);
}
