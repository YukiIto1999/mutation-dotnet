using Mutation.Domain;
using Mutation.Application;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>報告と差分の port を制御した、変わった行の判定の use-case の検査</summary>
public sealed partial class JudgeChangedLinesFacts
{
    /// <summary>報告に記録された、10 行目で生存と 20 行目で生存の二件</summary>
    private static readonly RecordedReport Report = new(
        "/repo",
        [
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Survived()),
            new RecordedMutant("T:2", Path.GetFullPath("/repo/A.cs"), new SourceSpan(20, 1, 20, 2), "M", "r", new MutantVerdict.Survived()),
        ]
    );

    /// <summary>報告の root で基点からの差分を解き、その差分に重なる変異だけを返すこと</summary>
    [Test]
    public async Task Diff_is_resolved_at_the_report_root_and_selects_overlapping_mutants()
    {
        var changes = new FakeChangedLines(
            Result.Succeeded(
                new ChangedLines(
                    new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal)
                    {
                        [Path.GetFullPath("/repo/A.cs")] = [new LineRange(10, 10)],
                    }
                )
            )
        );
        var judge = new JudgeChangedLines(new FakeReports(Result.Succeeded(Report)), changes);

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(changes.Requests.ToArray()).IsEquivalentTo([("/repo", "base-ref")]);
        await Assert.That(outcome.IsFailed(out _, out var verdicts)).IsFalse();
        await Assert.That(verdicts!.Undetected.Select(m => m.Id).ToArray()).IsEquivalentTo(["T:1"]);
    }

    /// <summary>報告を読めなければ、差分を解かずにその失敗を返すこと</summary>
    [Test]
    public async Task Unreadable_report_fails_without_consulting_the_diff()
    {
        var unreadable = new PipelineFailure.ReportUnreadable("report.json", "missing");
        var changes = new FakeChangedLines(Result.Failed<PipelineFailure>(new PipelineFailure.SinceUnavailable("unused")));
        var judge = new JudgeChangedLines(new FakeReports(Result.Failed<PipelineFailure>(unreadable)), changes);

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure == unreadable).IsTrue();
        await Assert.That(changes.Requests).IsEmpty();
    }

    /// <summary>差分を解けなければ、合格と区別できるようその失敗を返すこと</summary>
    [Test]
    public async Task Unresolvable_diff_fails_the_judgement()
    {
        var unavailable = new PipelineFailure.SinceUnavailable("bad ref");
        var judge = new JudgeChangedLines(
            new FakeReports(Result.Succeeded(Report)),
            new FakeChangedLines(Result.Failed<PipelineFailure>(unavailable))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure == unavailable).IsTrue();
    }

    /// <summary>変わった行に未判定の変異があり未検出がなければ、合格と言えず判定できない失敗になること</summary>
    [Test]
    public async Task Unjudged_mutant_on_a_changed_line_makes_the_gate_indeterminate()
    {
        var judge = JudgeWith(
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.UnjudgedOnChangedLines { Count: 1 })
            .IsTrue();
    }

    /// <summary>未判定だけが変わった行にあり合否を決められないときも、報告が未分析にした影響の関係が失敗に残ること</summary>
    [Test]
    public async Task Indeterminate_gate_keeps_the_unanalysed_scope_of_the_report()
    {
        var judge = JudgeWithUnanalysed(
            ["Target: test-changed 1 件"],
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.UnjudgedOnChangedLines { Count: 1, Unanalysed: ["Target: test-changed 1 件"] })
            .IsTrue();
    }

    /// <summary>未検出が変わった行にあれば、未判定が別にあっても未検出で合否が決まること</summary>
    [Test]
    public async Task Undetected_mutant_decides_the_gate_even_when_others_are_unjudged()
    {
        var judge = JudgeWith(
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Survived()),
            new RecordedMutant("T:2", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out _, out var verdicts)).IsFalse();
        await Assert.That(verdicts!.Undetected.Select(m => m.Id).ToArray()).IsEquivalentTo(["T:1"]);
        await Assert.That(verdicts.Unjudged.Select(m => m.Id).ToArray()).IsEquivalentTo(["T:2"]);
    }

    /// <summary>変わった行の変異が全て検出され未判定もなければ、失敗でなく確定結果を返すこと</summary>
    [Test]
    public async Task Detected_mutants_on_the_changed_lines_let_the_gate_pass()
    {
        var judge = JudgeWith(
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Killed("Calc.Add"))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out _, out var verdicts)).IsFalse();
        await Assert.That(verdicts!.OnChangedLines.Select(m => m.Id).ToArray()).IsEquivalentTo(["T:1"]);
    }

    /// <summary>報告が未分析として残した影響の関係が、合格の判定にも添えられ、判定が影響する範囲を含まないことを示せること</summary>
    [Test]
    public async Task Unanalysed_scope_recorded_by_the_report_accompanies_the_verdicts()
    {
        var judge = new JudgeChangedLines(
            new FakeReports(
                Result.Succeeded(
                    new RecordedReport(
                        "/repo",
                        [new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Killed("Calc.Add"))],
                        ["Target: test-changed 1 件"]
                    )
                )
            ),
            new FakeChangedLines(
                Result.Succeeded(
                    new ChangedLines(
                        new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal)
                        {
                            [Path.GetFullPath("/repo/A.cs")] = [new LineRange(10, 10)],
                        }
                    )
                )
            )
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out _, out var verdicts)).IsFalse();
        await Assert.That(verdicts!.Unanalysed.ToArray()).IsEquivalentTo(["Target: test-changed 1 件"]);
    }

    /// <summary>10 行目だけが変わった差分と、指定の変異を持つ報告からの判定の組み立て</summary>
    private static JudgeChangedLines JudgeWith(params RecordedMutant[] mutants) => JudgeReport(new RecordedReport("/repo", mutants));

    /// <summary>10 行目だけが変わった差分と、指定の変異と未分析の影響の関係を持つ報告からの判定の組み立て</summary>
    private static JudgeChangedLines JudgeWithUnanalysed(IReadOnlyList<string>? unanalysed, params RecordedMutant[] mutants) =>
        JudgeReport(new RecordedReport("/repo", mutants, unanalysed));

    /// <summary>10 行目だけが変わった差分と、指定の変異と走行の記録を持つ報告からの判定の組み立て</summary>
    private static JudgeChangedLines JudgeWithRun(RecordedRun? run, params RecordedMutant[] mutants) =>
        JudgeReport(new RecordedReport("/repo", mutants, null, run));

    /// <summary>10 行目だけが変わった差分と、指定の報告からの判定の組み立て</summary>
    private static JudgeChangedLines JudgeReport(RecordedReport report) =>
        new(
            new FakeReports(Result.Succeeded(report)),
            new FakeChangedLines(
                Result.Succeeded(
                    new ChangedLines(
                        new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal)
                        {
                            [Path.GetFullPath("/repo/A.cs")] = [new LineRange(10, 10)],
                        }
                    )
                )
            )
        );

    /// <summary>決めた読み込み結果を返す報告の port</summary>
    private sealed class FakeReports(Result<RecordedReport, PipelineFailure> result) : IRecordedReports
    {
        /// <inheritdoc />
        public Result<RecordedReport, PipelineFailure> Read(string reportPath) => result;
    }

    /// <summary>決めた解決結果を返し、受けた要求を記録する差分の port</summary>
    private sealed class FakeChangedLines(Result<ChangedLines, PipelineFailure> result) : IChangedLines
    {
        /// <summary>受けた directory と基点の列</summary>
        public List<(string Directory, string SinceRef)> Requests { get; } = [];

        /// <inheritdoc />
        public Result<ChangedLines, PipelineFailure> Resolve(string directory, string sinceRef)
        {
            Requests.Add((directory, sinceRef));
            return result;
        }
    }
}
