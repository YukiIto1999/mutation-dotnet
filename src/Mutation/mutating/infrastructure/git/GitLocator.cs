

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>子 process の起動に使う git 実行 file の解決</summary>
public static class GitLocator
{
    /// <summary>git 実行 file の絶対 path</summary>
    public static string Executable { get; } = Resolve();

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
