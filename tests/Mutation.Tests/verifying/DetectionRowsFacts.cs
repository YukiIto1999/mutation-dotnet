using Mutation.Protocol;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Workers;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>run の応答から検出行列の一行を導く変換の検査</summary>
public sealed class DetectionRowsFacts
{
    /// <summary>三件のテストの名簿</summary>
    private static readonly TestRoster Roster = new(["Calc.Add", "Calc.Sub", "Calc.Mul"], [10, 10, 10]);

    /// <summary>計画した全テストの結果がそろった応答が失敗した全テストを観測順に持つ完全な行になること</summary>
    [Test]
    public async Task Fully_observed_run_keeps_every_failed_test_in_observed_order()
    {
        var response = Completed(failed: ["Calc.Mul", "Calc.Add"], completed: ["Calc.Add", "Calc.Sub", "Calc.Mul"]);

        var row = DetectionRows.From(Order(0, 1, 2), Roster, response);

        await Assert.That(Indexes(row.Planned)).IsEqualTo("0,1,2");
        await Assert.That(Indexes(row.Failed)).IsEqualTo("2,0");
        await Assert.That(row.NotRun).IsEmpty();
        await Assert.That(row.IsComplete).IsTrue();
    }

    /// <summary>結果を観測できなかったテストが計画の順で未実行として区別されること</summary>
    [Test]
    public async Task Tests_without_an_observed_result_are_reported_as_not_run()
    {
        var response = Completed(failed: ["Calc.Sub"], completed: ["Calc.Sub"]);

        var row = DetectionRows.From(Order(2, 0, 1), Roster, response);

        await Assert.That(Indexes(row.Failed)).IsEqualTo("1");
        await Assert.That(Indexes(row.NotRun)).IsEqualTo("2,0");
        await Assert.That(row.IsComplete).IsFalse();
    }

    /// <summary>応答のない run と全件の記録を持たない応答が全テストを未実行とする行になること</summary>
    /// <param name="answered">記録を持たない完了応答があったか</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Runs_without_a_full_record_leave_every_planned_test_not_run(bool answered)
    {
        WorkerResponse? response = answered
            ? new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, "Calc.Add", 1, 1)
            : null;

        var row = DetectionRows.From(Order(0, 1), Roster, response);

        await Assert.That(row.Failed).IsEmpty();
        await Assert.That(Indexes(row.NotRun)).IsEqualTo("0,1");
    }

    /// <summary>二つの run の行の合成が計画と失敗の和を持ち、どちらかで観測したテストを未実行としないこと</summary>
    [Test]
    public async Task Merged_rows_join_plans_and_failures_and_keep_only_tests_unobserved_in_both_runs()
    {
        var shared = new DetectionRow(Order(0, 1), Order(0), Order(1));
        var isolated = new DetectionRow(Order(0, 1, 2), Order(0, 2), Order(0));

        var row = DetectionRows.Merge(shared, isolated);

        await Assert.That(Indexes(row.Planned)).IsEqualTo("0,1,2");
        await Assert.That(Indexes(row.Failed)).IsEqualTo("0,2");
        await Assert.That(row.IsComplete).IsTrue();
        await Assert.That(DetectionRows.Merge(new DetectionRow(Order(1), [], Order(1)), isolated with { NotRun = Order(1) }).NotRun)
            .IsEquivalentTo(Order(1));
    }

    /// <summary>全件の記録を持つ完了応答の組み立て</summary>
    /// <param name="failed">失敗したテストの識別子</param>
    /// <param name="completed">結果まで観測したテストの識別子</param>
    /// <returns>全件記録の run の応答</returns>
    private static WorkerResponse.RunCompleted Completed(string[] failed, string[] completed) =>
        new(
            failed.Length > 0 ? WorkerContract.OutcomeKilled : WorkerContract.OutcomeSurvived,
            failed.FirstOrDefault(),
            completed.Length,
            1,
            true,
            failed,
            completed
        );

    /// <summary>名簿の連番からのテスト列の組み立て</summary>
    /// <param name="tests">名簿の連番</param>
    /// <returns>指定順のテスト連番の列</returns>
    private static TestIndex[] Order(params int[] tests) => tests.Select(test => new TestIndex(test)).ToArray();

    /// <summary>テスト連番の列の順序を保つ連結</summary>
    /// <param name="indexes">テスト連番の列</param>
    /// <returns>連番をコンマで区切った文字列</returns>
    private static string Indexes(IEnumerable<TestIndex> indexes) => string.Join(',', indexes.Select(i => i.Value));
}
