using Mutation.Domain;

namespace Mutation.Infrastructure.Workers;

/// <summary>この process が worker を起動するときの既定の host の決定</summary>
public static class DefaultWorkerHost
{
    /// <summary>worker assembly の file 名</summary>
    private const string DllFileName = "Mutation.Worker.dll";

    /// <summary>この process の環境と配置から決めた host 実行体と worker assembly の組</summary>
    /// <returns>走行の間使う host。環境を読むのはここだけで、読んだ後の値は変わらない</returns>
    public static WorkerHost Resolve() =>
        new(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } dotnetHost
                ? dotnetHost
                : Environment.ProcessPath ?? "dotnet",
            Path.Combine(AppContext.BaseDirectory, DllFileName)
        );
}
