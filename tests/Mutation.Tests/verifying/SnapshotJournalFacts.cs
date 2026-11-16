using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Snapshot;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定の確定ごとの追記の記録と、止まった走行からの復元の検査</summary>
public sealed class SnapshotJournalFacts
{
    /// <summary>追記した判定が、保存の前に process が殺されても、次の継承で使えること</summary>
    /// <remarks>連番 0 は走行の前から検出、1 は追記で生存、2 は追記のない未判定</remarks>
    [Test]
    public async Task Verdicts_appended_before_the_run_died_are_inherited_by_the_next_run()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var journal = snapshots.Begin(
            fixture.Request(
                (0, new MutantVerdict.Killed(SnapshotFixture.TestName)),
                (1, SnapshotFixture.Pending),
                (2, SnapshotFixture.Pending)
            )
        );

        journal.Append(new MutantId(1), new MutantVerdict.Survived());
        journal.Dispose();

        var inherited = Inherit(fixture, snapshots);
        await Assert.That(inherited.Keys.Select(id => id.Value).Order().ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(inherited[new MutantId(0)]).IsEqualTo(new MutantVerdict.Killed(SnapshotFixture.TestName));
        await Assert.That(inherited[new MutantId(1)]).IsEqualTo(new MutantVerdict.Survived());
    }

    /// <summary>判定を確定するたびに記録が一行ずつ増え、実行の途中でも読めること</summary>
    [Test]
    public async Task The_journal_gains_one_line_per_verdict_while_the_run_is_still_going()
    {
        using var fixture = new SnapshotFixture();
        using var journal = new Snapshots().Begin(
            fixture.Request((0, SnapshotFixture.Pending), (1, SnapshotFixture.Pending), (2, SnapshotFixture.Pending))
        );

        var lines = new List<int> { LineCount(fixture) };
        journal.Append(new MutantId(2), new MutantVerdict.Killed(SnapshotFixture.TestName));
        lines.Add(LineCount(fixture));
        journal.Append(new MutantId(0), new MutantVerdict.TimedOut());
        lines.Add(LineCount(fixture));

        await Assert.That(lines.ToArray()).IsEquivalentTo([1, 2, 3]);
    }

    /// <summary>書き終える前に殺された最後の行を、捨てて残りを使うこと</summary>
    [Test]
    public async Task A_torn_last_line_is_ignored_and_the_complete_lines_are_kept()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        var journal = snapshots.Begin(fixture.Request((0, SnapshotFixture.Pending), (1, SnapshotFixture.Pending), (2, SnapshotFixture.Pending)));
        journal.Append(new MutantId(0), new MutantVerdict.Survived());
        journal.Dispose();
        await File.AppendAllTextAsync(fixture.JournalPath, "{\"file\":\"" + fixture.SourcePath + "\",\"index\":1,\"sta");

        var inherited = Inherit(fixture, snapshots);

        await Assert.That(inherited.Keys.Select(id => id.Value).ToArray()).IsEquivalentTo([0]);
    }

    /// <summary>全件の判定を確定して保存に畳むと、記録を取り除き、全体の再構築に一致する保存を残すこと</summary>
    [Test]
    public async Task Commit_of_settled_verdicts_removes_the_journal_and_matches_in_full()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        using var journal = snapshots.Begin(fixture.Request((0, SnapshotFixture.Pending), (1, SnapshotFixture.Pending), (2, SnapshotFixture.Pending)));

        journal.Commit(fixture.Request((0, new MutantVerdict.Killed("a")), (1, new MutantVerdict.Survived()), (2, new MutantVerdict.TimedOut())));

        await Assert.That(File.Exists(fixture.JournalPath)).IsFalse();
        var rebuilt = snapshots.TryRebuild(fixture.SnapshotPath, SnapshotFixture.Fingerprint, fixture.TestAssemblyPath, fixture.MutatedAssemblyPath, "T");
        await Assert.That(rebuilt).IsNotNull();
        await Assert.That(rebuilt!.Verdicts[new MutantId(2)]).IsEqualTo(new MutantVerdict.TimedOut());
    }

    /// <summary>未判定が残る保存が、全体の再構築に一致せず、確定した判定だけを継承に残すこと</summary>
    [Test]
    public async Task Commit_with_an_unjudged_mutant_never_matches_in_full_and_keeps_the_settled_verdicts()
    {
        using var fixture = new SnapshotFixture();
        var snapshots = new Snapshots();
        using var journal = snapshots.Begin(fixture.Request((0, SnapshotFixture.Pending), (1, SnapshotFixture.Pending), (2, SnapshotFixture.Pending)));

        journal.Commit(fixture.Request((0, new MutantVerdict.Killed("a")), (1, new MutantVerdict.Survived()), (2, SnapshotFixture.Pending)));

        await Assert.That(File.Exists(fixture.JournalPath)).IsFalse();
        var rebuilt = snapshots.TryRebuild(fixture.SnapshotPath, SnapshotFixture.Fingerprint, fixture.TestAssemblyPath, fixture.MutatedAssemblyPath, "T");
        await Assert.That(rebuilt).IsNull();
        await Assert.That(Inherit(fixture, snapshots).Keys.Select(id => id.Value).Order().ToArray()).IsEquivalentTo([0, 1]);
    }

    /// <summary>前の走行が残した記録を、新しい走行の開始が置き換えること</summary>
    [Test]
    public async Task Begin_replaces_a_journal_left_by_an_earlier_run()
    {
        using var fixture = new SnapshotFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.JournalPath)!);
        await File.WriteAllTextAsync(fixture.JournalPath, "stale\nlines\n");

        using var journal = new Snapshots().Begin(fixture.Request((0, SnapshotFixture.Pending), (1, SnapshotFixture.Pending), (2, SnapshotFixture.Pending)));

        var lines = await File.ReadAllLinesAsync(fixture.JournalPath);
        await Assert.That(lines.Length).IsEqualTo(1);
        await Assert.That(lines[0]).StartsWith("{\"begin\":");
    }

    /// <summary>この環境の保存から継承できる判定</summary>
    private static IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(SnapshotFixture fixture, Snapshots snapshots) =>
        snapshots.Inherit(fixture.SnapshotPath, fixture.Mutants, fixture.TestAssemblyPath, fixture.Coverage, fixture.Roster);

    /// <summary>追記の記録の行数</summary>
    private static int LineCount(SnapshotFixture fixture) => File.ReadAllLines(fixture.JournalPath).Length;
}
