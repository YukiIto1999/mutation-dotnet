using Mutation.Domain;
using Mutation.Infrastructure.Dotnet;

namespace Mutation.Infrastructure.Workers;

/// <summary>この process が worker を起動するときの既定の host の決定</summary>
public static class DefaultWorkerHost
{
    /// <summary>worker assembly の file 名</summary>
    private const string DllFileName = "Mutation.Worker.dll";

    /// <summary>この process の環境と配置から決めた host 実行体と worker assembly の組</summary>
    /// <remarks>明示した host を優先し、apphost 自身を dotnet host として再起動しない</remarks>
    /// <returns>走行の間使う host。環境を読むのはここだけで、読んだ後の値は変わらない</returns>
    public static WorkerHost Resolve() =>
        new(
            DotnetHost.Resolve(),
            Path.Combine(AppContext.BaseDirectory, DllFileName)
        );
}
