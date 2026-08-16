using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>前回実行の判定を継承するための保存と読出</summary>
public static class SnapshotStore
{
    /// <summary>camelCase の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>出力 directory 内での保存先</summary>
    public const string FileName = "snapshot.json";

    /// <summary>今回の実行全体の、次回の継承元としての書き出し</summary>
    /// <param name="mutants">全変異</param>
    /// <param name="verdicts">変異の連番から確定結果への対応</param>
    /// <param name="tests">連番順のテストの素性</param>
    /// <param name="coverage">今回の被覆表</param>
    /// <param name="context">指紋と成果物の素性と保存先</param>
    public static void Save(
        IReadOnlyList<Mutant> mutants,
        IReadOnlyDictionary<MutantId, MutantVerdict> verdicts,
        IReadOnlyList<TestCaseInfo> tests,
        CoverageMap coverage,
        SnapshotContext context
    )
    {
        var files = mutants
            .GroupBy(m => m.FilePath, StringComparer.Ordinal)
            .Where(group => File.Exists(group.Key))
            .ToDictionary(
                group => group.Key,
                group => new SnapshotFile(
                    HashOf(group.Key),
                    group.OrderBy(m => m.Id.Value).Select(m => ToSaved(m, verdicts, coverage)).ToArray()
                ),
                StringComparer.Ordinal
            );
        var document = new SnapshotDocument(
            HashOf(context.TestAssemblyPath),
            context.CompilationFingerprint,
            File.Exists(context.MutatedAssemblyPath) ? HashOf(context.MutatedAssemblyPath) : null,
            context.CompileErrorIds.Select(id => id.Value).Order().ToArray(),
            tests
                .Select(t => new SnapshotTest(
                    context.Roster.Id(t.Index),
                    t.DisplayName,
                    t.BaselineMs,
                    t.BaselinePassed,
                    context.Roster.ProbeCount(t.Index)
                ))
                .ToArray(),
            coverage.AmbientMutants.Select(id => id.Value).Order().ToArray(),
            files
        );
        Directory.CreateDirectory(Path.GetDirectoryName(context.SavePath) ?? ".");
        File.WriteAllText(context.SavePath, JsonSerializer.Serialize(document, Serializer));
    }

    /// <summary>前回の保存を読み出す。なければ不在</summary>
    /// <param name="path">保存 file の path</param>
    /// <returns>前回の判定一式。読めなければ不在</returns>
    public static SnapshotDocument? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path), Serializer);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>file 内容の SHA-256</summary>
    /// <param name="path">対象 file の path</param>
    /// <returns>16 進の hash 文字列</returns>
    public static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>一変異の保存形への写像</summary>
    private static SnapshotMutant ToSaved(
        Mutant mutant,
        IReadOnlyDictionary<MutantId, MutantVerdict> verdicts,
        CoverageMap coverage
    ) =>
        new(
            mutant.OperatorName,
            mutant.Span.Line,
            mutant.Span.Column,
            mutant.Span.EndLine,
            mutant.Span.EndColumn,
            mutant.Replacement,
            VerdictNames.For(verdicts[mutant.Id]),
            verdicts[mutant.Id] is MutantVerdict.Killed killed ? killed.KillerTest : null,
            mutant.Original,
            mutant.InStaticContext,
            coverage.CoveringTests(mutant.Id).Select(t => t.Value).Order().ToArray(),
            coverage.TriggeringTests(mutant.Id).Select(t => t.Value).Order().ToArray()
        );
}

/// <summary>保存に添える指紋と成果物の素性、および保存先</summary>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="CompilationFingerprint">変異の生成入力の指紋</param>
/// <param name="MutatedAssemblyPath">変異 assembly の絶対 path</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="Roster">連番順のテストの名簿</param>
/// <param name="SavePath">書き出す file の絶対 path</param>
public sealed record SnapshotContext(
    string TestAssemblyPath,
    string? CompilationFingerprint,
    string MutatedAssemblyPath,
    IReadOnlySet<MutantId> CompileErrorIds,
    TestRoster Roster,
    string SavePath
);
