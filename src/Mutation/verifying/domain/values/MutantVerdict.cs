using TypeModeling.Domain;

namespace Mutation.Verifying.Domain;

/// <summary>変異一件に対する検査の確定結果</summary>
[ClosedUnion]
public abstract record MutantVerdict
{
    /// <summary>列挙した派生以外の結果を塞ぐ基底の構築</summary>
    private MutantVerdict()
    {
    }

    /// <summary>テストの失敗により検出された結果</summary>
    /// <param name="KillerTest">最初に失敗したテストの表示名</param>
    public sealed record Killed(string KillerTest) : MutantVerdict;

    /// <summary>被覆する全テストが成功し検出されなかった結果</summary>
    public sealed record Survived : MutantVerdict;

    /// <summary>実行時間予算の超過により検出扱いとなった結果</summary>
    public sealed record TimedOut : MutantVerdict;

    /// <summary>どのテストにも被覆されず実行されなかった結果</summary>
    public sealed record NoCoverage : MutantVerdict;

    /// <summary>schemata を含む再コンパイルが失敗し無効化された結果</summary>
    public sealed record CompileError : MutantVerdict;

    /// <summary>指定により検査対象から外された結果。score の母数の対象外</summary>
    public sealed record Ignored : MutantVerdict;
}
