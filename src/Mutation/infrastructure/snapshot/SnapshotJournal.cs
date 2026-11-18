using Mutation.Domain;
using Mutation.Application;
using System.Text;
using System.Text.Json;

namespace Mutation.Infrastructure.Snapshot;

/// <summary>判定の確定ごとに一行ずつ追記する、保存に畳む前の記録</summary>
/// <remarks>
/// 一行目は走行の前に分かっている判定を持つ保存の全体、二行目からは確定した判定の一件ずつである。
/// 一行を書くごとに flush するので、process が殺されても、書き終えた行は残る
/// </remarks>
internal sealed class SnapshotJournal : IVerdictJournal
{
    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>追記の記録の path</summary>
    private readonly string journalPath;

    /// <summary>最終の保存の path</summary>
    private readonly string snapshotPath;

    /// <summary>変異の連番から、保存の中での file と順番への対応</summary>
    private readonly Dictionary<MutantId, (string File, int Index)> places;

    /// <summary>追記の書き込み口</summary>
    private readonly StreamWriter writer;

    /// <summary>複数の lane からの追記を一行ずつにする排他</summary>
    private readonly object gate = new();

    /// <summary>実行の前の判定を一行目に書いた記録の開始</summary>
    /// <param name="checkpoint">実行の前に分かっている判定を持つ、保存する内容</param>
    public SnapshotJournal(SnapshotSaveRequest checkpoint)
    {
        snapshotPath = checkpoint.SnapshotPath;
        journalPath = SnapshotStore.JournalPathOf(snapshotPath);
        places = Places(checkpoint.Mutants);
        var begin = new JournalBegin(Describe(checkpoint));
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath) ?? ".");
        var pending = journalPath + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(begin, Serializer) + "\n");
        File.Move(pending, journalPath, overwrite: true);
        writer = new StreamWriter(
            new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(false)
        ) { AutoFlush = true, NewLine = "\n" };
    }

    /// <inheritdoc />
    public void Append(MutantId id, MutantVerdict verdict)
    {
        if (!places.TryGetValue(id, out var place))
        {
            return;
        }

        var entry = new JournalEntry(
            place.File,
            place.Index,
            VerdictNames.For(verdict),
            verdict is MutantVerdict.Killed killed ? killed.KillerTest : null
        );
        lock (gate)
        {
            writer.WriteLine(JsonSerializer.Serialize(entry, Serializer));
        }
    }

    /// <inheritdoc />
    public void Commit(SnapshotSaveRequest final)
    {
        Dispose();
        SnapshotStore.Write(Describe(final), snapshotPath);
        File.Delete(journalPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            writer.Dispose();
        }
    }

    /// <summary>保存する内容の保存形への写像</summary>
    private static SnapshotDocument Describe(SnapshotSaveRequest request) =>
        SnapshotStore.Describe(
            request.Mutants,
            request.Verdicts,
            request.Baseline.Tests,
            request.Baseline.Coverage,
            new SnapshotContext(
                request.TestAssemblyPath,
                request.Identity,
                request.MutatedAssemblyPath,
                request.CompileErrorIds,
                request.Baseline.Roster
            )
        );

    /// <summary>保存形が file ごとに連番順で並べる変異の、file と順番の控え</summary>
    private static Dictionary<MutantId, (string File, int Index)> Places(IReadOnlyList<Mutant> mutants) =>
        mutants
            .GroupBy(m => m.FilePath, StringComparer.Ordinal)
            .Where(group => File.Exists(group.Key))
            .SelectMany(group => group.OrderBy(m => m.Id.Value).Select((m, index) => (m.Id, (group.Key, index))))
            .ToDictionary(pair => pair.Id, pair => pair.Item2);
}

/// <summary>追記の記録の一行目。走行の前に分かっている判定を持つ保存の全体</summary>
/// <param name="Begin">保存の全体</param>
internal sealed record JournalBegin(SnapshotDocument Begin);

/// <summary>追記の記録の二行目からの、確定した判定の一件</summary>
/// <param name="File">変異を置いたファイルの絶対 path</param>
/// <param name="Index">保存の中でのファイル内の順番</param>
/// <param name="Status">判定の状態名</param>
/// <param name="KilledBy">検出したテストの表示名。検出以外は不在</param>
internal sealed record JournalEntry(string File, int Index, string Status, string? KilledBy);
