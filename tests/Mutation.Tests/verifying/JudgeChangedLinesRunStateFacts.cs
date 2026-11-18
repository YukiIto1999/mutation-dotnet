using Mutation.Domain;
using Mutation.Application;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

public sealed partial class JudgeChangedLinesFacts
{
    /// <summary>失敗した走行の報告が、変異を一件も持たなくても、変わった行の合格にならず、報告と失敗した対象を名指す失敗になること</summary>
    [Test]
    public async Task Report_of_a_failed_run_cannot_pass_the_gate()
    {
        var judge = JudgeWithRun(new RecordedRun.Failed(["T"]));

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.ReportOfFailedRun { ReportPath: "report.json", Targets: ["T"] })
            .IsTrue();
    }

    /// <summary>取り消しで止まった走行の報告が、変わった行に判定済みの変異がなければ、合格にならず、報告と中断した対象を名指す失敗になること</summary>
    [Test]
    public async Task Report_of_an_interrupted_run_cannot_pass_the_gate()
    {
        var judge = JudgeWithRun(
            new RecordedRun.Interrupted(["Later"]),
            new RecordedMutant("T:2", Path.GetFullPath("/repo/A.cs"), new SourceSpan(20, 1, 20, 2), "M", "r", new MutantVerdict.Killed("Calc.Add"))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.ReportOfInterruptedRun { ReportPath: "report.json", Targets: ["Later"] })
            .IsTrue();
    }

    /// <summary>止まった走行の報告でも、変わった行に未検出があれば、未検出で合否が決まること</summary>
    [Test]
    public async Task Undetected_mutant_decides_the_gate_of_a_failed_run_report()
    {
        var judge = JudgeWithRun(
            new RecordedRun.Failed(["T"]),
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Survived())
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(!outcome.IsFailed(out _, out var verdicts) && verdicts.Undetected.Select(m => m.Id).SequenceEqual(["T:1"]))
            .IsTrue();
    }

    /// <summary>取り消しで止まった走行の報告でも、変わった行に未判定の変異があれば、従来どおり判定できない失敗になること</summary>
    [Test]
    public async Task Pending_mutant_keeps_its_own_failure_in_an_interrupted_run_report()
    {
        var judge = JudgeWithRun(
            new RecordedRun.Interrupted([]),
            new RecordedMutant("T:1", Path.GetFullPath("/repo/A.cs"), new SourceSpan(10, 1, 10, 2), "M", "r", new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()))
        );

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.UnjudgedOnChangedLines { Count: 1 })
            .IsTrue();
    }

    /// <summary>全対象の判定に至った走行の報告が、変わった行に変異を持たなくても、変異のない正当な結果として合格になること</summary>
    [Test]
    public async Task Report_of_a_finished_run_without_mutants_on_the_changed_lines_passes_the_gate()
    {
        var judge = JudgeWithRun(new RecordedRun.Finished());

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(!outcome.IsFailed(out _, out var verdicts) && verdicts.OnChangedLines.Count == 0).IsTrue();
    }

    /// <summary>この道具が走行の状態を書かなかった報告が、止まった走行と取り違えられず、変異がなければ従来どおり合格になること</summary>
    [Test]
    public async Task Report_without_a_run_record_and_without_mutants_still_passes_the_gate()
    {
        var judge = JudgeWithRun(null);

        var outcome = judge.Execute("report.json", "base-ref");

        await Assert.That(!outcome.IsFailed(out _, out var verdicts) && verdicts.OnChangedLines.Count == 0).IsTrue();
    }

}
