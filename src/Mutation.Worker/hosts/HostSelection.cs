namespace Mutation.Worker;

/// <summary>テスト assembly が使う基盤に応じたホストの選択</summary>
public static class HostSelection
{
    /// <summary>基盤の判別とホストの開設</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <param name="mutatedDirectory">変異 assembly の置き場</param>
    /// <param name="targetAssemblyName">差し替え対象 assembly の単純名</param>
    /// <param name="resultsDirectory">テスト実行体が成果物を書く directory</param>
    /// <returns>実行準備の整ったホスト</returns>
    public static ITestHost Open(
        string testAssembly,
        string mutatedDirectory,
        string targetAssemblyName,
        string resultsDirectory
    )
    {
        var directory = Path.GetDirectoryName(testAssembly) ?? ".";
        if (File.Exists(Path.Combine(directory, "xunit.core.dll")))
        {
            return XunitTestHost.Open(testAssembly, mutatedDirectory, targetAssemblyName);
        }

        // Test.Sdk は VSTest 用の出力にも MTP assembly と自動 entry point を同梱するため、adapter の存在を先に見る
        if (VstestTestHost.AdapterPath(directory) is { } adapterPath)
        {
            return VstestTestHost.Open(testAssembly, mutatedDirectory, targetAssemblyName, adapterPath);
        }

        if (HostEnvironment.IsTestingPlatform(testAssembly))
        {
            return PlatformTestHost.Open(testAssembly, mutatedDirectory, targetAssemblyName, resultsDirectory);
        }

        return XunitTestHost.Open(testAssembly, mutatedDirectory, targetAssemblyName);
    }
}
