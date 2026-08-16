using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変異ごとのテスト実行全体の集計</summary>
/// <param name="Verdicts">変異の連番から確定結果への対応</param>
/// <param name="Timings">変異ごとの所要時間の記録</param>
/// <param name="WorkerRestarts">時間切れなどで worker を作り直した回数</param>
public sealed record OrchestrationResult(
    IReadOnlyDictionary<MutantId, MutantVerdict> Verdicts,
    IReadOnlyList<MutantTiming> Timings,
    int WorkerRestarts
)
{
    /// <summary>後続実行の結果を重ねた集計。判定は後続が勝ち、記録と回数は合算の形</summary>
    /// <param name="later">同じ変異の一部を実行し直した後続の集計</param>
    /// <returns>両方の実行を反映した集計</returns>
    public OrchestrationResult Overlaid(OrchestrationResult later)
    {
        var merged = new Dictionary<MutantId, MutantVerdict>(Verdicts);
        foreach (var (id, verdict) in later.Verdicts)
        {
            merged[id] = verdict;
        }

        return new OrchestrationResult(
            merged,
            [.. Timings, .. later.Timings],
            WorkerRestarts + later.WorkerRestarts
        );
    }
}
