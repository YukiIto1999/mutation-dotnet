using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text.Json;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>Stryker 互換 mutation-report.json の書き出し</summary>
public static class StrykerReport
{
    /// <summary>互換 schema が求める camelCase の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>確定結果の互換 schema での書き出し</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <param name="projectRoot">報告内の相対 path の基準</param>
    /// <param name="outputPath">書き出す file の path</param>
    public static void Write(MutationRunResult result, string projectRoot, string outputPath)
    {
        var files = result
            .Mutants.GroupBy(m => m.FilePath)
            .ToDictionary(
                group => Path.GetRelativePath(projectRoot, group.Key).Replace('\\', '/'),
                group => new
                {
                    language = "cs",
                    source = File.Exists(group.Key) ? File.ReadAllText(group.Key) : "",
                    mutants = group
                        .Select(m => new
                        {
                            id = m.Id.Value.ToString(CultureInfo.InvariantCulture),
                            mutatorName = m.OperatorName,
                            replacement = m.Replacement,
                            location = new
                            {
                                start = new { line = m.Span.Line, column = m.Span.Column },
                                end = new { line = m.Span.EndLine, column = m.Span.EndColumn },
                            },
                            status = VerdictNames.For(result.Verdicts[m.Id]),
                            killedBy = result.Verdicts[m.Id] is MutantVerdict.Killed killed
                                ? new[] { killed.KillerTest }
                                : Array.Empty<string>(),
                            @static = m.InStaticContext,
                        })
                        .ToArray(),
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
}
