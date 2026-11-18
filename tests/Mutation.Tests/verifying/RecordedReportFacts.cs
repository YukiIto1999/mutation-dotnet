using Mutation.Domain;
using Mutation.Application;
using Mutation.Infrastructure.Reports;
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

        await Assert.That(parsed.IsFailed(out _, out var report)).IsFalse();
        await Assert.That(report!.ProjectRoot).IsEqualTo("/repo");
        await Assert.That(report.Mutants[0].FilePath).IsEqualTo(Path.GetFullPath("/repo/src/A.cs"));
        await Assert.That(report.Mutants[0].Span).IsEqualTo(new SourceSpan(5, 9, 6, 14));
        await Assert.That(report.Mutants[0].Verdict is MutantVerdict.Survived).IsTrue();
        await Assert.That(report.Mutants[1].Verdict is MutantVerdict.Killed { KillerTest: "Suite.Test" }).IsTrue();
    }

    /// <summary>この道具が書かない状態名を持つ報告が、その status を名指しした読めない報告として失敗すること</summary>
    [Test]
    public async Task Unknown_status_makes_the_report_unreadable()
    {
        var json = Report(
            """
            { "id": "T:1", "mutatorName": "LiteralMutator", "replacement": "1",
              "location": { "start": { "line": 1, "column": 1 }, "end": { "line": 1, "column": 2 } },
              "status": "RuntimeError", "killedBy": [], "static": false }
            """
        );

        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(ReasonOf(parsed)).Contains("RuntimeError");
    }

    /// <summary>判定を確定できなかった変異の Pending が、読めない報告にならず未判定として読み戻されること</summary>
    [Test]
    public async Task Pending_status_reads_back_as_an_unjudged_mutant()
    {
        var json = Report(
            """
            { "id": "T:1", "mutatorName": "LiteralMutator", "replacement": "1",
              "location": { "start": { "line": 1, "column": 1 }, "end": { "line": 1, "column": 2 } },
              "status": "Pending", "killedBy": [], "statusReason": "Unjudged: the run stopped before this mutant was tested", "static": false }
            """
        );

        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(parsed.IsFailed(out _, out var report)).IsFalse();
        await Assert.That(report!.Mutants.Single().Verdict is MutantVerdict.Unjudged).IsTrue();
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

        await Assert.That(parsed.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.ReportUnreadable)
            .IsTrue();
    }

    /// <summary>取り消しではない失敗で止まった走行の報告が、失敗で判定に至らなかった対象を名指す失敗の記録として読み戻されること</summary>
    [Test]
    public async Task Failed_run_state_reads_back_with_its_failed_targets()
    {
        var parsed = StrykerReport.Parse(ReportWithRun("""{ "state": "failed", "complete": false, "failedTargets": ["Broken"], "interruptedTargets": [] }"""), "report.json");

        await Assert.That(!parsed.IsFailed(out _, out var report) && report.Run is RecordedRun.Failed { Targets: ["Broken"] }).IsTrue();
    }

    /// <summary>取り消しで止まった走行の報告が、取り消しで判定に至らなかった対象を名指す中断の記録として読み戻されること</summary>
    [Test]
    public async Task Interrupted_run_state_reads_back_with_its_interrupted_targets()
    {
        var parsed = StrykerReport.Parse(ReportWithRun("""{ "state": "interrupted", "complete": false, "failedTargets": [], "interruptedTargets": ["Later"] }"""), "report.json");

        await Assert.That(!parsed.IsFailed(out _, out var report) && report.Run is RecordedRun.Interrupted { Targets: ["Later"] }).IsTrue();
    }

    /// <summary>全対象の判定に至った走行の報告が、未判定の変異の有無によらず終えた走行の記録として読み戻されること</summary>
    [Test]
    [Arguments("completed")]
    [Arguments("unjudged")]
    public async Task Finished_run_states_read_back_as_a_finished_run(string state)
    {
        var parsed = StrykerReport.Parse(ReportWithRun($$"""{ "state": "{{state}}", "complete": false }"""), "report.json");

        await Assert.That(!parsed.IsFailed(out _, out var report) && report.Run is RecordedRun.Finished).IsTrue();
    }

    /// <summary>この道具が走行の状態を書かなかった報告が、終えた走行とは別に、走行の記録を持たない報告として読み戻されること</summary>
    [Test]
    [Arguments("""{ "files": {}, "projectRoot": "/repo" }""")]
    [Arguments("""{ "files": {}, "projectRoot": "/repo", "run": { "selection": [] } }""")]
    public async Task Report_without_a_run_state_reads_back_without_a_run_record(string json)
    {
        var parsed = StrykerReport.Parse(json, "report.json");

        await Assert.That(!parsed.IsFailed(out _, out var report) && report.Run is null).IsTrue();
    }

    /// <summary>この道具が書かない走行の状態を持つ報告が、終えた走行と取り違えられず、その状態を名指した読めない報告として失敗すること</summary>
    [Test]
    public async Task Unknown_run_state_makes_the_report_unreadable()
    {
        var parsed = StrykerReport.Parse(ReportWithRun("""{ "state": "paused" }"""), "report.json");

        await Assert.That(ReasonOf(parsed)).Contains("paused");
    }

    /// <summary>読めない報告の失敗の理由。読めた場合や別の失敗なら不在</summary>
    private static string? ReasonOf(Result<RecordedReport, PipelineFailure> parsed) =>
        parsed.IsFailed(out var failed, out _)
        && failed.Failure is PipelineFailure.ReportUnreadable { Reason: var reason }
            ? reason
            : null;

    /// <summary>projectRoot が /repo で、src/A.cs に指定の変異を持つ報告</summary>
    private static string Report(string mutants) =>
        $$"""
        { "schemaVersion": "2", "thresholds": { "high": 80, "low": 60 }, "projectRoot": "/repo",
          "files": { "src/A.cs": { "language": "cs", "source": "", "mutants": [ {{mutants}} ] } } }
        """;

    /// <summary>projectRoot が /repo で、変異を持たず、run の欄が指定の内容の報告</summary>
    private static string ReportWithRun(string run) =>
        $$"""
        { "schemaVersion": "2", "projectRoot": "/repo", "run": {{run}}, "files": {} }
        """;
}
