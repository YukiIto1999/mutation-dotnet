using Mutation.Mutating.Domain;
using Mutation.Shared;

namespace Mutation.Mutating.Application;

/// <summary>生成入力の指紋計算を担う port</summary>
public interface IFingerprints
{
    /// <summary>判定を決める入力の閉包の同一性と、変わった行を加えた全体の指紋の計算</summary>
    /// <remarks>
    /// 閉包は、csc 引数、ソース、参照する assembly、analyzer と generator、additional file、analyzer config、resource の内容、
    /// test assembly の directory の内容、engine と compiler と runtime の版、設定の要約から求める
    /// 読めない入力が一つでもあれば、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="testAssemblyPath">テスト assembly の絶対 path。その directory の内容を閉包に含める</param>
    /// <param name="settings">変異の集合と判定に影響する設定の要約</param>
    /// <param name="changes">差分運用の基点から変わった行。差分運用でなければ不在</param>
    /// <param name="selection">差分運用で選んだ変異候補の範囲を見分ける鍵。全体の指紋にだけ含める。差分運用でなければ空</param>
    /// <returns>閉包の同一性と全体の指紋。入力を読めなければ不在</returns>
    ReuseIdentity? Compute(CscInvocation invocation, string testAssemblyPath, string settings, ChangedLines? changes, string selection);
}
