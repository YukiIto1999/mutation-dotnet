using System.Collections.Concurrent;
using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker の実 stdout の回収と、指示完了の目印の待ち合わせ</summary>
public sealed class WorkerOutputBuffer
{
    /// <summary>保持する行数の上限</summary>
    private const int RetainedLines = 400;

    /// <summary>観測した出力行の控え</summary>
    private readonly ConcurrentQueue<string> lines = new();
    /// <summary>指示の連番から完了待ちへの対応</summary>
    private readonly ConcurrentDictionary<int, TaskCompletionSource> sentinels = new();

    /// <summary>stdout の一行の取り込み。目印の行は待ち合わせの解決に使う形</summary>
    /// <param name="line">観測した行。stream の終端なら不在</param>
    public void OnLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        if (line.StartsWith(WorkerContract.OutputSentinel, StringComparison.Ordinal))
        {
            var raw = line[(WorkerContract.OutputSentinel.Length + 1)..].Trim();
            if (int.TryParse(raw, out var completed))
            {
                Sentinel(completed).TrySetResult();
            }

            return;
        }

        lines.Enqueue(line);
        while (lines.Count > RetainedLines)
        {
            lines.TryDequeue(out _);
        }
    }

    /// <summary>指定の指示の処理完了を待つ task</summary>
    /// <param name="sequence">指示の連番</param>
    /// <returns>worker が目印を書いたら完了する task</returns>
    public Task Completion(int sequence) => Sentinel(sequence).Task;

    /// <summary>溜まった出力行の取り出し。取り出した行は消える形</summary>
    /// <returns>前回の取り出し以降に観測した行</returns>
    public List<string> Take()
    {
        var taken = new List<string>();
        while (lines.TryDequeue(out var line))
        {
            taken.Add(line);
        }

        return taken;
    }

    /// <summary>連番に対応する完了待ちの取得。なければ作る形</summary>
    private TaskCompletionSource Sentinel(int sequence) =>
        sentinels.GetOrAdd(sequence, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
