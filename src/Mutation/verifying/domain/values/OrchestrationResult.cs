using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変異ごとのテスト実行全体の集計</summary>
/// <param name="Verdicts">変異の連番から確定結果への対応</param>
/// <param name="Timings">変異ごとの所要時間の記録</param>
/// <param name="WorkerRestarts">時間切れなどで worker を作り直した回数</param>
/// <param name="Detections">検出行列を求めた実行での変異の連番から検出行列の行への対応</param>
public sealed record OrchestrationResult(
    IReadOnlyDictionary<MutantId, MutantVerdict> Verdicts,
    IReadOnlyList<MutantTiming> Timings,
    int WorkerRestarts,
    IReadOnlyDictionary<MutantId, DetectionRow> Detections
)
{
    /// <summary>判定と検出行列の行を後続で上書きし記録と回数を合算する後続実行の重ね合わせ。後続で判定を確定できなかった変異は、前の判定と行を残す</summary>
    /// <param name="later">同じ変異の一部を実行し直した後続の集計</param>
    /// <returns>両方の実行を反映した集計</returns>
    public OrchestrationResult Overlaid(OrchestrationResult later)
    {
        var settled = later.Verdicts.Where(pair => pair.Value is not MutantVerdict.Unjudged).ToArray();
        var merged = new Dictionary<MutantId, MutantVerdict>(Verdicts);
        foreach (var (id, verdict) in settled)
        {
            merged[id] = verdict;
        }

        var detections = new Dictionary<MutantId, DetectionRow>(Detections);
        foreach (var (id, row) in later.Detections.Where(pair => settled.Any(s => s.Key == pair.Key)))
        {
            detections[id] = row;
        }

        return new OrchestrationResult(
            merged,
            [.. Timings, .. later.Timings],
            WorkerRestarts + later.WorkerRestarts,
            detections
        );
    }
}
