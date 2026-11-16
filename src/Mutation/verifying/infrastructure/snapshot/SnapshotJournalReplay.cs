using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>止まった走行が残した追記の記録からの、保存の復元</summary>
internal static class SnapshotJournalReplay
{
    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>一行目の保存の全体に、続く行の判定を重ねた保存の復元</summary>
    /// <remarks>書き終える前に止まった最後の行は読めないので捨てる</remarks>
    /// <param name="journalPath">追記の記録の path</param>
    /// <returns>確定済みの判定を持つ保存。記録が無いか一行目を読めなければ不在</returns>
    public static SnapshotDocument? Recover(string journalPath)
    {
        string[] lines;
        try
        {
            lines = File.Exists(journalPath) ? File.ReadAllLines(journalPath) : [];
        }
        catch (IOException)
        {
            return null;
        }

        if (lines.Length == 0 || Parse<JournalBegin>(lines[0])?.Begin is not { } document)
        {
            return null;
        }

        var mutants = document.Files.ToDictionary(pair => pair.Key, pair => pair.Value.Mutants.ToArray(), StringComparer.Ordinal);
        foreach (var entry in lines.Skip(1).Select(Parse<JournalEntry>).OfType<JournalEntry>())
        {
            if (mutants.TryGetValue(entry.File, out var file) && entry.Index >= 0 && entry.Index < file.Length)
            {
                file[entry.Index] = file[entry.Index] with { Status = entry.Status, KilledBy = entry.KilledBy };
            }
        }

        return document with
        {
            Files = document.Files.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { Mutants = mutants[pair.Key] },
                StringComparer.Ordinal
            ),
        };
    }

    /// <summary>一行の読み取り。読めなければ不在</summary>
    private static T? Parse<T>(string line)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, Serializer);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
