

namespace Mutation.Verifying.Domain;

/// <summary>実行全体の段階別所要時間</summary>
/// <param name="BuildMs">対象の初回 build と成果物解析の所要時間</param>
/// <param name="MutateMs">構文解析と変異生成の所要時間</param>
/// <param name="CompileMs">schemata 込み再コンパイルの所要時間</param>
/// <param name="BaselineMs">変異なし実行と被覆収集の所要時間</param>
/// <param name="TestingMs">変異ごとのテスト実行の所要時間</param>
/// <param name="TotalMs">全体の所要時間</param>
public sealed record PhaseTimings(
    double BuildMs,
    double MutateMs,
    double CompileMs,
    double BaselineMs,
    double TestingMs,
    double TotalMs
);
