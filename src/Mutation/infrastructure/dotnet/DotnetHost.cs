namespace Mutation.Infrastructure.Dotnet;

/// <summary>build と worker DLL を起動する dotnet host の所在</summary>
internal static class DotnetHost
{
    /// <summary>明示した host、実行中の dotnet、runtime の配置、PATH の順での解決</summary>
    /// <returns>dotnet 実行体の path。実体を特定できない場合は PATH で解く名前</returns>
    public static string Resolve()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } configured)
        {
            return configured;
        }

        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (Environment.ProcessPath is { } process && Path.GetFileName(process).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return process;
        }

        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        var root = Environment.GetEnvironmentVariable($"DOTNET_ROOT_{architecture}") ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        return root is { Length: > 0 } && File.Exists(Path.Combine(root, name))
            ? Path.Combine(root, name)
            : name;
    }
}
