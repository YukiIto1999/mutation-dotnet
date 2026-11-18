using Mutation.Domain;
using System.Diagnostics;

using TypeModeling.Domain;

namespace Mutation.Infrastructure.Git;

/// <summary>差分運用で対象を絞るための、基点から変わったファイルと行の解決</summary>
public static class ChangedFiles
{
    /// <summary>基点と作業木の差分と未追跡ファイルの、変わったファイルと行としての取得</summary>
    /// <remarks>
    /// 未追跡ファイルは全行が変わったものとする。移動したファイルは削除と追加とみなし、移動先の全行が変わったものとする
    /// 消えたファイルと、行の差分を持たない binary のファイルも、変わったファイルとして空の範囲で含め、消えたファイルは <see cref="ChangedLines.Deleted"/> にも載せる
    /// git のどの command が失敗しても失敗を返す
    /// </remarks>
    /// <param name="projectDirectory">git repo の中にある directory</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変わったファイルと行、失敗なら理由</returns>
    public static Result<ChangedLines, PipelineFailure> Resolve(string projectDirectory, string sinceRef)
    {
        var toplevel = Run(projectDirectory, ["rev-parse", "--show-toplevel"]);
        if (toplevel.IsFailed(out var rootFailure, out var rootOutput))
        {
            return rootFailure;
        }

        var root = rootOutput.Trim();
        var baseCommit = Run(projectDirectory, ["rev-parse", "--verify", "--end-of-options", $"{sinceRef}^{{commit}}"]);
        if (baseCommit.IsFailed(out var baseFailure, out var baseOutput))
        {
            return baseFailure;
        }

        var diff = Run(
            projectDirectory,
            [
                "-c", "core.quotePath=false",
                "diff", "-U0", "--no-color", "--no-ext-diff", "--no-textconv", "--no-relative", "--no-renames",
                $"--dst-prefix={UnifiedDiff.NewSidePrefix}",
                baseOutput.Trim(), "--",
            ]
        );
        if (diff.IsFailed(out var diffFailure, out var diffOutput))
        {
            return diffFailure;
        }

        var names = Run(
            projectDirectory,
            [
                "diff", "--name-only", "-z", "--no-color", "--no-ext-diff", "--no-textconv", "--no-relative", "--no-renames",
                baseOutput.Trim(), "--",
            ]
        );
        if (names.IsFailed(out var namesFailure, out var namesOutput))
        {
            return namesFailure;
        }

        var untracked = Run(root, ["ls-files", "-z", "--others", "--exclude-standard", "--full-name"]);
        if (untracked.IsFailed(out var untrackedFailure, out var untrackedOutput))
        {
            return untrackedFailure;
        }

        var ranges = WithUntracked(root, diffOutput, untrackedOutput);

        var deleted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in namesOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.GetFullPath(Path.Combine(root, name));
            ranges.TryAdd(path, []);
            if (!File.Exists(path))
            {
                deleted.Add(path);
            }
        }

        return Result.Succeeded(new ChangedLines(ranges, deleted));
    }

    /// <summary>追跡済み差分への未追跡ファイルの全行範囲の追加</summary>
    /// <param name="root">repository の絶対 path</param>
    /// <param name="diff">追跡済みファイルの unified diff</param>
    /// <param name="untracked">NUL 区切りの未追跡ファイルの相対 path</param>
    /// <returns>ファイルごとの変更行範囲</returns>
    private static Dictionary<string, IReadOnlyList<LineRange>> WithUntracked(string root, string diff, string untracked)
    {
        var ranges = new Dictionary<string, IReadOnlyList<LineRange>>(
            UnifiedDiff.Parse(root, diff.Split('\n')),
            StringComparer.Ordinal
        );
        foreach (var file in untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            ranges[Path.GetFullPath(Path.Combine(root, file))] = [LineRange.WholeFile];
        }

        return ranges;
    }

    /// <summary>git の一 command の実行と標準出力の取得</summary>
    private static Result<string, PipelineFailure> Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = GitLocator.StartInfo(workingDirectory);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderr = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => stderr.AppendLine(e.Data);
        process.BeginErrorReadLine();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return Result.Failed<PipelineFailure>(
                new PipelineFailure.SinceUnavailable(
                    $"git {string.Join(' ', arguments)} が失敗した: {stderr.ToString().Trim()}"
                )
            );
        }

        return Result.Succeeded(output);
    }
}
