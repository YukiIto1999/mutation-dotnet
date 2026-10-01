using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Reports;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>互換 schema の報告の読み戻しの検査</summary>
public sealed class RecordedReportFacts
{
    /// <summary>報告の変異が、projectRoot で絶対化した path と範囲と確定結果へ復元されること</summary>
    [Test]
    public async Task Report_mutants_read_back_with_absolute_paths_spans_and_verdicts()
    {
        var json = Report(
            """
            { "id": "T:1", "mutatorName": "BinaryOperatorMutator", "replacement": "a - b",
              "location": { "start": { "line": 5, "column": 9 }, "end": { "line": 6, "column": 14 } },
              "status": "Survived", "killedBy": [], "static": false },
            { "id": "T:2", "mutatorName": "LiteralMutator", "replacement": "1",
              "location": { "start": { "line": 7, "column": 1 }, "end": { "line": 7, "column": 2 } },
              "status": "Killed", "killedBy": ["Suite.Test"], "static": false }
            """
        );

        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(parsed is Result<RecordedReport, PipelineFailure>.Succeeded).IsTrue();
        var report = ((Result<RecordedReport, PipelineFailure>.Succeeded)parsed).Value;
        await Assert.That(report.ProjectRoot).IsEqualTo("/repo");
        await Assert.That(report.Mutants[0].FilePath).IsEqualTo(Path.GetFullPath("/repo/src/A.cs"));
        await Assert.That(report.Mutants[0].Span).IsEqualTo(new SourceSpan(5, 9, 6, 14));
        await Assert.That(report.Mutants[0].Verdict is MutantVerdict.Survived).IsTrue();
        await Assert.That(report.Mutants[1].Verdict is MutantVerdict.Killed { KillerTest: "Suite.Test" }).IsTrue();
    }

    /// <summary>互換の状態名でない status を持つ報告が、その status を名指しした読めない報告として失敗すること</summary>
    [Test]
    public async Task Unknown_status_makes_the_report_unreadable()
    {
        var json = Report(
            """
            { "id": "T:1", "mutatorName": "LiteralMutator", "replacement": "1",
              "location": { "start": { "line": 1, "column": 1 }, "end": { "line": 1, "column": 2 } },
              "status": "Pending", "killedBy": [], "static": false }
            """
        );

        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(ReasonOf(parsed)).Contains("Pending");
    }

    /// <summary>必須の文字列の欄が null の報告が、その欄を名指しした読めない報告として失敗すること</summary>
    [Test]
    public async Task Null_required_field_makes_the_report_unreadable()
    {
        var json = Report(
            """
            { "id": null, "mutatorName": "LiteralMutator", "replacement": "1",
              "location": { "start": { "line": 1, "column": 1 }, "end": { "line": 1, "column": 2 } },
              "status": "Killed", "killedBy": [], "static": false }
            """
        );

        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(ReasonOf(parsed)).Contains("id");
    }

    /// <summary>JSON として壊れた報告と、必須の欄を欠く報告が、読めない報告として失敗すること</summary>
    [Test]
    [Arguments("{")]
    [Arguments("""{ "files": {} }""")]
    public async Task Malformed_reports_are_unreadable(string json)
    {
        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(parsed is Result<RecordedReport, PipelineFailure>.Failed(PipelineFailure.ReportUnreadable))
            .IsTrue();
    }

    /// <summary>読めない報告の失敗の理由。読めた場合や別の失敗なら不在</summary>
    private static string? ReasonOf(Result<RecordedReport, PipelineFailure> parsed) =>
        parsed is Result<RecordedReport, PipelineFailure>.Failed(PipelineFailure.ReportUnreadable { Reason: var reason })
            ? reason
            : null;

    /// <summary>projectRoot が /repo で、src/A.cs に指定の変異を持つ報告</summary>
    private static string Report(string mutants) =>
        $$"""
        { "schemaVersion": "2", "thresholds": { "high": 80, "low": 60 }, "projectRoot": "/repo",
          "files": { "src/A.cs": { "language": "cs", "source": "", "mutants": [ {{mutants}} ] } } }
        """;
}
