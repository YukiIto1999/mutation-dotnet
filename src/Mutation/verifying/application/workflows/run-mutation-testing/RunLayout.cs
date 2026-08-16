


namespace Mutation.Verifying.Application;

/// <summary>出力 directory 配下の置き場の取り決め</summary>
/// <param name="OutputDirectory">報告と中間物を置く root</param>
public sealed record RunLayout(string OutputDirectory)
{
    /// <summary>build と中間物の置き場</summary>
    public string WorkDirectory => Path.Combine(OutputDirectory, "work");

    /// <summary>変異 assembly の置き場</summary>
    public string MutatedDirectory => Path.Combine(OutputDirectory, "mutated");

    /// <summary>報告の置き場</summary>
    public string ReportsDirectory => Path.Combine(OutputDirectory, "reports");

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
