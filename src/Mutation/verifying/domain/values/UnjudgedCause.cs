using TypeModeling.Domain;

namespace Mutation.Verifying.Domain;

/// <summary>判定を確定できなかった理由</summary>
[Closed]
public abstract record UnjudgedCause
{
    /// <summary>列挙した派生以外の理由を塞ぐ基底の構築</summary>
    private UnjudgedCause()
    {
    }

    /// <summary>走行が止まるまで、この変異の実行に取りかからなかった</summary>
    public sealed record NotRun : UnjudgedCause;

    /// <summary>この変異を実行している途中で走行が止まり、その結果を採らなかった</summary>
    public sealed record Interrupted : UnjudgedCause;

    /// <summary>期限を超えたが、変異のない対照の実行も予算に収まらず、競合による遅れと区別できなかった</summary>
    /// <param name="ControlMs">対照の実行の所要時間。応答がなければ不在</param>
    /// <param name="BudgetMs">変異一件の実行時間予算</param>
    public sealed record ContendedTimeout(double? ControlMs, double BudgetMs) : UnjudgedCause;

    /// <summary>テストを走らせる worker が失敗の応答を返したか、開けなかったか、応えなかったために、変異の判定を確かめられなかった。変異がテストに検出された事実はない</summary>
    /// <param name="Detail">失敗の説明。worker が返した理由か、応答がなかった状況の記述</param>
    public sealed record RunnerFailure(string Detail) : UnjudgedCause;
}
