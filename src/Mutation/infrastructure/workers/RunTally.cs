using Mutation.Domain;
using System.Collections.Concurrent;

namespace Mutation.Infrastructure.Workers;

/// <summary>一回の実行呼び出しに閉じた判定の集計</summary>
/// <param name="total">実行する変異計画の件数</param>
/// <param name="progress">進行を伝える通知先</param>
/// <param name="settled">判定が確定するたびに呼ぶ通知先</param>
public sealed class RunTally(int total, Action<RunProgress> progress, Action<MutantId, MutantVerdict> settled)
{
    /// <summary>確定した判定の集まり</summary>
    private readonly ConcurrentDictionary<MutantId, MutantVerdict> verdicts = [];
    /// <summary>変異ごとの検出行列の行の集まり</summary>
    private readonly ConcurrentDictionary<MutantId, DetectionRow> detections = [];
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
    /// <param name="detection">検出行列を求めた実行での検出行列の行</param>
    public void Record(
        MutantPlan plan,
        MutantVerdict verdict,
        int executedTests,
        double elapsedMs,
        bool isolated,
        DetectionRow? detection
    )
    {
        verdicts[plan.MutantId] = verdict;
        if (detection is not null)
        {
            detections[plan.MutantId] = detection;
        }

        timings.Add(new MutantTiming(plan.MutantId, elapsedMs, executedTests, isolated));
        if (verdict is not MutantVerdict.Unjudged)
        {
            settled(plan.MutantId, verdict);
        }

        var done = Interlocked.Increment(ref completed);
        if (done % reportEvery == 0 || done == total)
        {
            progress(new RunProgress.VerdictProgress(done, total));
        }
    }

    /// <summary>判定を確定できなかった変異の記録。確定した件数にも、確定の通知にも数えない</summary>
    /// <param name="plan">判定を確定できなかった変異計画</param>
    /// <param name="cause">確定できなかった理由</param>
    public void Abandon(MutantPlan plan, UnjudgedCause cause) =>
        verdicts[plan.MutantId] = new MutantVerdict.Unjudged(cause);

    /// <summary>取りかからなかった計画の、実行に至らなかった未判定としての記録</summary>
    /// <param name="plans">取りかからなかった変異計画</param>
    public void AbandonAll(IEnumerable<MutantPlan> plans)
    {
        foreach (var plan in plans)
        {
            Abandon(plan, new UnjudgedCause.NotRun());
        }
    }

    /// <summary>worker を作り直した回数の加算</summary>
    public void CountRestart() => Interlocked.Increment(ref restarts);

    /// <summary>ここまでの記録の集計</summary>
    /// <returns>この呼び出し分の判定と記録</returns>
    public OrchestrationResult Result() => new(verdicts, timings.ToArray(), restarts, detections);
}
