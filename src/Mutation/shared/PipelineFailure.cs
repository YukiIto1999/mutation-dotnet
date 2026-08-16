using TypeModeling.Domain;

namespace Mutation.Shared;

/// <summary>実行を継続できない想定内の失敗</summary>
[ClosedUnion]
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
}
