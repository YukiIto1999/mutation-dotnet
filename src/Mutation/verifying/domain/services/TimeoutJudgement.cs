namespace Mutation.Verifying.Domain;

/// <summary>実行時間の期限を超えた変異を、検出に数えてよいかの判定</summary>
/// <remarks>
/// Stryker.NET(timeout-ratio と additional-timeout)、StrykerJS(timeoutFactor と timeoutMS)、PIT(timeoutFactor と timeoutConstant)は
/// 倍率と定数で固定の期限を作り、超過をそのまま timeout とする
/// 忙しい機への手当ては人が定数を大きくすることだけで、超過が無限走行か競合による遅れかは分けない
/// ここでは同じ形の期限を最初の関門にし、超えた変異だけ、変異のない同じテスト列を新規 process で走らせ直して予算に収まるかを測る
/// 収まらなければ unmutated のテストさえ予算の外で、超過を検出と言える根拠がないので未判定にする
/// 収まれば変異の再実行で一時の遅れかを確かめる。再実行の結果は呼び出し側が持つ
/// </remarks>
public static class TimeoutJudgement
{
    /// <summary>期限を超えた後に測った、変異のない対照の実行の結果からの、競合による遅れの判定</summary>
    /// <param name="controlMs">対照の実行の所要時間。応答がなければ不在</param>
    /// <param name="budgetMs">変異一件の実行時間予算</param>
    /// <returns>対照の実行も予算を超えたなら競合による遅れの理由。予算に収まれば不在</returns>
    public static UnjudgedCause.ContendedTimeout? Contention(double? controlMs, double budgetMs) =>
        controlMs is { } elapsed && elapsed <= budgetMs ? null : new UnjudgedCause.ContendedTimeout(controlMs, budgetMs);
}
