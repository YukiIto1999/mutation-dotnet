using Mutation.Shared;
using Mutation.Verifying.Domain;


namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>生成入力が完全一致したときの、前回実行からの結果の再構築</summary>
public static class SnapshotReconstruction
{
    /// <summary>保存が今回の入力と完全一致し、そのまま再利用できるか</summary>
    /// <param name="document">前回の保存</param>
    /// <param name="fingerprint">今回の生成入力の指紋</param>
    /// <param name="testAssemblyPath">今回のテスト assembly の絶対 path</param>
    /// <param name="mutatedAssemblyPath">前回の変異 assembly の置き場所</param>
    /// <returns>全段階を省けるなら真</returns>
    public static bool IsFullMatch(
        SnapshotDocument? document,
        string? fingerprint,
        string testAssemblyPath,
        string mutatedAssemblyPath
    ) =>
        document is { CompilationFingerprint: not null, MutatedAssemblyHash: not null, Tests: not null }
        && fingerprint is not null
        && document.CompilationFingerprint == fingerprint
        && document.TestAssemblyHash == SnapshotStore.HashOf(testAssemblyPath)
        && File.Exists(mutatedAssemblyPath)
        && document.MutatedAssemblyHash == SnapshotStore.HashOf(mutatedAssemblyPath);

    /// <summary>保存からの対象の確定結果の再構築</summary>
    /// <param name="document">完全一致した前回の保存</param>
    /// <param name="targetName">対象 project の名前</param>
    /// <returns>前回と同じ判定を持つ確定結果</returns>
    public static TargetResult Rebuild(SnapshotDocument document, string targetName)
    {
        var mutants = new List<Mutant>();
        var verdicts = new Dictionary<MutantId, MutantVerdict>();
        var nextId = 0;
        foreach (var (path, file) in document.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var saved in file.Mutants)
            {
                var id = new MutantId(nextId++);
                mutants.Add(
                    new Mutant(
                        id,
                        saved.Operator,
                        path,
                        new SourceSpan(saved.Line, saved.Column, saved.EndLine, saved.EndColumn),
                        saved.Original ?? "",
                        saved.Replacement,
                        saved.InStaticContext
                    )
                );
                verdicts[id] = VerdictNames.Restore(saved.Status, saved.KilledBy);
            }
        }

        var tests = (document.Tests ?? [])
            .Select((t, index) => new TestCaseInfo(new TestIndex(index), t.Name, t.Ms, t.Passed))
            .ToArray();
        return new TargetResult(targetName, mutants, verdicts, tests, TargetTimings.None, [], 0);
    }
}
