namespace Mutation.Protocol;

/// <summary>注入された切替クラスと、それを載せる process の間の取り決め</summary>
public static class InjectionContract
{
    /// <summary>活性化する変異の連番を渡す環境変数の名前</summary>
    public const string ActiveEnvironmentVariable = "MUTATION_ACTIVE";

    /// <summary>probe 呼び出し回数の上限を渡す環境変数の名前</summary>
    public const string HitLimitEnvironmentVariable = "MUTATION_HIT_LIMIT";

    /// <summary>被覆収集で起動することを伝える環境変数の名前</summary>
    public const string CoverageEnvironmentVariable = "MUTATION_COVERAGE";

    /// <summary>probe 上限超過を伝える例外メッセージの目印</summary>
    public const string HitLimitMarker = "MUTATION_HIT_LIMIT_EXCEEDED";
}
