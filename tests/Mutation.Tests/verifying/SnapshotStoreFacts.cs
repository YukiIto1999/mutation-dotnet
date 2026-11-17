using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Snapshot;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>保存の書き出しと読み戻しの検査</summary>
public sealed class SnapshotStoreFacts
{
    /// <summary>書き出し先の directory が無くても作って書き、書いた保存を読み戻せること</summary>
    [Test]
    public async Task Write_creates_the_missing_directories_and_the_snapshot_reads_back()
    {
        using var fixture = new SnapshotFixture();
        var path = Path.Combine(Path.GetDirectoryName(fixture.SnapshotPath)!, "deeper", "snapshot.json");

        SnapshotStore.Write(DescribeAll(fixture, new MutantVerdict.Survived()), path);

        var loaded = SnapshotStore.Load(path);
        await Assert.That(loaded).IsNotNull();
        await Assert.That(loaded!.Files[fixture.SourcePath].Mutants.Select(mutant => mutant.Status).ToArray())
            .IsEquivalentTo(["Survived", "Survived", "Survived"]);
    }

    /// <summary>保存を書き直しても、前の保存を開いていた読み手には前の保存がそのまま読めること</summary>
    [Test]
    public async Task Write_leaves_a_reader_of_the_earlier_snapshot_with_the_whole_earlier_content()
    {
        using var fixture = new SnapshotFixture();
        SnapshotStore.Write(DescribeAll(fixture, new MutantVerdict.Survived()), fixture.SnapshotPath);
        var earlier = await File.ReadAllTextAsync(fixture.SnapshotPath);
        using var reader = new FileStream(fixture.SnapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        SnapshotStore.Write(DescribeAll(fixture, new MutantVerdict.TimedOut()), fixture.SnapshotPath);

        using var text = new StreamReader(reader);
        await Assert.That(await text.ReadToEndAsync()).IsEqualTo(earlier);
    }

    /// <summary>判定を確定できなかった変異を持つ保存が 生成入力の指紋も変異 assembly の hash も持たないこと</summary>
    [Test]
    public async Task A_save_with_an_unjudged_mutant_carries_neither_the_fingerprint_nor_the_assembly_hash()
    {
        using var fixture = new SnapshotFixture();

        var document = DescribeAll(fixture, SnapshotFixture.Pending);

        await Assert.That(document.CompilationFingerprint).IsNull();
        await Assert.That(document.MutatedAssemblyHash).IsNull();
    }

    /// <summary>全変異が同じ判定の保存の内容</summary>
    private static SnapshotDocument DescribeAll(SnapshotFixture fixture, MutantVerdict verdict) =>
        SnapshotStore.Describe(
            fixture.Mutants,
            fixture.Mutants.ToDictionary(mutant => mutant.Id, _ => verdict),
            fixture.Baseline.Tests,
            fixture.Coverage,
            new SnapshotContext(
                fixture.TestAssemblyPath,
                SnapshotFixture.Fingerprint,
                fixture.MutatedAssemblyPath,
                new HashSet<MutantId>(),
                fixture.Roster
            )
        );
}
