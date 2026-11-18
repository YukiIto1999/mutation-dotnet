namespace Mutation.Protocol;

/// <summary>baseline で観測した一テストの結果</summary>
/// <param name="Id">テストの一意識別子</param>
/// <param name="Name">テストの表示名</param>
/// <param name="Ms">実行の所要時間</param>
/// <param name="Passed">成功したか。skip は偽</param>
/// <param name="Hits">実行中に観測した変異の連番</param>
/// <param name="ProbeCount">実行中の probe 呼び出し回数</param>
/// <param name="StaticHits">実行中に static 初期化の区間で観測した変異の連番</param>
public sealed record WorkerTestResult(
    string Id,
    string Name,
    double Ms,
    bool Passed,
    IReadOnlyList<int> Hits,
    long ProbeCount,
    IReadOnlyList<int>? StaticHits = null
);
