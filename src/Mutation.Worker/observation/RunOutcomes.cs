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

    /// <summary>観測の記録からの run の応答の組み立て</summary>
    /// <remarks>失敗と完了の一覧を全失敗の記録を求めた run の応答だけに持たせる形</remarks>
    /// <param name="observation">run で観測した失敗と完了の記録</param>
    /// <param name="allFailures">全失敗の記録を求めた run か</param>
    /// <param name="executedTests">実行を始めたテスト数</param>
    /// <param name="ms">実行の所要時間</param>
    /// <param name="reached">活性の変異の枝へ実行が一度でも入ったか</param>
    /// <returns>最初の失敗で結末を決めた run の応答</returns>
    public static WorkerResponse.RunCompleted Completed(
        RunObservation observation,
        bool allFailures,
        int executedTests,
        double ms,
        bool reached
    ) =>
        new(
            From(observation.KillerTest, observation.HitLimitExceeded),
            observation.KillerTest,
            executedTests,
            ms,
            reached,
            allFailures ? [.. observation.FailedTestIds] : null,
            allFailures ? [.. observation.CompletedTestIds] : null
        );
}
