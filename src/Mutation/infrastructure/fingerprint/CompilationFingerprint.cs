using Mutation.Domain;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace Mutation.Infrastructure;

/// <summary>変異の生成入力と判定を決める入力が前回と同一かを見分ける指紋</summary>
public static class CompilationFingerprint
{
    /// <summary>走行一回の間、全ての対象で変わらない入力の捕捉</summary>
    /// <remarks>
    /// engine と compiler の assembly、渡された worker の host 実行体と worker assembly の directory の全 file、runtime の版、実行環境、設定から求める
    /// 設定は構造のまま JSON へ直列化して hash するので、列の区切りで二つの設定が同じ記述になることはない
    /// process の間で使い回さないので、呼ぶたびに読み直す。一つでも読めなければ、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="settings">変異の集合と判定を左右する設定</param>
    /// <param name="outputDirectory">engine 自身が成果物を書く directory。test の directory の中にあっても、test の閉包に入れない</param>
    /// <param name="worker">test を動かす worker の実体</param>
    /// <returns>走行の間共通の入力の閉包の部分。入力を読めなければ不在</returns>
    public static RunClosure? Capture(ReuseSettings settings, string outputDirectory, WorkerHost worker)
    {
        if (InputClosure.DescribeToolchain(worker) is not { } toolchain)
        {
            return null;
        }

        var inputs = new RunInputs(toolchain, ExecutionEnvironment.Identity(), settings);
        return new RunClosure(InputClosure.Sha256Of(JsonSerializer.Serialize(inputs)), Path.GetFullPath(outputDirectory));
    }

    /// <summary>判定を決める入力の閉包の同一性と、変わった行と候補の範囲を加えた全体の指紋の計算</summary>
    /// <remarks>
    /// 閉包は、走行の間共通の部分に、csc 引数の文字列、ソース、additional file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵の内容、
    /// test assembly の directory の全 file の内容を加えて求める
    /// 全体の指紋は、閉包に、対象のソースごとの変わった行の範囲と、差分運用で選んだ候補の範囲を加える。対象外のファイルの変化では変わらない
    /// 一つでも入力を読めなければ、同一性を示せないので不在を返す
    /// </remarks>
    /// <param name="run">Capture が返した走行の間共通の部分</param>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="testAssemblyPath">テスト assembly の絶対 path。その directory の内容を閉包に含める</param>
    /// <param name="changes">差分運用の基点から変わった行。差分運用でなければ不在</param>
    /// <param name="selection">差分運用で選んだ変異候補の範囲を見分ける鍵。差分運用でなければ空</param>
    /// <returns>閉包の同一性と全体の指紋の対。入力を読めなければ不在</returns>
    public static ReuseIdentity? Compute(
        RunClosure run,
        CscInvocation invocation,
        string testAssemblyPath,
        ChangedLines? changes,
        string selection
    )
    {
        var baseDirectory = Path.GetDirectoryName(invocation.ProjectFile) ?? ".";
        var arguments = CSharpCommandLineParser.Default.Parse(invocation.Arguments, baseDirectory, sdkDirectory: null);
        var compile = InputClosure.Describe(InputClosure.CompileInputs(arguments, invocation.Arguments, baseDirectory));
        var tests = InputClosure.TestClosure(testAssemblyPath, run.OwnedDirectory);
        if (compile is null || tests is null)
        {
            return null;
        }

        var closure = InputClosure.Sha256Of(string.Join('\n', string.Join('\n', invocation.Arguments), compile, tests, run.Digest));
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

/// <summary>走行の間共通の入力の、hash の元になる構造</summary>
/// <param name="Toolchain">engine と compiler と worker の実体と runtime の版の記述</param>
/// <param name="EnvironmentIdentity">実行環境の同一性</param>
/// <param name="Settings">変異の集合と判定を左右する設定</param>
internal sealed record RunInputs(string Toolchain, string EnvironmentIdentity, ReuseSettings Settings);
