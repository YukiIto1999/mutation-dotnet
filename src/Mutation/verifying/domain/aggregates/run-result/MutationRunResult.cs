using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>実行全体の確定結果</summary>
/// <param name="Mutants">検査対象となった全変異</param>
/// <param name="Verdicts">変異の連番から確定結果への対応</param>
/// <param name="Tests">初回実行で観測したテストの素性</param>
/// <param name="Timings">段階別の所要時間</param>
/// <param name="MutantTimings">変異ごとの所要時間の記録</param>
/// <param name="WorkerRestarts">時間切れなどで worker を作り直した回数</param>
public sealed record MutationRunResult(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlyDictionary<MutantId, MutantVerdict> Verdicts,
    IReadOnlyList<TestCaseInfo> Tests,
    PhaseTimings Timings,
    IReadOnlyList<MutantTiming> MutantTimings,
    int WorkerRestarts
)
{
    /// <summary>検出された変異の数。テスト失敗と時間超過の合計</summary>
    public int DetectedCount =>
        Verdicts.Values.Count(v => v is MutantVerdict.Killed or MutantVerdict.TimedOut);

    /// <summary>検出されなかった変異の数。生存と被覆なしの合計</summary>
    public int UndetectedCount =>
        Verdicts.Values.Count(v => v is MutantVerdict.Survived or MutantVerdict.NoCoverage);

    /// <summary>変異検出率。検出も非検出もなければ不在</summary>
    public double? Score =>
        DetectedCount + UndetectedCount == 0
            ? null
            : (double)DetectedCount / (DetectedCount + UndetectedCount);
}
