

namespace Mutation.Verifying.Application;

/// <summary>出力 directory 配下の置き場の取り決め</summary>
/// <param name="OutputDirectory">報告と中間物を置く root</param>
public sealed record RunLayout(string OutputDirectory)
{
    /// <summary>対象間で共有する build の中間物の置き場</summary>
    public string WorkDirectory => Path.Combine(OutputDirectory, "work");

    /// <summary>報告の置き場。対象をまたいで一つ</summary>
    public string ReportsDirectory => Path.Combine(OutputDirectory, "reports");

    /// <summary>対象一件の置き場の取り出し</summary>
    /// <param name="targetName">対象 project の名前</param>
    /// <returns>対象ごとに分かれた置き場の取り決め</returns>
    public TargetLayout For(string targetName) => new(OutputDirectory, targetName);
}

/// <summary>対象一件の置き場の取り決め</summary>
/// <param name="OutputDirectory">報告と中間物を置く root</param>
/// <param name="TargetName">対象 project の名前</param>
public sealed record TargetLayout(string OutputDirectory, string TargetName)
{
    /// <summary>対象の中間物の置き場</summary>
    public string WorkDirectory => Path.Combine(OutputDirectory, "work", TargetName);

    /// <summary>変異 assembly の置き場</summary>
    public string MutatedDirectory => Path.Combine(OutputDirectory, "mutated", TargetName);

    /// <summary>worker 内のテスト実行体が成果物を書く置き場</summary>
    public string TestResultsDirectory => Path.Combine(WorkDirectory, "test-results");

    /// <summary>継承元の保存 file の置き場</summary>
    public string SnapshotPath => Path.Combine(WorkDirectory, "snapshot.json");

    /// <summary>元の出力 file 名に対応する変異 assembly の path</summary>
    /// <param name="originalOutputPath">対象 project の元の出力 path</param>
    /// <returns>変異 assembly の絶対 path</returns>
    public string MutatedAssemblyPath(string originalOutputPath) =>
        Path.Combine(MutatedDirectory, Path.GetFileName(originalOutputPath));
}
