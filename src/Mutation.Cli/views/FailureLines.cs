using Mutation.Shared;



namespace Mutation.Cli;

/// <summary>想定内の失敗から端末へ出す説明への変換</summary>
public static class FailureLines
{
    /// <summary>一つの失敗の、人が読む説明への変換</summary>
    /// <param name="failure">実行を中断した想定内の失敗</param>
    /// <returns>端末へ出す説明</returns>
    public static string Describe(PipelineFailure failure) =>
        failure switch
        {
            PipelineFailure.BuildFailed failed => $"dotnet build が失敗した:\n{failed.Detail}",
            PipelineFailure.CompilerArgumentsNotFound notFound =>
                $"binlog に {notFound.ProjectPath} の csc 呼び出しが見つからない",
            PipelineFailure.CompileFailed compileFailed => $"変異コンパイルが失敗した:\n{compileFailed.Diagnostics}",
            PipelineFailure.WorkerFailed workerFailed => $"テスト worker が失敗した: {workerFailed.Reason}",
            PipelineFailure.SinceUnavailable since => $"--since の差分を解決できない: {since.Reason}",
            PipelineFailure.NoTestsFound noTests => $"{noTests.TestAssembly} からテストが見つからない",
        };
}
