using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text.Json;
using TypeModeling.Domain;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>Stryker 互換 mutation-report.json の書き出しと読み戻し</summary>
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

    /// <summary>書き出した互換 schema の報告の読み戻し</summary>
    /// <remarks>未知の status は判定を偽らないよう、読めない報告として扱う</remarks>
    /// <param name="json">報告の内容</param>
    /// <param name="reportPath">失敗の説明に添える報告の path</param>
    /// <returns>成功なら報告の内容、形が合わなければ理由</returns>
    public static Result<RecordedReport, PipelineFailure> Parse(string json, string reportPath)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var projectRoot = root.GetProperty("projectRoot").GetString()
                ?? throw new FormatException("projectRoot が空");
            var mutants = root.GetProperty("files")
                .EnumerateObject()
                .SelectMany(file => file.Value.GetProperty("mutants")
                    .EnumerateArray()
                    .Select(mutant => Recorded(Path.GetFullPath(Path.Combine(projectRoot, file.Name)), mutant)))
                .ToArray();
            return new Result<RecordedReport, PipelineFailure>.Succeeded(new RecordedReport(projectRoot, mutants));
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new Result<RecordedReport, PipelineFailure>.Failed(
                new PipelineFailure.ReportUnreadable(reportPath, exception.Message)
            );
        }
    }

    /// <summary>報告の変異一件の、記録された変異への復元</summary>
    private static RecordedMutant Recorded(string filePath, JsonElement mutant)
    {
        var location = mutant.GetProperty("location");
        var start = location.GetProperty("start");
        var end = location.GetProperty("end");
        return new RecordedMutant(
            mutant.GetProperty("id").GetString() ?? "",
            filePath,
            new SourceSpan(
                start.GetProperty("line").GetInt32(),
                start.GetProperty("column").GetInt32(),
                end.GetProperty("line").GetInt32(),
                end.GetProperty("column").GetInt32()
            ),
            mutant.GetProperty("mutatorName").GetString() ?? "",
            mutant.GetProperty("replacement").GetString() ?? "",
            Verdict(mutant)
        );
    }

    /// <summary>status と最初の検出テストからの確定結果の復元。互換の状態名でなければ失敗</summary>
    private static MutantVerdict Verdict(JsonElement mutant)
    {
        var status = mutant.GetProperty("status").GetString() ?? "";
        var killer = mutant.TryGetProperty("killedBy", out var killedBy) && killedBy.GetArrayLength() > 0
            ? killedBy[0].GetString()
            : null;
        var verdict = VerdictNames.Restore(status, killer);
        return VerdictNames.For(verdict) == status ? verdict : throw new FormatException($"未知の status {status}");
    }
}
