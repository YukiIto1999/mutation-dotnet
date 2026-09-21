using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>段階別と対象別と変異別の実測時間の書き出し</summary>
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
                mutants = result.Completed.Sum(t => t.Mutants.Count),
                tests = result.Completed.Sum(t => t.Tests.Count),
                score = result.Score,
                workerRestarts = result.Completed.Sum(t => t.WorkerRestarts),
                failedTargets = result.Failures.Count(),
                byStatus = CountByStatus([.. result.Completed]),
            },
            targets = result.Outcomes.Select(Describe).ToArray(),
            mutants = result
                .Completed.SelectMany(target => target.MutantTimings.Select(timing => (target, timing)))
                .OrderByDescending(pair => pair.timing.ElapsedMs)
                .Select(pair => new
                {
                    target = pair.target.Name,
                    id = pair.timing.MutantId.Value,
                    status = VerdictNames.For(pair.target.Verdicts[pair.timing.MutantId]),
                    elapsedMs = pair.timing.ElapsedMs,
                    executedTests = pair.timing.ExecutedTests,
                    isolated = pair.timing.Isolated,
                })
                .ToArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, Serializer));
    }

    /// <summary>対象一件の帰結の記述。中断した対象は理由の型名だけを残す</summary>
    private static object Describe(TargetOutcome outcome) =>
        outcome switch
        {
            TargetOutcome.Completed completed => Completion(completed.Result),
            TargetOutcome.Failed failed => new
            {
                name = failed.Name,
                failed = true,
                reason = failed.Failure.GetType().Name,
                elapsedMs = failed.ElapsedMs,
            },
        };

    /// <summary>判定まで到達した対象の段階別時間と件数の記述</summary>
    private static object Completion(TargetResult target) =>
        new
        {
            name = target.Name,
            failed = false,
            mutants = target.Mutants.Count,
            tests = target.Tests.Count,
            score = target.DetectedCount + target.UndetectedCount == 0
                ? (double?)null
                : (double)target.DetectedCount / (target.DetectedCount + target.UndetectedCount),
            workerRestarts = target.WorkerRestarts,
            mutateMs = target.Timings.MutateMs,
            compileMs = target.Timings.CompileMs,
            baselineMs = target.Timings.BaselineMs,
            testingMs = target.Timings.TestingMs,
            byStatus = CountByStatus([target]),
        };

    /// <summary>判定名ごとの件数の集計</summary>
    private static Dictionary<string, int> CountByStatus(IReadOnlyList<TargetResult> targets) =>
        targets
            .SelectMany(t => t.Verdicts.Values)
            .GroupBy(VerdictNames.For)
            .ToDictionary(group => group.Key, group => group.Count());
}
