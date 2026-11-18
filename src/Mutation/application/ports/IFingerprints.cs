using Mutation.Domain;

namespace Mutation.Application;

/// <summary>生成入力の指紋計算を担う port</summary>
public interface IFingerprints
{
    /// <summary>走行一回の間、全ての対象で変わらない入力の捕捉</summary>
    /// <remarks>
    /// engine と compiler の assembly、渡された worker の host 実行体と worker assembly の directory の全 file、runtime の版、実行環境、設定を、走行のたびに読み直す
    /// 読めない入力が一つでもあれば、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="settings">変異の集合と判定を左右する設定。構造のまま直列化して同一性に含める</param>
    /// <param name="outputDirectory">engine 自身が成果物を書く directory。test の directory の中にあっても、test の閉包に入れない</param>
    /// <param name="worker">この走行が test を動かす worker の実体。呼び出し側が起動に使う値と同じものを渡す</param>
    /// <returns>走行の間共通の入力の閉包の部分。入力を読めなければ不在</returns>
    RunClosure? Capture(ReuseSettings settings, string outputDirectory, WorkerHost worker);

    /// <summary>判定を決める入力の閉包の同一性と、変わった行を加えた全体の指紋の計算</summary>
    /// <remarks>
    /// 閉包は、走行の間共通の部分に、csc 引数、ソース、参照する assembly、analyzer と generator、additional file、analyzer config、resource の内容と、
    /// test assembly の directory の全 file の内容を加えて求める。読めない入力が一つでもあれば、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="run">Capture が返した走行の間共通の部分</param>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="testAssemblyPath">テスト assembly の絶対 path。その directory の内容を閉包に含める</param>
    /// <param name="changes">差分運用の基点から変わった行。差分運用でなければ不在</param>
    /// <param name="selection">差分運用で選んだ変異候補の範囲を見分ける鍵。全体の指紋にだけ含める。差分運用でなければ空</param>
    /// <returns>閉包の同一性と全体の指紋の対。入力を読めなければ不在</returns>
    ReuseIdentity? Compute(
        RunClosure run,
        CscInvocation invocation,
        string testAssemblyPath,
        ChangedLines? changes,
        string selection
    );
}
