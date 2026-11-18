using Mutation.Infrastructure;
using Mutation.Domain;
using Mutation.Infrastructure.Snapshot;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>前回判定の継承条件の検査</summary>
public sealed class SnapshotReuseFacts
{
    /// <summary>入力の閉包の同一性・ファイル・テスト assembly・被覆集合が不変なら判定が継承されること</summary>
    [Test]
    public async Task Unchanged_inputs_inherit_verdicts()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath), Mutant(1, sourcePath) with { Replacement = "a * b" } };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"), Saved("Survived", null, "a * b"));
        var inherited = SnapshotReuse.Inheritable(document, Closure, mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(2);
        await Assert.That(inherited[new MutantId(0)] is MutantVerdict.Killed { KillerTest: "t1" }).IsTrue();
        await Assert.That(inherited[new MutantId(1)] is MutantVerdict.Survived).IsTrue();
    }

    /// <summary>ファイル内容が変わると継承されないこと</summary>
    [Test]
    public async Task Changed_file_content_inherits_nothing()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath) };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"));
        await File.AppendAllTextAsync(sourcePath, "// changed\n");
        var inherited = SnapshotReuse.Inheritable(document, Closure, mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(0);
    }

    /// <summary>テスト assembly が変わると全て継承されないこと</summary>
    [Test]
    public async Task Changed_test_assembly_inherits_nothing()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath) };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"));
        await File.AppendAllTextAsync(testAssemblyPath, "x");
        var inherited = SnapshotReuse.Inheritable(document, Closure, mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(0);
    }

    /// <summary>入力の閉包の同一性が前回と違えば、ファイルと被覆が前回と同じ変異でも継承されないこと</summary>
    /// <remarks>設定、他のファイル、参照する assembly、analyzer と generator、テストの補助 file の変化は、どれも同一性を変える</remarks>
    [Test]
    public async Task A_different_closure_identity_inherits_nothing_even_when_the_file_and_coverage_match()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath) };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"));
        var inherited = SnapshotReuse.Inheritable(document, "another-closure", mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(0);
    }

    /// <summary>入力の閉包の同一性を示せない側があれば、継承されないこと</summary>
    /// <param name="saved">保存が持つ同一性。前の版が書いた保存や同一性を求めなかった走行の保存は不在</param>
    /// <param name="current">今回の同一性。入力を読めなかったときは不在</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments(null, "closure")]
    [Arguments("closure", null)]
    [Arguments(null, null)]
    public async Task An_unknown_closure_identity_on_either_side_inherits_nothing(string? saved, string? current)
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath) };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1")) with { ClosureIdentity = saved };
        var inherited = SnapshotReuse.Inheritable(document, current, mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(0);
    }

    /// <summary>被覆するテストの集合が変わった変異は継承されないこと</summary>
    [Test]
    public async Task Changed_covering_test_set_inherits_nothing_for_that_mutant()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath), Mutant(1, sourcePath) with { Replacement = "a * b" } };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"), Saved("Survived", null, "a * b"));
        var coverage = new CoverageMap(
            new Dictionary<MutantId, IReadOnlyList<TestIndex>>
            {
                [new MutantId(0)] = [new TestIndex(0)],
                [new MutantId(1)] = [new TestIndex(0), new TestIndex(1)],
            },
            new HashSet<MutantId>(),
            null
        );
        var inherited = SnapshotReuse.Inheritable(document, Closure, mutants, testAssemblyPath, coverage, Roster);
        await Assert.That(inherited.ContainsKey(new MutantId(0))).IsTrue();
        await Assert.That(inherited.ContainsKey(new MutantId(1))).IsFalse();
    }

    /// <summary>変異列が食い違うファイルは丸ごと継承されないこと</summary>
    [Test]
    public async Task Mismatched_mutant_sequence_inherits_nothing_for_that_file()
    {
        var (sourcePath, testAssemblyPath) = Fixture();
        var mutants = new[] { Mutant(0, sourcePath), Mutant(1, sourcePath) with { Replacement = "違う置換" } };
        var document = Document(sourcePath, testAssemblyPath, Saved("Killed", "t1"), Saved("Survived", null, "a * b"));
        var inherited = SnapshotReuse.Inheritable(document, Closure, mutants, testAssemblyPath, Coverage(), Roster);
        await Assert.That(inherited.Count).IsEqualTo(0);
    }

    /// <summary>今回の入力の閉包の同一性として保存に添える値</summary>
    private const string Closure = "closure";

    private static readonly TestRoster Roster = new(["test-0", "test-1"], [0, 0]);

    private static CoverageMap Coverage() =>
        new(
            new Dictionary<MutantId, IReadOnlyList<TestIndex>>
            {
                [new MutantId(0)] = [new TestIndex(0)],
                [new MutantId(1)] = [new TestIndex(0)],
            },
            new HashSet<MutantId>(),
            null
        );

    private static (string SourcePath, string TestAssemblyPath) Fixture()
    {
        var directory = Directory.CreateTempSubdirectory("baseline-facts").FullName;
        var source = Path.Combine(directory, "Target.cs");
        var testAssembly = Path.Combine(directory, "Tests.dll");
        File.WriteAllText(source, "class C { int M(int a, int b) => a + b; }");
        File.WriteAllText(testAssembly, "dll");
        return (source, testAssembly);
    }

    private static Mutant Mutant(int id, string path) =>
        new(new MutantId(id), "op", path, new SourceSpan(1, 1, 1, 5), "a + b", "a - b", false);

    private static SnapshotDocument Document(
        string sourcePath,
        string testAssemblyPath,
        params SnapshotMutant[] mutants
    ) =>
        new(
            SnapshotStore.HashOf(testAssemblyPath),
            CompilationFingerprint: null,
            MutatedAssemblyHash: null,
            CompileErrorIds: [],
            Tests: [new SnapshotTest("test-0", "t0", 1, true, 10)],
            AmbientMutants: [],
            new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
            {
                [sourcePath] = new(SnapshotStore.HashOf(sourcePath), mutants),
            },
            ClosureIdentity: Closure
        );

    private static SnapshotMutant Saved(string status, string? killedBy, string replacement = "a - b") =>
        new("op", 1, 1, 1, 5, replacement, status, killedBy, "a + b", false, CoveringTests: [0], TriggeringTests: []);
}
