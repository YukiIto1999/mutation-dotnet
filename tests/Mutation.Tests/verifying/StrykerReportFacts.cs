using System.Text.Json;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Reports;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>互換 schema の報告の書き出しの検査</summary>
public sealed class StrykerReportFacts
{
    /// <summary>報告内の相対 path の基準</summary>
    private const string ProjectRoot = "/repo";

    /// <summary>連番順の三件のテストの素性</summary>
    private static readonly TestCaseInfo[] Tests =
    [
        new(new TestIndex(0), "Calc.Add", 1, true),
        new(new TestIndex(1), "Calc.Sub", 1, true),
        new(new TestIndex(2), "Calc.Mul", 1, true),
    ];

    /// <summary>検出行列の行を持つ変異が失敗した全テストを killedBy へ書き計画したテストを coveredBy へ書くこと</summary>
    /// <remarks>二件のテストが検出する変異と一件だけが検出する変異と生存した変異の対比</remarks>
    [Test]
    public async Task Detection_rows_write_every_failing_test_to_killed_by()
    {
        var mutants = await WriteAsync(
            new Dictionary<MutantId, MutantVerdict>
            {
                [new MutantId(0)] = new MutantVerdict.Killed("Calc.Sub"),
                [new MutantId(1)] = new MutantVerdict.Killed("Calc.Add"),
                [new MutantId(2)] = new MutantVerdict.Survived(),
            },
            new Dictionary<MutantId, DetectionRow>
            {
                [new MutantId(0)] = Row(planned: [0, 1, 2], failed: [1, 2], notRun: []),
                [new MutantId(1)] = Row(planned: [0, 1], failed: [0], notRun: []),
                [new MutantId(2)] = Row(planned: [2], failed: [], notRun: []),
            }
        );

        await Assert.That(Strings(mutants[0], "killedBy")).IsEqualTo("Calc.Sub,Calc.Mul");
        await Assert.That(Strings(mutants[0], "coveredBy")).IsEqualTo("Calc.Add,Calc.Sub,Calc.Mul");
        await Assert.That(mutants[0].GetProperty("testsCompleted").GetInt32()).IsEqualTo(3);
        await Assert.That(Strings(mutants[1], "killedBy")).IsEqualTo("Calc.Add");
        await Assert.That(mutants[1].GetProperty("testsCompleted").GetInt32()).IsEqualTo(2);
        await Assert.That(Strings(mutants[2], "killedBy")).IsEqualTo("");
        await Assert.That(Strings(mutants[2], "coveredBy")).IsEqualTo("Calc.Mul");
        await Assert.That(mutants.Any(m => m.TryGetProperty("statusReason", out _))).IsFalse();
    }

    /// <summary>未実行のテストが残った行が完了数を減らし未実行のテストを statusReason で名指しすること</summary>
    [Test]
    public async Task Incomplete_rows_name_the_tests_that_did_not_run()
    {
        var mutants = await WriteAsync(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.TimedOut() },
            new Dictionary<MutantId, DetectionRow> { [new MutantId(0)] = Row(planned: [0, 1, 2], failed: [], notRun: [1, 2]) }
        );

