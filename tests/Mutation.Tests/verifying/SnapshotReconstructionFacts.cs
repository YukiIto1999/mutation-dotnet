

using Mutation.Mutating.Infrastructure;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Snapshot;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>完全一致短絡の判定と再構築の検査</summary>
public sealed class SnapshotReconstructionFacts
{
    /// <summary>指紋と assembly hash が全て一致したときだけ完全一致になること</summary>
    [Test]
    public async Task Full_match_requires_fingerprint_and_hashes()
    {
        var (testAssembly, mutatedAssembly) = Artifacts();
        var document = Document(testAssembly, mutatedAssembly) with { CompilationFingerprint = "fp" };
        await Assert.That(SnapshotReconstruction.IsFullMatch(document, "fp", testAssembly, mutatedAssembly)).IsTrue();
        await Assert.That(SnapshotReconstruction.IsFullMatch(document, "other", testAssembly, mutatedAssembly)).IsFalse();
        await Assert.That(SnapshotReconstruction.IsFullMatch(null, "fp", testAssembly, mutatedAssembly)).IsFalse();
        await File.AppendAllTextAsync(mutatedAssembly, "x");
        await Assert.That(SnapshotReconstruction.IsFullMatch(document, "fp", testAssembly, mutatedAssembly)).IsFalse();
    }

    /// <summary>保存から変異とその判定が採番順に組み立て直されること</summary>
    [Test]
    public async Task Rebuild_restores_mutants_and_verdicts()
    {
        var (testAssembly, mutatedAssembly) = Artifacts();
        var document = Document(testAssembly, mutatedAssembly);
        var result = SnapshotReconstruction.Rebuild(document, "Target");
        await Assert.That(result.Mutants.Count).IsEqualTo(2);
        await Assert.That(result.Verdicts[new MutantId(0)] is MutantVerdict.Killed { KillerTest: "t1" }).IsTrue();
        await Assert.That(result.Verdicts[new MutantId(1)] is MutantVerdict.Survived).IsTrue();
        await Assert.That(result.Tests.Count).IsEqualTo(1);
        await Assert.That(result.Mutants[0].Span.Line).IsEqualTo(1);
    }

    private static (string TestAssembly, string MutatedAssembly) Artifacts()
    {
        var directory = Directory.CreateTempSubdirectory("snapshot-facts").FullName;
        var testAssembly = Path.Combine(directory, "Tests.dll");
        var mutatedAssembly = Path.Combine(directory, "Target.dll");
        File.WriteAllText(testAssembly, "tests");
        File.WriteAllText(mutatedAssembly, "mutated");
        return (testAssembly, mutatedAssembly);
    }

    private static SnapshotDocument Document(string testAssembly, string mutatedAssembly) =>
        new(
            SnapshotStore.HashOf(testAssembly),
            CompilationFingerprint: null,
            SnapshotStore.HashOf(mutatedAssembly),
            CompileErrorIds: [],
            Tests: [new SnapshotTest("t-0", "t0", 1, true, 10)],
            AmbientMutants: [],
            new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
            {
                ["a.cs"] = new(
                    "hash",
                    [
                        new SnapshotMutant("op", 1, 1, 1, 5, "a - b", "Killed", "t1", "a + b", false, [0], []),
                        new SnapshotMutant("op", 2, 1, 2, 5, "a * b", "Survived", null, "a + b", false, [0], []),
                    ]
                ),
            }
        );
}
