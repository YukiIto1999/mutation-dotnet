using Mutation.Shared;
using System.Diagnostics;
using System.Text;

using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>対象の初回 build の実行と binlog の取得</summary>
public static class BuildExecution
{
    /// <summary>テスト project の binlog 付き build と binlog path の取得</summary>
    /// <param name="testProjectPath">build するテスト project の絶対 path</param>
    /// <param name="configuration">build 構成の名前</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら binlog の path、失敗なら build 出力付きの失敗</returns>
    public static Task<Result<string, PipelineFailure>> BuildWithBinlog(
        string testProjectPath,
        string configuration,
        string workDirectory
    ) => RunBuildAsync(testProjectPath, configuration, workDirectory, forceRebuild: false);

    /// <summary>増分 build を無効化し csc の再実行を強制する build と binlog path の取得</summary>
    /// <param name="testProjectPath">build するテスト project の絶対 path</param>
    /// <param name="configuration">build 構成の名前</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら binlog の path、失敗なら build 出力付きの失敗</returns>
    public static Task<Result<string, PipelineFailure>> RebuildWithBinlog(
        string testProjectPath,
        string configuration,
        string workDirectory
    ) => RunBuildAsync(testProjectPath, configuration, workDirectory, forceRebuild: true);

    /// <summary>dotnet build の実行と結果の取り込み</summary>
    private static async Task<Result<string, PipelineFailure>> RunBuildAsync(
        string testProjectPath,
        string configuration,
        string workDirectory,
        bool forceRebuild
    )
    {
        Directory.CreateDirectory(workDirectory);
        var binlogPath = Path.Combine(workDirectory, "build.binlog");
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnetHost
                ? dotnetHost
                : Environment.ProcessPath ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(testProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add($"-bl:{binlogPath}");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v:q");
        if (forceRebuild)
        {
            startInfo.ArgumentList.Add("--no-incremental");
        }
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new Result<string, PipelineFailure>.Failed(
                new PipelineFailure.BuildFailed("dotnet を起動できない")
            );
        }

        var output = new StringBuilder();
        var stdout = Drain(process.StandardOutput, output);
        var stderr = Drain(process.StandardError, output);
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var text = output.ToString();
            var tail = text.Length <= 4000 ? text : text[^4000..];
            return new Result<string, PipelineFailure>.Failed(new PipelineFailure.BuildFailed(tail));
        }

        return new Result<string, PipelineFailure>.Succeeded(binlogPath);
    }

    /// <summary>子 process の出力の吸い上げ</summary>
    private static async Task Drain(TextReader reader, StringBuilder sink)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (sink)
            {
                sink.AppendLine(line);
            }
        }
    }
}
