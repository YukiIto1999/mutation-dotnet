using Mutation.Mutating.Domain;
using Microsoft.Build.Logging.StructuredLogger;
using Microsoft.CodeAnalysis;
using StructuredTask = Microsoft.Build.Logging.StructuredLogger.Task;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>binlog からの csc 呼び出しの回収</summary>
public static class BinlogCompilations
{
    /// <summary>binlog に記録された全 project の csc 呼び出しの列挙。参照専用 assembly の生成は除く</summary>
    /// <param name="binlogPath">読み取る binlog の path</param>
    /// <returns>project path から csc 呼び出しへの対応。同一 project の複数回は最後を採る</returns>
    public static IReadOnlyDictionary<string, CscInvocation> Read(string binlogPath)
    {
        var build = BinaryLog.ReadBuild(binlogPath);
        var invocations = new Dictionary<string, CscInvocation>(StringComparer.OrdinalIgnoreCase);
        build.VisitAllChildren<StructuredTask>(task =>
        {
            if (task.Name != "Csc" || string.IsNullOrEmpty(task.CommandLineArguments))
            {
                return;
            }

            var project = task.GetNearestParent<Project>();
            if (project is null)
            {
                return;
            }

            var arguments = StripExecutable(
                CommandLineParser.SplitCommandLineIntoArguments(task.CommandLineArguments, removeHashComments: false)
            );
            if (arguments.Any(a => a is "/refonly" or "-refonly" or "/refonly+" or "-refonly+"))
            {
                return;
            }

            var output = OutputArgument(arguments, Path.GetDirectoryName(project.ProjectFile) ?? "");
            if (output is null)
            {
                return;
            }

            invocations[Path.GetFullPath(project.ProjectFile)] = new CscInvocation(
                Path.GetFullPath(project.ProjectFile),
                arguments,
                output
            );
        });
        return invocations;
    }

    /// <summary>先頭の csc 実行 file を除いた引数列の取り出し</summary>
    private static List<string> StripExecutable(IEnumerable<string> tokens)
    {
        var list = tokens.ToList();
        var firstOption = list.FindIndex(IsOptionLike);
        return firstOption <= 0 ? list : list.Skip(firstOption).ToList();
    }

    /// <summary>token が csc のオプションに見えるかの判定。Linux の絶対 path を誤認しない形</summary>
    private static bool IsOptionLike(string token) =>
        token.StartsWith('-')
        || token.StartsWith('@')
        || (token.StartsWith('/') && !token.AsSpan(1).Contains('/'));

    /// <summary>/out: 引数からの出力 path の取り出し。なければ不在</summary>
    private static string? OutputArgument(List<string> arguments, string baseDirectory)
    {
        foreach (var argument in arguments)
        {
            var isOut =
                argument.StartsWith("/out:", StringComparison.Ordinal)
                || argument.StartsWith("-out:", StringComparison.Ordinal);
            if (isOut)
            {
                var value = argument[(argument.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim('"');
                return Path.GetFullPath(value, baseDirectory);
            }
        }

        return null;
    }
}
