using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>実行時間の期限を超えた変異の、検出か競合による遅れかの確かめ</summary>
/// <remarks>
/// 期限は基準実行の所要時間から決めた固定の値なので、機が基準より遅ければ、無限走行でない変異も超える
/// 超えた変異ごとに、変異のない対照の実行が予算に収まるかを測り、収まらなければ未判定にする
/// 収まれば新規 process でもう一度実行し、また超えたときだけ timeout の検出とする
/// 対照を先に走らせるのは、競合が続く間は再実行も同じく超えるので、再実行に期限を二度使わないため
/// </remarks>
/// <param name="isolationRuns">隔離 process での実行係</param>
/// <param name="roster">連番順のテストの名簿</param>
/// <param name="detectionMatrix">変異ごとに全テストを実行し検出行列の行を残すか</param>
internal sealed class TimeoutAdjudication(IsolationRuns isolationRuns, TestRoster roster, bool detectionMatrix)
{
    /// <summary>期限を超えた変異の確かめ</summary>
    /// <param name="plan">期限を超えた変異計画。その隔離側のテスト列と予算で確かめる</param>
    /// <param name="cancellation">取り消しの要求。受けたら結果は採られない</param>
    /// <returns>競合のせいなら未判定、再実行が終われば再実行の判定、また超えたなら検出に数える timeout</returns>
    public async Task<IsolationOutcome> AdjudicateAsync(MutantPlan plan, CancellationToken cancellation)
    {
        var (order, budgetMs) = plan.Execution.IsolatedSlice;
        var control = await isolationRuns.ControlAsync(plan, cancellation).ConfigureAwait(false);
        if (TimeoutJudgement.Contention(control.CompletedMs, budgetMs) is { } contention)
        {
            return new IsolationOutcome(
                new MutantVerdict.Unjudged(contention),
                0,
                control.ElapsedMs,
                detectionMatrix ? DetectionRows.From(order, roster, null) : null
            );
        }

        return await isolationRuns.RunAsync(plan, cancellation).ConfigureAwait(false);
    }
}
