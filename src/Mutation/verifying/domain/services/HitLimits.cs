

namespace Mutation.Verifying.Domain;

/// <summary>無限走行を即断するための probe 呼び出し上限の算出</summary>
public static class HitLimits
{
    /// <summary>baseline の probe 回数に掛ける係数</summary>
    private const double Factor = 200.0;

    /// <summary>短いテストでも保証する上限の下駄</summary>
    private const long Floor = 100_000;

    /// <summary>実行するテスト列に応じた probe 呼び出し上限</summary>
    /// <param name="roster">テストの名簿</param>
    /// <param name="order">実行するテスト連番の列</param>
    /// <returns>この回数を超えたら無限走行と判定する上限</returns>
    public static long For(TestRoster roster, IReadOnlyList<TestIndex> order)
    {
        var maxProbes = order.Count == 0 ? 0 : order.Max(roster.ProbeCount);
        return (long)(maxProbes * Factor) + Floor;
    }
}
