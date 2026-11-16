using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;

namespace Mutation.Tests;

/// <summary>一時 directory に置いた、変異三件を持つ一対象の保存の検査環境</summary>
/// <remarks>変異は一つのソースに置き、どれも名簿の先頭のテストが被覆する。保存の継承と再構築が読む実ファイルを持つ</remarks>
internal sealed class SnapshotFixture : IDisposable
{
    /// <summary>この環境の一時 directory</summary>
    private readonly string directory = Directory.CreateTempSubdirectory("mutation-snapshot-facts").FullName;

    /// <summary>名簿の先頭のテストの表示名</summary>
    public const string TestName = "Calc.Add";

    /// <summary>生成入力の指紋として保存に添える値</summary>
    public const string Fingerprint = "fingerprint";

    /// <summary>変異三件を持つ環境の作成</summary>
    public SnapshotFixture()
    {
        File.WriteAllText(SourcePath, "class C { int M(int a, int b) => a + b; }");
        File.WriteAllText(TestAssemblyPath, "tests");
        File.WriteAllText(MutatedAssemblyPath, "mutated");
        Mutants =
        [
            Mutant(0, "a - b"),
            Mutant(1, "a * b"),
            Mutant(2, "a / b"),
        ];
        Roster = new TestRoster(["test-0"], [10]);
        Coverage = new CoverageMap(
            Mutants.ToDictionary(m => m.Id, _ => (IReadOnlyList<TestIndex>)[new TestIndex(0)]),
            new HashSet<MutantId>(),
            null
        );
        Baseline = new BaselineProfile([new TestCaseInfo(new TestIndex(0), TestName, 1, true)], Roster, Coverage);
    }

    /// <summary>変異を置いたソースの絶対 path</summary>
    public string SourcePath => Path.Combine(directory, "Target.cs");

    /// <summary>テスト assembly の絶対 path</summary>
    public string TestAssemblyPath => Path.Combine(directory, "Tests.dll");

    /// <summary>変異 assembly の絶対 path</summary>
    public string MutatedAssemblyPath => Path.Combine(directory, "Target.dll");

    /// <summary>保存 file の絶対 path</summary>
    public string SnapshotPath => Path.Combine(directory, "work", "T", "snapshot.json");

    /// <summary>追記の記録の絶対 path</summary>
    public string JournalPath => Path.Combine(directory, "work", "T", "snapshot.verdicts.jsonl");

    /// <summary>連番 0 から 2 の変異</summary>
    public IReadOnlyList<Mutant> Mutants { get; }

    /// <summary>テスト一件の名簿</summary>
    public TestRoster Roster { get; }

    /// <summary>三件の変異の被覆表</summary>
    public CoverageMap Coverage { get; }

    /// <summary>テスト一件の基準実行の観測</summary>
    public BaselineProfile Baseline { get; }

    /// <summary>指定の判定を持つ保存の内容</summary>
    /// <param name="verdicts">変異の連番から判定への対応</param>
    /// <returns>この環境の対象の保存の内容</returns>
    public SnapshotSaveRequest Request(params (int Id, MutantVerdict Verdict)[] verdicts) =>
        new(
            Mutants,
            verdicts.ToDictionary(pair => new MutantId(pair.Id), pair => pair.Verdict),
            Baseline,
            Fingerprint,
            TestAssemblyPath,
            MutatedAssemblyPath,
            new HashSet<MutantId>(),
            SnapshotPath
        );

    /// <summary>worker の起動情報。判定の検査では起動しないので path だけを持つ</summary>
    public WorkerLaunchPlan Launch => new("worker.dll", TestAssemblyPath, directory, "Target", directory);

    /// <summary>未判定の三件目までを置いた保存の内容に使う未判定</summary>
    public static MutantVerdict Pending => new MutantVerdict.Unjudged(new UnjudgedCause.NotRun());

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>連番と置換だけが違う、同じ範囲の変異</summary>
    private Mutant Mutant(int id, string replacement) =>
        new(new MutantId(id), "BinaryOperatorMutator", SourcePath, new SourceSpan(1, 30, 1, 35), "a + b", replacement, false);
}
