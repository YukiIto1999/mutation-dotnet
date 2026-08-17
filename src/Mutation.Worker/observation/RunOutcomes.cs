using Mutation.Protocol;

namespace Mutation.Worker;

/// <summary>失敗の観測から run の結末の値への変換</summary>
public static class RunOutcomes
{
    /// <summary>最初の失敗の有無と probe 上限超過からの結末の決定</summary>
    /// <param name="killerTest">最初に失敗したテストの表示名。失敗がなければ不在</param>
    /// <param name="hitLimitExceeded">最初の失敗が probe 上限超過によるものか</param>
    /// <returns>killed / survived / hitlimit のいずれか</returns>
    public static string From(string? killerTest, bool hitLimitExceeded) =>
        (killerTest, hitLimitExceeded) switch
        {
            (null, _) => WorkerContract.OutcomeSurvived,
            (_, true) => WorkerContract.OutcomeHitLimit,
            _ => WorkerContract.OutcomeKilled,
        };
}
