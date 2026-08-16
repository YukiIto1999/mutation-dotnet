using Mutation.Shared;
using Mutation.Verifying.Domain;


namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>前回の保存から今回へ継承できる判定の算出</summary>
public static class SnapshotReuse
{
    /// <summary>ファイル・変異列・テスト assembly・被覆テスト集合が前回と一致する変異の判定の収集</summary>
    /// <param name="document">前回の保存。なければ継承なし</param>
    /// <param name="mutants">今回の全変異</param>
    /// <param name="testAssemblyPath">今回のテスト assembly の絶対 path</param>
    /// <param name="coverage">今回の被覆表</param>
    /// <param name="roster">今回の連番順のテストの名簿</param>
    /// <returns>変異の連番から継承する判定への対応</returns>
    public static IReadOnlyDictionary<MutantId, MutantVerdict> Inheritable(
        SnapshotDocument? document,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    )
    {
        var inherited = new Dictionary<MutantId, MutantVerdict>();
        if (document?.Tests is null || document.TestAssemblyHash != SnapshotStore.HashOf(testAssemblyPath))
        {
            return inherited;
        }

        foreach (var group in mutants.GroupBy(m => m.FilePath, StringComparer.Ordinal))
        {
            var current = group.OrderBy(m => m.Id.Value).ToArray();
            if (!TryMatchFile(document, group.Key, current, out var previous))
            {
                continue;
            }

            foreach (var (mutant, saved) in current.Zip(previous.Mutants))
            {
                var coveringUnchanged = SameTestSet(
                    saved.CoveringTests,
                    document.Tests,
                    coverage.CoveringTests(mutant.Id),
                    roster
                );
                if (coveringUnchanged && ToVerdict(saved) is { } verdict)
                {
                    inherited[mutant.Id] = verdict;
                }
            }
        }

        return inherited;
    }

    /// <summary>ファイル内容と変異列が前回と完全一致するときだけの、前回の保存の取り出し</summary>
    private static bool TryMatchFile(
        SnapshotDocument document,
        string filePath,
        Mutant[] current,
        out SnapshotFile previous
    )
    {
        previous = null!;
        if (!document.Files.TryGetValue(filePath, out var saved) || !File.Exists(filePath))
        {
            return false;
        }

        if (saved.Hash != SnapshotStore.HashOf(filePath))
        {
            return false;
        }

        if (current.Length != saved.Mutants.Count || !current.Zip(saved.Mutants).All(pair => Matches(pair.First, pair.Second)))
        {
            return false;
        }

        previous = saved;
        return true;
    }

    /// <summary>保存時と今回の被覆テスト集合の一意識別子での一致判定</summary>
    private static bool SameTestSet(
        IReadOnlyList<int>? savedIndexes,
        IReadOnlyList<SnapshotTest> savedTests,
        IReadOnlyList<TestIndex> currentIndexes,
        TestRoster roster
    )
    {
        if (savedIndexes is null)
        {
            return false;
        }

        var saved = savedIndexes.Select(i => savedTests[i].Id).ToHashSet(StringComparer.Ordinal);
        return saved.SetEquals(currentIndexes.Select(roster.Id));
    }

    /// <summary>今回の変異と保存された変異の同一性の判定</summary>
    private static bool Matches(Mutant current, SnapshotMutant previous) =>
        current.OperatorName == previous.Operator
        && current.Span == new SourceSpan(previous.Line, previous.Column, previous.EndLine, previous.EndColumn)
        && current.Replacement == previous.Replacement;

    /// <summary>継承してよい判定への復元。Killed / Survived / Timeout 以外は不在</summary>
    private static MutantVerdict? ToVerdict(SnapshotMutant previous)
    {
        var verdict = VerdictNames.Restore(previous.Status, previous.KilledBy);
        return verdict is MutantVerdict.Killed or MutantVerdict.Survived or MutantVerdict.TimedOut ? verdict : null;
    }
}