        await Assert.That(Strings(mutants[0], "coveredBy")).IsEqualTo("Calc.Add,Calc.Sub,Calc.Mul");
        await Assert.That(mutants[0].GetProperty("testsCompleted").GetInt32()).IsEqualTo(1);
        await Assert.That(mutants[0].GetProperty("statusReason").GetString())
            .IsEqualTo("Detection matrix incomplete; not run: Calc.Sub, Calc.Mul");
    }

    /// <summary>失敗したテストを特定できない検出が行を持っても従来の検出の目印を killedBy に残すこと</summary>
    [Test]
    public async Task Killed_rows_without_identified_failures_keep_the_killer_marker()
    {
        var mutants = await WriteAsync(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.Killed("(process crash)") },
            new Dictionary<MutantId, DetectionRow> { [new MutantId(0)] = Row(planned: [0], failed: [], notRun: [0]) }
        );

        await Assert.That(Strings(mutants[0], "killedBy")).IsEqualTo("(process crash)");
        await Assert.That(mutants[0].GetProperty("testsCompleted").GetInt32()).IsEqualTo(0);
    }

    /// <summary>検出行列の行がない変異が最初の検出テストだけを書き行列の欄を書かないこと</summary>
    [Test]
    public async Task Mutants_without_a_row_keep_the_first_failure_report()
    {
        var mutants = await WriteAsync(
            new Dictionary<MutantId, MutantVerdict>
            {
                [new MutantId(0)] = new MutantVerdict.Killed("Calc.Sub"),
                [new MutantId(1)] = new MutantVerdict.Survived(),
            },
            new Dictionary<MutantId, DetectionRow>()
        );

        await Assert.That(Strings(mutants[0], "killedBy")).IsEqualTo("Calc.Sub");
        await Assert.That(Strings(mutants[1], "killedBy")).IsEqualTo("");
        await Assert.That(
                mutants
                    .SelectMany(m => m.EnumerateObject().Select(p => p.Name))
                    .Intersect(["coveredBy", "testsCompleted", "statusReason"])
            )
            .IsEmpty();
    }

    /// <summary>指定の判定と行を持つ対象一件の報告の書き出しと変異の連番順の読み戻し</summary>
    /// <param name="verdicts">変異の連番から確定結果への対応</param>
    /// <param name="detections">変異の連番から検出行列の行への対応</param>
    /// <returns>書き出した報告の変異の列</returns>
    private static async Task<JsonElement[]> WriteAsync(
        Dictionary<MutantId, MutantVerdict> verdicts,
        Dictionary<MutantId, DetectionRow> detections
    )
    {
        var mutants = verdicts
            .Keys.Select(id => new Mutant(id, "BinaryOperatorMutator", "/repo/src/A.cs", new SourceSpan(1, 1, 1, 5), "a + b", "a - b", false))
            .ToArray();
        var target = new TargetResult("T", mutants, verdicts, Tests, TargetTimings.None, [], 0, detections);
        var result = new MutationRunResult([new TargetOutcome.Completed(target)], new PhaseTimings(0, 0, 0, 0, 0, 0));
        var path = Path.Combine(Path.GetTempPath(), $"mutation-report-{Guid.NewGuid():N}.json");
        try
        {
            StrykerReport.Write(result, ProjectRoot, path);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            return document
                .RootElement.GetProperty("files")
                .GetProperty("src/A.cs")
                .GetProperty("mutants")
                .EnumerateArray()
                .Select(m => m.Clone())
                .ToArray();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>名簿の連番による検出行列の行の組み立て</summary>
    /// <param name="planned">実行を求めたテストの連番</param>
    /// <param name="failed">失敗したテストの連番</param>
    /// <param name="notRun">結果を観測できなかったテストの連番</param>
    /// <returns>指定の連番を持つ行</returns>
    private static DetectionRow Row(int[] planned, int[] failed, int[] notRun) =>
        new(Indexes(planned), Indexes(failed), Indexes(notRun));

    /// <summary>整数からのテスト連番の列</summary>
    /// <param name="values">連番の値</param>
    /// <returns>指定順のテスト連番の列</returns>
    private static TestIndex[] Indexes(int[] values) => values.Select(v => new TestIndex(v)).ToArray();

    /// <summary>文字列の配列の欄の順序を保つ連結</summary>
    /// <param name="mutant">報告の変異一件</param>
    /// <param name="name">欄の名前</param>
    /// <returns>要素をコンマで区切った文字列</returns>
    private static string Strings(JsonElement mutant, string name) =>
        string.Join(',', mutant.GetProperty(name).EnumerateArray().Select(e => e.GetString()));
}
