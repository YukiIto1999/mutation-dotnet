using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>Stryker 互換 mutation-report.json の書き出し</summary>
public static class StrykerReport
{
    /// <summary>互換 schema が求める camelCase の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>確定結果の互換 schema での書き出し。対象をまたいで一つの報告にまとめる</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <param name="projectRoot">報告内の相対 path の基準</param>
    /// <param name="outputPath">書き出す file の path</param>
    public static void Write(MutationRunResult result, string projectRoot, string outputPath)
    {
        var files = result
            .Completed.SelectMany(target => target.Mutants.Select(mutant => (target, mutant)))
            .GroupBy(pair => pair.mutant.FilePath)
            .ToDictionary(
                group => Path.GetRelativePath(projectRoot, group.Key).Replace('\\', '/'),
                group => new
                {
                    language = "cs",
                    source = File.Exists(group.Key) ? File.ReadAllText(group.Key) : "",
                    mutants = group.Select(pair => Describe(pair.target, pair.mutant)).ToArray(),
                }
            );
        var report = new
        {
            schemaVersion = "2",
            thresholds = new { high = 80, low = 60 },
            projectRoot,
            files,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, Serializer));
    }

    /// <summary>変異一件の互換 schema での記述。連番は対象名で修飾して一意にする</summary>
    private static object Describe(TargetResult target, Mutant mutant)
    {
        var verdict = target.Verdicts[mutant.Id];
        return new
        {
            id = $"{target.Name}:{mutant.Id.Value.ToString(CultureInfo.InvariantCulture)}",
            mutatorName = mutant.OperatorName,
            replacement = mutant.Replacement,
            location = new
            {
                start = new { line = mutant.Span.Line, column = mutant.Span.Column },
                end = new { line = mutant.Span.EndLine, column = mutant.Span.EndColumn },
            },
            status = VerdictNames.For(verdict),
            killedBy = verdict is MutantVerdict.Killed killed ? new[] { killed.KillerTest } : Array.Empty<string>(),
            @static = mutant.InStaticContext,
        };
    }
}
