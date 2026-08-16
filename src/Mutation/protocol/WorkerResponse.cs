using System.Text.Json.Serialization;

namespace Mutation.Protocol;

/// <summary>worker から CLI へ返す一行 JSON の応答</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Failed), "failed")]
[JsonDerivedType(typeof(Opened), "opened")]
[JsonDerivedType(typeof(Discovered), "discovered")]
[JsonDerivedType(typeof(Baselined), "baselined")]
[JsonDerivedType(typeof(RunCompleted), "run")]
[JsonDerivedType(typeof(Done), "done")]
public abstract record WorkerResponse
{
    /// <summary>列挙した派生以外の応答を塞ぐ基底の構築</summary>
    private WorkerResponse()
    {
    }

    /// <summary>指示を処理できなかった応答</summary>
    /// <param name="Error">失敗した理由</param>
    public sealed record Failed(string Error) : WorkerResponse;

    /// <summary>init に応えるホストの開設完了</summary>
    public sealed record Opened : WorkerResponse;

    /// <summary>discover に応える全テストの列挙</summary>
    /// <param name="TestIds">全テストの一意識別子の列</param>
    public sealed record Discovered(IReadOnlyList<string> TestIds) : WorkerResponse;

    /// <summary>baseline に応える被覆収集の結果</summary>
    /// <param name="Tests">観測した全テストの結果</param>
    /// <param name="Ambient">テスト境界の外に観測された変異の連番</param>
    public sealed record Baselined(IReadOnlyList<WorkerTestResult> Tests, IReadOnlyList<int> Ambient) : WorkerResponse;

    /// <summary>run に応える検査の結末</summary>
    /// <param name="Outcome">結末。killed / survived / hitlimit</param>
    /// <param name="KillerTest">最初に失敗したテストの表示名。なければ不在</param>
    /// <param name="ExecutedTests">実行を始めたテスト数。数えられないホストでは不在</param>
    /// <param name="Ms">実行の所要時間</param>
    public sealed record RunCompleted(string Outcome, string? KillerTest, int? ExecutedTests, double Ms) : WorkerResponse;

    /// <summary>exit に応える終了の受理</summary>
    public sealed record Done : WorkerResponse;
}
