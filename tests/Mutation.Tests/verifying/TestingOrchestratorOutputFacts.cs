using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>worker が実 stdout へ書いた行の回収の検査</summary>
/// <remarks>台本の worker が応答を書いた後で完了の目印の前に出力を書く 応答が先に読まれる状況を作る</remarks>
public sealed class TestingOrchestratorOutputFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, "Calc.Sub"], [10, 10]);

    /// <summary>テスト名を返さない検出が 完了の目印より前に worker が書いた出力から 失敗したテストの名前を得ること</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_kill_without_a_test_name_takes_it_from_the_output_written_before_the_request_was_marked_done()
    {
        using var run = ScriptedWorkerRun.WithLateOutput(
            new Dictionary<string, string[]> { [ScriptedWorkerRun.SharedKey(1)] = ["  failed Calc.Late (12ms)"] },
            ScriptedWorkerRun.SharedAnswer(1, new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, null, 1, 1, true))
        );
        var plan = new MutantPlan(new MutantId(1), new MutantExecution.Resident([new TestIndex(0)], BudgetMs));

        var execution = await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Verdict(1)).IsEqualTo(new MutantVerdict.Killed("Calc.Late"));
    }
}
