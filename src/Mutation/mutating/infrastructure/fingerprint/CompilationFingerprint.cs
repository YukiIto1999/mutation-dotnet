using Mutation.Mutating.Domain;
using Mutation.Shared;
using Microsoft.CodeAnalysis.CSharp;

namespace Mutation.Mutating.Infrastructure;

/// <summary>変異の生成入力と判定を決める入力が前回と同一かを見分ける指紋</summary>
public static class CompilationFingerprint
{
    /// <summary>判定を決める入力の閉包の同一性と、変わった行と候補の範囲を加えた全体の指紋の計算</summary>
    /// <remarks>
    /// 閉包は、csc 引数の文字列、ソース、additional file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵の内容、
    /// test assembly の directory の内容、engine と worker と compiler の assembly と runtime の版、実行環境、設定の要約から求める
    /// 全体の指紋は、閉包に、対象のソースごとの変わった行の範囲と、差分運用で選んだ候補の範囲を加える。対象外のファイルの変化では変わらない
    /// 一つでも入力を読めなければ、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="testAssemblyPath">テスト assembly の絶対 path。その directory の内容を閉包に含める</param>
    /// <param name="settings">変異の集合と判定に影響する設定の要約</param>
    /// <param name="changes">差分運用の基点から変わった行。差分運用でなければ不在</param>
    /// <param name="selection">差分運用で選んだ変異候補の範囲を見分ける鍵。差分運用でなければ空</param>
    /// <returns>閉包の同一性と全体の指紋。入力を読めなければ不在</returns>
    public static ReuseIdentity? Compute(
        CscInvocation invocation,
        string testAssemblyPath,
        string settings,
        ChangedLines? changes,
        string selection
    )
    {
        var baseDirectory = Path.GetDirectoryName(invocation.ProjectFile) ?? ".";
        var arguments = CSharpCommandLineParser.Default.Parse(invocation.Arguments, baseDirectory, sdkDirectory: null);
        var compile = InputClosure.Describe(InputClosure.CompileInputs(arguments, invocation.Arguments, baseDirectory));
        var tests = InputClosure.TestClosure(testAssemblyPath);
        if (compile is null || tests is null || InputClosure.Toolchain is not { } toolchain)
        {
            return null;
        }

        var closure = InputClosure.Sha256Of(
            string.Join('\n', string.Join('\n', invocation.Arguments), compile, tests, toolchain, ExecutionEnvironment.Identity(), settings)
        );
        return new ReuseIdentity(
            closure,
            InputClosure.Sha256Of(string.Join('\n', closure, ChangedRanges(arguments, changes), selection))
        );
    }

    /// <summary>対象のソースごとの変わった行の範囲の記述。内容が変わっていないソースは含めない</summary>
    private static string ChangedRanges(CSharpCommandLineArguments arguments, ChangedLines? changes) =>
        changes is null
            ? ""
            : string.Join(
                '\n',
                arguments
                    .SourceFiles.Select(file => file.Path)
                    .Order(StringComparer.Ordinal)
                    .Select(path => changes.RangesOf(path) is { } ranges ? $"{path}:{string.Join(',', ranges)}" : null)
                    .OfType<string>()
            );
}
