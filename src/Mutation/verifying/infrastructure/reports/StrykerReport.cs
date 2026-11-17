using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TypeModeling.Domain;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>Stryker 互換 mutation-report.json の書き出しと読み戻し</summary>
public static class StrykerReport
{
    /// <summary>検出行列の欄を行のある変異だけに書く互換 schema の camelCase の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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
            run = RunStateReport.Describe(result),
            files,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, Serializer));
    }

    /// <summary>変異一件の互換 schema での記述。連番は対象名で修飾して一意にする</summary>
    /// <remarks>検出行列の行を持つ変異での killedBy と coveredBy と testsCompleted と statusReason への行の書き出し</remarks>
    private static object Describe(TargetResult target, Mutant mutant)
    {
        var verdict = target.Verdicts[mutant.Id];
        var killer = verdict is MutantVerdict.Killed killed ? new[] { killed.KillerTest } : Array.Empty<string>();
        var row = target.Detections.GetValueOrDefault(mutant.Id);
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
            killedBy = row is { Failed.Count: > 0 } ? Names(target, row.Failed) : killer,
            coveredBy = row is null ? null : Names(target, row.Planned),
            testsCompleted = row is null ? (int?)null : row.Planned.Count - row.NotRun.Count,
            statusReason = StatusReason(verdict, target, row),
            @static = mutant.InStaticContext,
        };
    }

    /// <summary>判定を確定できなかった理由、なければ検出行列の未実行のテストを名指しする、変異の statusReason</summary>
    private static string? StatusReason(MutantVerdict verdict, TargetResult target, DetectionRow? row) =>
        verdict switch
        {
            MutantVerdict.Unjudged unjudged => RunStateReport.Reason(unjudged.Cause),
            _ when row is { IsComplete: false } =>
                $"Detection matrix incomplete; not run: {string.Join(", ", Names(target, row.NotRun))}",
            _ => null,
        };

    /// <summary>対象のテストの素性の並びと一致するテスト連番からの表示名の列</summary>
    private static string[] Names(TargetResult target, IReadOnlyList<TestIndex> indexes) =>
        indexes.Select(index => target.Tests[index.Value].DisplayName).ToArray();

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
            var projectRoot = RequiredString(root, "projectRoot");
            var mutants = root.GetProperty("files")
                .EnumerateObject()
                .SelectMany(file => file.Value.GetProperty("mutants")
                    .EnumerateArray()
                    .Select(mutant => Recorded(Path.GetFullPath(Path.Combine(projectRoot, file.Name)), mutant)))
                .ToArray();
            return Result.Succeeded(new RecordedReport(projectRoot, mutants, Unanalysed(root)));
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result.Failed<PipelineFailure>(new PipelineFailure.ReportUnreadable(reportPath, exception.Message));
        }
    }

    /// <summary>報告の変異一件の、記録された変異への復元</summary>
    private static RecordedMutant Recorded(string filePath, JsonElement mutant)
    {
        var location = mutant.GetProperty("location");
        var start = location.GetProperty("start");
        var end = location.GetProperty("end");
        return new RecordedMutant(
            RequiredString(mutant, "id"),
            filePath,
            new SourceSpan(
                start.GetProperty("line").GetInt32(),
                start.GetProperty("column").GetInt32(),
                end.GetProperty("line").GetInt32(),
                end.GetProperty("column").GetInt32()
            ),
            RequiredString(mutant, "mutatorName"),
            RequiredString(mutant, "replacement"),
            Verdict(mutant)
        );
    }

    /// <summary>status と最初の検出テストからの確定結果の復元。互換の状態名でなければ失敗</summary>
    private static MutantVerdict Verdict(JsonElement mutant)
    {
        var status = RequiredString(mutant, "status");
        var killer = mutant.TryGetProperty("killedBy", out var killedBy) && killedBy.GetArrayLength() > 0
            ? killedBy[0].GetString()
            : null;
        var verdict = VerdictNames.Restore(status, killer);
        return VerdictNames.For(verdict) == status ? verdict : throw new FormatException($"未知の status {status}");
    }

    /// <summary>報告の run が残した、`--since` の選別で未分析にした関係の記述。差分運用でない報告や前の版の報告では空</summary>
    private static string[] Unanalysed(JsonElement root) =>
        root.TryGetProperty("run", out var run)
        && run.TryGetProperty("selection", out var selection)
        && selection.ValueKind == JsonValueKind.Array
            ? [.. selection.EnumerateArray().SelectMany(UnanalysedOf)]
            : [];

    /// <summary>対象一件の選別が残した未分析の関係と件数の列</summary>
    private static IEnumerable<string> UnanalysedOf(JsonElement target) =>
        target
            .GetProperty("unanalysed")
            .EnumerateArray()
            .Select(reason =>
                $"{RequiredString(target, "target")}: {RequiredString(reason, "relation")} {reason.GetProperty("paths").GetArrayLength().ToString(CultureInfo.InvariantCulture)} 件"
            );

    /// <summary>必須の文字列の欄の値。null なら読めない報告として失敗させる</summary>
    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new FormatException($"{name} が null");
}
