using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>隔離 process の実行が 検出行列の行を残す条件の検査</summary>
public sealed class TestingOrchestratorRowFacts
{
    /// <summary>名簿の先頭のテスト名</summary>
    private const string AddTest = "Calc.Add";

    /// <summary>名簿の二番目のテスト名</summary>
    private const string SubTest = "Calc.Sub";

    /// <summary>台本 worker の即時応答に対する実行時間予算</summary>
    private const double BudgetMs = 30_000;

    /// <summary>二件のテストの名簿</summary>
    private static readonly TestRoster Roster = new([AddTest, SubTest], [10, 10]);

    /// <summary>隔離 process の実行が 検出行列の行を残すのは 行列を求めた走行だけであること</summary>
    /// <param name="matrix">検出行列を求める走行か</param>
    /// <param name="rows">残る行の数</param>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    [Arguments(true, 1)]
    [Arguments(false, 0)]
    public async Task An_isolated_run_records_a_detection_row_only_when_the_matrix_is_requested(bool matrix, int rows)
    {
        var plan = new MutantPlan(
            new MutantId(1),
            new MutantExecution.Isolated([new TestIndex(0), new TestIndex(1)], BudgetMs)
        );
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.IsolatedAnswer(
                1,
                new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, AddTest, 2, 1, true, [AddTest], [AddTest, SubTest])
            )
        );

        var execution = matrix ? await run.ExecuteDetectionMatrixAsync(Roster, plan) : await run.ExecuteAsync(Roster, plan);

        await Assert.That(execution.Result.Detections.Count).IsEqualTo(rows);
    }
}
