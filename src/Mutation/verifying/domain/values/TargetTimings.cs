

namespace Mutation.Verifying.Domain;

/// <summary>対象一件の段階別所要時間。対象間で共有する build を除く</summary>
/// <param name="MutateMs">構文解析と変異生成の所要時間</param>
/// <param name="CompileMs">schemata 込み再コンパイルの所要時間</param>
/// <param name="BaselineMs">変異なし実行と被覆収集の所要時間</param>
/// <param name="TestingMs">変異ごとのテスト実行の所要時間</param>
public sealed record TargetTimings(double MutateMs, double CompileMs, double BaselineMs, double TestingMs)
{
    /// <summary>全段階を省いたときの所要時間</summary>
    public static TargetTimings None { get; } = new(0, 0, 0, 0);
}
