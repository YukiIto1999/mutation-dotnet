namespace Mutation.Protocol;

/// <summary>CLI と worker の間の一行 JSON の取り決め</summary>
public static class WorkerContract
{
    /// <summary>run の結末で検出を表す値</summary>
    public const string OutcomeKilled = "killed";

    /// <summary>run の結末で生存を表す値</summary>
    public const string OutcomeSurvived = "survived";

    /// <summary>run の結末で probe 上限超過を表す値</summary>
    public const string OutcomeHitLimit = "hitlimit";

    /// <summary>worker が一指示の処理を終えたことを実 stdout に刻む目印</summary>
    public const string OutputSentinel = "@@mutation-request-done";
}
