using Mutation.Shared;
using System.Diagnostics;

using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>差分運用で対象を絞るための、基点から変わったファイルと行の解決</summary>
public static class ChangedFiles
{
    /// <summary>基点と作業木の差分と未追跡ファイルの、変わったファイルと行としての取得</summary>
    /// <remarks>未追跡ファイルは全行が変わったものとする。git のどの command が失敗しても失敗を返す</remarks>
    /// <param name="projectDirectory">git repo の中にある directory</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変わったファイルと行、失敗なら理由</returns>
    public static Result<ChangedLines, PipelineFailure> Resolve(string projectDirectory, string sinceRef)
    {
        var toplevel = Run(projectDirectory, ["rev-parse", "--show-toplevel"]);
        if (toplevel is Result<string, PipelineFailure>.Failed(var rootFailure))
        {
            return new Result<ChangedLines, PipelineFailure>.Failed(rootFailure);
        }

        var root = ((Result<string, PipelineFailure>.Succeeded)toplevel).Value.Trim();
        var diff = Run(
            projectDirectory,
            [
                "-c", "core.quotePath=false",
                "diff", "-U0", "--no-color", "--no-ext-diff", "--no-textconv", "--no-relative",
                "--src-prefix=a/", "--dst-prefix=b/", sinceRef, "--",
            ]
        );
        if (diff is Result<string, PipelineFailure>.Failed(var diffFailure))
        {
            return new Result<ChangedLines, PipelineFailure>.Failed(diffFailure);
        }

        var untracked = Run(projectDirectory, ["ls-files", "-z", "--others", "--exclude-standard", "--full-name"]);
        if (untracked is Result<string, PipelineFailure>.Failed(var untrackedFailure))
        {
            return new Result<ChangedLines, PipelineFailure>.Failed(untrackedFailure);
        }

        var diffLines = ((Result<string, PipelineFailure>.Succeeded)diff).Value.Split('\n');
        var untrackedFiles = ((Result<string, PipelineFailure>.Succeeded)untracked)
            .Value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var ranges = new Dictionary<string, IReadOnlyList<LineRange>>(
            UnifiedDiff.Parse(root, diffLines),
            StringComparer.Ordinal
        );
        foreach (var file in untrackedFiles)
        {
            ranges[Path.GetFullPath(Path.Combine(root, file))] = [LineRange.WholeFile];
        }

        return new Result<ChangedLines, PipelineFailure>.Succeeded(new ChangedLines(ranges));
    }

    /// <summary>git の一 command の実行と標準出力の取得</summary>
    private static Result<string, PipelineFailure> Run(string workingDirectory, IReadOnlyList<string> arguments)
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
            return new Result<string, PipelineFailure>.Failed(new PipelineFailure.SinceUnavailable("git を起動できない"));
        }

        var stderr = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => stderr.AppendLine(e.Data);
        process.BeginErrorReadLine();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return new Result<string, PipelineFailure>.Failed(
                new PipelineFailure.SinceUnavailable(
                    $"git {string.Join(' ', arguments)} が失敗した: {stderr.ToString().Trim()}"
                )
            );
        }

        return new Result<string, PipelineFailure>.Succeeded(output);
    }
}
