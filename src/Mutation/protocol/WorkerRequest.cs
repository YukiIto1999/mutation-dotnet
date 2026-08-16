using System.Text.Json.Serialization;

namespace Mutation.Protocol;

/// <summary>CLI から worker へ送る一行 JSON の指示</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "cmd")]
[JsonDerivedType(typeof(Init), "init")]
[JsonDerivedType(typeof(Discover), "discover")]
[JsonDerivedType(typeof(Baseline), "baseline")]
[JsonDerivedType(typeof(Run), "run")]
[JsonDerivedType(typeof(Shutdown), "exit")]
public abstract record WorkerRequest
{
    /// <summary>列挙した派生以外の指示を塞ぐ基底の構築</summary>
    private WorkerRequest()
    {
    }

    /// <summary>テストホストを開く指示</summary>
    /// <param name="TestAssembly">テスト assembly の絶対 path</param>
    /// <param name="MutatedDirectory">変異 assembly の置き場</param>
    /// <param name="TargetAssemblyName">差し替え対象 assembly の単純名</param>
    public sealed record Init(string TestAssembly, string MutatedDirectory, string TargetAssemblyName) : WorkerRequest;

    /// <summary>全テストの一意識別子を列挙する指示</summary>
    public sealed record Discover : WorkerRequest;

    /// <summary>被覆収集モードでテストを実行する指示</summary>
    /// <param name="TestIds">対象にするテストの一意識別子の列</param>
    public sealed record Baseline(IReadOnlyList<string> TestIds) : WorkerRequest;

    /// <summary>一変異を活性化して選択テストを実行する指示</summary>
    /// <param name="MutantId">活性化する変異の連番</param>
    /// <param name="TestIds">実行するテストの一意識別子の列</param>
    /// <param name="HitLimit">probe 呼び出し上限。環境変数で活性化済みの worker では不在</param>
    public sealed record Run(int MutantId, IReadOnlyList<string> TestIds, long? HitLimit = null) : WorkerRequest;

    /// <summary>worker を終了する指示</summary>
    public sealed record Shutdown : WorkerRequest;
}
