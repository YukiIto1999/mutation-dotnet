using TypeModeling.Domain;

namespace Mutation.Domain;

/// <summary>実行を継続できない想定内の失敗</summary>
[Closed]
public abstract record PipelineFailure
{
    /// <summary>列挙した派生以外の失敗を塞ぐ基底の構築</summary>
    private PipelineFailure()
    {
    }

    /// <summary>対象の初回 build が失敗した</summary>
    /// <param name="Detail">build 出力の末尾</param>
    public sealed record BuildFailed(string Detail) : PipelineFailure;

    /// <summary>binlog から対象 project のコンパイル引数を見つけられなかった</summary>
    /// <param name="ProjectPath">探した project の path</param>
    public sealed record CompilerArgumentsNotFound(string ProjectPath) : PipelineFailure;

    /// <summary>schemata 込みの再コンパイルが rollback を尽くしても失敗した</summary>
    /// <param name="Diagnostics">残った compile error の説明</param>
    public sealed record CompileFailed(string Diagnostics) : PipelineFailure;

    /// <summary>テスト worker が起動または応答しなかった</summary>
    /// <param name="Reason">worker の失敗内容</param>
    public sealed record WorkerFailed(string Reason) : PipelineFailure;

    /// <summary>差分運用の基点から変更ファイルを解決できなかった</summary>
    /// <param name="Reason">git の失敗内容</param>
    public sealed record SinceUnavailable(string Reason) : PipelineFailure;

    /// <summary>対象のテストが一件も見つからなかった</summary>
    /// <param name="TestAssembly">探したテスト assembly の path</param>
    public sealed record NoTestsFound(string TestAssembly) : PipelineFailure;

    /// <summary>変異検査の報告を読めなかった</summary>
    /// <param name="ReportPath">読もうとした報告の path</param>
    /// <param name="Reason">読めなかった理由</param>
    public sealed record ReportUnreadable(string ReportPath, string Reason) : PipelineFailure;

    /// <summary>取り消しを受けて止まり、この対象は判定に至らなかった</summary>
    public sealed record Interrupted : PipelineFailure;

    /// <summary>変わった行に重なる変異に判定を確定できなかったものがあり、変わった行の合否を決められなかった</summary>
    /// <param name="Count">判定を確定できなかった変異の件数</param>
    /// <param name="Unanalysed">変更行の選別が未分析として残した影響の関係と件数。合否を決められなくても、判定がそれらを含まないことは変わらない</param>
    public sealed record UnjudgedOnChangedLines(int Count, IReadOnlyList<string> Unanalysed) : PipelineFailure;

    /// <summary>報告を書いた走行が判定に至らなかった対象を残して失敗しており、その報告は変わった行の合否の根拠にならない</summary>
    /// <param name="ReportPath">読んだ報告の path</param>
    /// <param name="Targets">失敗で判定に至らなかった対象の名前</param>
    public sealed record ReportOfFailedRun(string ReportPath, IReadOnlyList<string> Targets) : PipelineFailure;

    /// <summary>報告を書いた走行が取り消しを受けて止まっており、その報告は変わった行の合否の根拠にならない</summary>
    /// <param name="ReportPath">読んだ報告の path</param>
    /// <param name="Targets">取り消しで判定に至らなかった対象の名前</param>
    public sealed record ReportOfInterruptedRun(string ReportPath, IReadOnlyList<string> Targets) : PipelineFailure;
}
