using System.Diagnostics;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>子 process の起動に使う git 実行 file の解決と起動情報</summary>
public static class GitLocator
{
    /// <summary>
    /// repository の位置を決める環境変数(`git rev-parse --local-env-vars`)。git の hook の中では呼び出し元の
    /// repository を指して export されており、残すと作業 directory でなくその repository を読む
    /// </summary>
    private static readonly string[] RepositoryLocalVariables =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_CONFIG",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
        "GIT_OBJECT_DIRECTORY",
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE",
        "GIT_GRAFT_FILE",
        "GIT_INDEX_FILE",
        "GIT_NO_REPLACE_OBJECTS",
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR",
    ];

    /// <summary>git 実行 file の絶対 path</summary>
    public static string Executable { get; } = Resolve();

    /// <summary>作業 directory を含む repository を読む git の、標準出力と標準エラーを呼び出し側が読む起動情報</summary>
    /// <param name="workingDirectory">git を起動する directory</param>
    /// <returns>repository の位置を決める環境変数を外した起動情報</returns>
    public static ProcessStartInfo StartInfo(string workingDirectory) =>
        WithoutRepositoryLocation(
            new ProcessStartInfo(Executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );

    /// <summary>起動情報の環境からの、repository の位置を決める環境変数の除去</summary>
    /// <param name="startInfo">git の起動情報</param>
    /// <returns>環境変数を除いた同じ起動情報</returns>
    public static ProcessStartInfo WithoutRepositoryLocation(ProcessStartInfo startInfo)
    {
        foreach (var name in RepositoryLocalVariables)
        {
            startInfo.Environment.Remove(name);
        }

        return startInfo;
    }

    /// <summary>PATH からの実行 file の解決</summary>
    private static string Resolve()
    {
        var fileName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("PATH から git を見つけられない");
    }
}
