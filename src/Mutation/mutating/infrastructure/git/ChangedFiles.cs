using Mutation.Shared;
using System.Diagnostics;

using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>差分運用で対象を絞るための変更ファイルの解決</summary>
public static class ChangedFiles
{
    /// <summary>基点からの差分と未追跡ファイルの、絶対 path 集合としての取得</summary>
    /// <param name="projectDirectory">対象 project の directory。git repo の中にあること</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変更ファイルの絶対 path 集合、失敗なら理由</returns>
    public static Result<IReadOnlySet<string>, PipelineFailure> Resolve(string projectDirectory, string sinceRef)
    {
        var toplevel = Run(projectDirectory, ["rev-parse", "--show-toplevel"]);
        if (toplevel is Result<IReadOnlyList<string>, PipelineFailure>.Failed(var rootFailure))
        {
            return new Result<IReadOnlySet<string>, PipelineFailure>.Failed(rootFailure);
        }

        var root = ((Result<IReadOnlyList<string>, PipelineFailure>.Succeeded)toplevel).Value[0];
        var diff = Run(projectDirectory, ["diff", "--name-only", sinceRef, "--"]);
        if (diff is Result<IReadOnlyList<string>, PipelineFailure>.Failed(var diffFailure))
        {
            return new Result<IReadOnlySet<string>, PipelineFailure>.Failed(diffFailure);
        }

        var untracked = Run(projectDirectory, ["ls-files", "--others", "--exclude-standard"]);
        var lines = ((Result<IReadOnlyList<string>, PipelineFailure>.Succeeded)diff).Value.Concat(
            untracked is Result<IReadOnlyList<string>, PipelineFailure>.Succeeded extra ? extra.Value : []
        );
        var files = lines
            .Where(line => line.Length > 0)
            .Select(line => Path.GetFullPath(Path.Combine(root, line)))
            .ToHashSet(StringComparer.Ordinal);
        return new Result<IReadOnlySet<string>, PipelineFailure>.Succeeded(files);
    }

    /// <summary>git の一 command の実行と標準出力の行の取得</summary>
    private static Result<IReadOnlyList<string>, PipelineFailure> Run(
        string workingDirectory,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GitLocator.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new Result<IReadOnlyList<string>, PipelineFailure>.Failed(
                new PipelineFailure.SinceUnavailable("git を起動できない")
            );
        }

        var stderr = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => stderr.AppendLine(e.Data);
        process.BeginErrorReadLine();
        var lines = process
            .StandardOutput.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return new Result<IReadOnlyList<string>, PipelineFailure>.Failed(
                new PipelineFailure.SinceUnavailable(
                    $"git {string.Join(' ', arguments)} が失敗した: {stderr.ToString().Trim()}"
                )
            );
        }

        return new Result<IReadOnlyList<string>, PipelineFailure>.Succeeded(lines);
    }
}
