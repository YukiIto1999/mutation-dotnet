using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>段階別と変異別の実測時間の書き出し</summary>
public static class TimingsReport
{
    /// <summary>camelCase の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>ボトルネック分析に使う実測一式の書き出し</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <param name="outputPath">書き出す file の path</param>
    public static void Write(MutationRunResult result, string outputPath)
    {
        var byStatus = result
            .Verdicts.Values.GroupBy(VerdictNames.For)
            .ToDictionary(group => group.Key, group => group.Count());
        var report = new
        {
            phases = new
            {
                buildMs = result.Timings.BuildMs,
                mutateMs = result.Timings.MutateMs,
                compileMs = result.Timings.CompileMs,
                baselineMs = result.Timings.BaselineMs,
                testingMs = result.Timings.TestingMs,
                totalMs = result.Timings.TotalMs,
            },
            counters = new
            {
                mutants = result.Mutants.Count,
                tests = result.Tests.Count,
                score = result.Score,
                workerRestarts = result.WorkerRestarts,
                byStatus,
            },
            mutants = result
                .MutantTimings.OrderByDescending(t => t.ElapsedMs)
                .Select(t => new
                {
                    id = t.MutantId.Value,
                    status = VerdictNames.For(result.Verdicts[t.MutantId]),
                    elapsedMs = t.ElapsedMs,
                    executedTests = t.ExecutedTests,
                    isolated = t.Isolated,
                })
                .ToArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, Serializer));
    }
}
