using Mutation.Verifying.Domain;


namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>一回の実行が残す全ての報告の一括書き出し</summary>
public static class ReportBundle
{
    /// <summary>互換レポートと実測の一括の書き出し</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <param name="projectRoot">互換レポート内の相対 path の基準</param>
    /// <param name="reportsDirectory">報告を置く directory</param>
    public static void Write(MutationRunResult result, string projectRoot, string reportsDirectory)
    {
        StrykerReport.Write(result, projectRoot, Path.Combine(reportsDirectory, "mutation-report.json"));
        TimingsReport.Write(result, Path.Combine(reportsDirectory, "timings.json"));
    }
}
