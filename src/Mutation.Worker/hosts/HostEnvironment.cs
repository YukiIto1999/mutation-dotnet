using Mutation.Protocol;
using System.Reflection;
using System.Runtime.Loader;

namespace Mutation.Worker;

/// <summary>テスト assembly を自 process へ読み込むための環境の整備</summary>
public static class HostEnvironment
{
    /// <summary>起動時の環境変数で変異が活性化済みか</summary>
    public static bool IsPreactivated { get; } =
        Environment.GetEnvironmentVariable(Mutation.Protocol.InjectionContract.ActiveEnvironmentVariable) is { Length: > 0 };

    /// <summary>assembly 解決の差し替えと基準 directory の付け替えを伴う、変異 assembly の読み込み</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <param name="mutatedDirectory">変異 assembly の置き場</param>
    /// <param name="targetAssemblyName">差し替え対象 assembly の単純名</param>
    /// <returns>読み込んだ変異 assembly</returns>
    public static Assembly Prepare(string testAssembly, string mutatedDirectory, string targetAssemblyName)
    {
        var testDirectory = Path.GetDirectoryName(testAssembly) ?? ".";
        var mutatedPath = Path.Combine(mutatedDirectory, targetAssemblyName + ".dll");
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", testDirectory + Path.DirectorySeparatorChar);
        Environment.CurrentDirectory = testDirectory;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (string.Equals(name.Name, targetAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return context.LoadFromAssemblyPath(mutatedPath);
            }

            var candidate = Path.Combine(testDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(mutatedPath);
    }

    /// <summary>テスト assembly の directory から使うテスト基盤の判別</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <returns>Microsoft.Testing.Platform の実行体なら真</returns>
    public static bool IsTestingPlatform(string testAssembly)
    {
        var testDirectory = Path.GetDirectoryName(testAssembly) ?? ".";
        // VSTest 用に build した MSTest も MTP の assembly を同梱するため、実行体である(entry point を持つ)ことまで求める
        return File.Exists(Path.Combine(testDirectory, "Microsoft.Testing.Platform.dll"))
            && !File.Exists(Path.Combine(testDirectory, "xunit.core.dll"))
            && HasEntryPoint(testAssembly);
    }

    /// <summary>assembly が entry point を持つかの判定</summary>
    private static bool HasEntryPoint(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
        return reader.PEHeaders.CorHeader is { EntryPointTokenOrRelativeVirtualAddress: not 0 };
    }
}
