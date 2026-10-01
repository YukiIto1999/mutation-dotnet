using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>報告と差分の port を制御した、変わった行の判定の use-case の検査</summary>
public sealed class JudgeChangedLinesFacts
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
            new Result<ChangedLines, PipelineFailure>.Succeeded(
                new ChangedLines(
                    new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal)
                    {
                        [Path.GetFullPath("/repo/A.cs")] = [new LineRange(10, 10)],
                    }
                )
            )
        );
        var judge = new JudgeChangedLines(new FakeReports(new Result<RecordedReport, PipelineFailure>.Succeeded(Report)), changes);

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(changes.Requests.ToArray()).IsEquivalentTo([("/repo", "base-ref")]);
        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Succeeded).IsTrue();
        var verdicts = ((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)outcome).Value;
        await Assert.That(verdicts.Undetected.Select(m => m.Id).ToArray()).IsEquivalentTo(["T:1"]);
    }

    /// <summary>報告を読めなければ、差分を解かずにその失敗を返すこと</summary>
    [Test]
    public async Task Unreadable_report_fails_without_consulting_the_diff()
    {
        var unreadable = new PipelineFailure.ReportUnreadable("report.json", "missing");
        var changes = new FakeChangedLines(new Result<ChangedLines, PipelineFailure>.Failed(new PipelineFailure.SinceUnavailable("unused")));
        var judge = new JudgeChangedLines(new FakeReports(new Result<RecordedReport, PipelineFailure>.Failed(unreadable)), changes);

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(var failure) && failure == unreadable).IsTrue();
        await Assert.That(changes.Requests).IsEmpty();
    }

    /// <summary>差分を解けなければ、合格と区別できるようその失敗を返すこと</summary>
    [Test]
    public async Task Unresolvable_diff_fails_the_judgement()
    {
        var unavailable = new PipelineFailure.SinceUnavailable("bad ref");
        var judge = new JudgeChangedLines(
            new FakeReports(new Result<RecordedReport, PipelineFailure>.Succeeded(Report)),
            new FakeChangedLines(new Result<ChangedLines, PipelineFailure>.Failed(unavailable))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(var failure) && failure == unavailable).IsTrue();
    }

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
