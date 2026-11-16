using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>実行全体の確定結果。対象一件ごとの帰結の束</summary>
/// <param name="Outcomes">対象ごとの帰結。入力に並べた順</param>
/// <param name="Timings">実行全体の段階別所要時間</param>
public sealed record MutationRunResult(IReadOnlyList<TargetOutcome> Outcomes, PhaseTimings Timings)
{
    /// <summary>判定まで到達した対象の結果</summary>
    public IEnumerable<TargetResult> Completed =>
        Outcomes.OfType<TargetOutcome.Completed>().Select(outcome => outcome.Result);

    /// <summary>中断した対象の帰結</summary>
    public IEnumerable<TargetOutcome.Failed> Failures => Outcomes.OfType<TargetOutcome.Failed>();

    /// <summary>検出された変異の数。テスト失敗と時間超過の合計</summary>
    public int DetectedCount => Completed.Sum(t => t.DetectedCount);

    /// <summary>検出されなかった変異の数。生存と被覆なしの合計</summary>
    public int UndetectedCount => Completed.Sum(t => t.UndetectedCount);

    /// <summary>変異検出率。判定まで到達した対象だけを母数にする。検出も非検出もなければ不在</summary>
    public double? Score =>
        DetectedCount + UndetectedCount == 0
            ? null
            : (double)DetectedCount / (DetectedCount + UndetectedCount);

    /// <summary>判定を確定できなかった変異の数。検出にも未検出にも数えない</summary>
    public int UnjudgedCount => Completed.Sum(t => t.UnjudgedCount);

    /// <summary>取り消しを受けて止まった走行か。判定に至らなかった対象か、実行に至らなかった変異と実行の途中だった変異がある</summary>
    public bool IsInterrupted =>
        Failures.Any(failure => failure.Failure is PipelineFailure.Interrupted) || Completed.Any(t => t.IsInterrupted);

    /// <summary>全件の判定が確定した走行か</summary>
    public bool IsComplete => !IsInterrupted && UnjudgedCount == 0;

    /// <summary>報告に残す走行の状態。取り消しで止まった interrupted、競合で判定できない変異が残った unjudged、全件が確定した completed</summary>
    public string State =>
        (IsInterrupted, UnjudgedCount) switch
        {
            (true, _) => "interrupted",
            (_, > 0) => "unjudged",
            _ => "completed",
        };

    /// <summary>対象ごとの所要時間と共有 build からの、実行全体の段階別所要時間の算出</summary>
    /// <param name="outcomes">対象ごとの帰結</param>
    /// <param name="buildMs">対象間で共有する build の所要時間</param>
    /// <param name="totalMs">全体の所要時間</param>
    /// <returns>段階ごとに合算した所要時間</returns>
    public static PhaseTimings Aggregate(IReadOnlyList<TargetOutcome> outcomes, double buildMs, double totalMs)
    {
        var completed = outcomes.OfType<TargetOutcome.Completed>().Select(outcome => outcome.Result).ToArray();
        return new PhaseTimings(
            buildMs,
            completed.Sum(t => t.Timings.MutateMs),
            completed.Sum(t => t.Timings.CompileMs),
            completed.Sum(t => t.Timings.BaselineMs),
            completed.Sum(t => t.Timings.TestingMs),
            totalMs
        );
    }
}
