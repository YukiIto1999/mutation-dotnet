using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Collections.Concurrent;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>一回の実行呼び出しに閉じた判定の集計</summary>
public sealed class RunTally(int total, Action<RunProgress> progress)
{
    /// <summary>確定した判定の集まり</summary>
    private readonly ConcurrentDictionary<MutantId, MutantVerdict> verdicts = [];
    /// <summary>変異ごとの実行の記録</summary>
    private readonly ConcurrentBag<MutantTiming> timings = [];
    /// <summary>進行を通知する間隔</summary>
    private readonly int reportEvery = Math.Max(1, total / 20);
    /// <summary>確定した件数</summary>
    private int completed;
    /// <summary>worker を作り直した回数</summary>
    private int restarts;

    /// <summary>一変異の判定と実行の記録</summary>
    /// <param name="plan">実行した変異計画</param>
    /// <param name="verdict">確定した結果</param>
    /// <param name="executedTests">実行を始めたテスト数</param>
    /// <param name="elapsedMs">実行の所要時間</param>
    /// <param name="isolated">隔離 process での実行だったか</param>
    public void Record(MutantPlan plan, MutantVerdict verdict, int executedTests, double elapsedMs, bool isolated)
    {
        verdicts[plan.MutantId] = verdict;
        timings.Add(new MutantTiming(plan.MutantId, elapsedMs, executedTests, isolated));
        var done = Interlocked.Increment(ref completed);
        if (done % reportEvery == 0 || done == total)
        {
            progress(new RunProgress.VerdictProgress(done, total));
        }
    }

    /// <summary>worker を作り直した回数の加算</summary>
    public void CountRestart() => Interlocked.Increment(ref restarts);

    /// <summary>ここまでの記録の集計</summary>
    /// <returns>この呼び出し分の判定と記録</returns>
    public OrchestrationResult Result() => new(verdicts, timings.ToArray(), restarts);
}
