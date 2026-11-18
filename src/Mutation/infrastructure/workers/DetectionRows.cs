using Mutation.Protocol;
using Mutation.Domain;

namespace Mutation.Infrastructure.Workers;

/// <summary>run の応答から検出行列の一行への変換</summary>
public static class DetectionRows
{
    /// <summary>実行を求めたテスト列と run の応答からの検出行列の一行の導出</summary>
    /// <param name="order">run に実行を求めたテスト連番の列</param>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="response">時間切れや process 終了では不在の run の応答</param>
    /// <returns>全件の記録がない応答では全テストを未実行とする失敗と未実行のテストの記録</returns>
    public static DetectionRow From(IReadOnlyList<TestIndex> order, TestRoster roster, WorkerResponse? response)
    {
        if (response is not WorkerResponse.RunCompleted { FailedTestIds: { } failedIds, CompletedTestIds: { } completedIds })
        {
            return new DetectionRow(order, [], order);
        }

        var byId = new Dictionary<string, TestIndex>(order.Count, StringComparer.Ordinal);
        foreach (var index in order)
        {
            byId.TryAdd(roster.Id(index), index);
        }

        var completed = completedIds.ToHashSet(StringComparer.Ordinal);
        var failed = failedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
        var notRun = order.Where(index => !completed.Contains(roster.Id(index))).ToArray();
        return new DetectionRow(order, failed, notRun);
    }

    /// <summary>同じ変異を別の process で実行した二つの行の合成</summary>
    /// <param name="first">先に実行した行</param>
    /// <param name="second">後に実行した行</param>
    /// <returns>両方の計画の和、失敗の和、どちらでも結果を観測しなかったテストを未実行とする行</returns>
    public static DetectionRow Merge(DetectionRow first, DetectionRow second)
    {
        var planned = first.Planned.Concat(second.Planned).Distinct().ToArray();
        var failed = first.Failed.Concat(second.Failed).Distinct().ToArray();
        var notRun = planned.Where(index => Unobserved(first, index) && Unobserved(second, index)).ToArray();
        return new DetectionRow(planned, failed, notRun);
    }

    /// <summary>行がそのテストの結果を観測していないか</summary>
    private static bool Unobserved(DetectionRow row, TestIndex index) =>
        !row.Planned.Contains(index) || row.NotRun.Contains(index);
}
