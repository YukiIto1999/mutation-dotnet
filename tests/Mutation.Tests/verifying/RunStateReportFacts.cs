using System.Text.Json;
using Mutation.Domain;
using Mutation.Infrastructure.Reports;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定を確定できなかった変異と走行の状態が、二つの報告にどう残るかの検査</summary>
public sealed class RunStateReportFacts
{
    /// <summary>報告内の相対 path の基準</summary>
    private const string ProjectRoot = "/repo";

    /// <summary>取り消された走行が、Pending の変異に理由を添え、走行の状態を二つの報告へ残すこと</summary>
    /// <remarks>検出と、実行に至らなかったものと、途中だったものと、競合で遅れた二通りの未判定の対比</remarks>
    [Test]
    public async Task Interrupted_run_marks_unjudged_mutants_pending_and_records_its_state()
    {
        var verdicts = new MutantVerdict[]
        {
            new MutantVerdict.Killed("Calc.Add"),
            new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()),
            new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()),
            new MutantVerdict.Unjudged(new UnjudgedCause.ContendedTimeout(9_000, 4_000)),
            new MutantVerdict.Unjudged(new UnjudgedCause.ContendedTimeout(null, 4_000)),
        };

        var (report, timings) = await WriteAsync(Result(verdicts));

        var mutants = report.GetProperty("files").GetProperty("src/A.cs").GetProperty("mutants").EnumerateArray().ToArray();
        await Assert.That(mutants.Select(m => m.GetProperty("status").GetString()!).ToArray())
            .IsEquivalentTo(["Killed", "Pending", "Pending", "Pending", "Pending"]);
        await Assert.That(mutants[0].TryGetProperty("statusReason", out _)).IsFalse();
        await Assert.That(mutants[1].GetProperty("statusReason").GetString())
            .IsEqualTo("Unjudged: the run stopped before this mutant was tested");
        await Assert.That(mutants[2].GetProperty("statusReason").GetString())
            .IsEqualTo("Unjudged: the run stopped while this mutant was being tested");
        await Assert.That(mutants[3].GetProperty("statusReason").GetString())
            .IsEqualTo(
                "Unjudged: the tests timed out while the machine was too slow to trust the timeout (an unmutated control run took 9000 ms against a 4000 ms budget)"
            );
        await Assert.That(mutants[4].GetProperty("statusReason").GetString())
            .IsEqualTo(
                "Unjudged: the tests timed out while the machine was too slow to trust the timeout (an unmutated control run did not finish against a 4000 ms budget)"
            );
        await AssertRunAsync(report.GetProperty("run"), "interrupted", unjudged: 4, notRun: 1, interrupted: 1, contended: 2);
        await AssertRunAsync(timings.GetProperty("run"), "interrupted", unjudged: 4, notRun: 1, interrupted: 1, contended: 2);
        await Assert.That(timings.GetProperty("counters").GetProperty("byStatus").GetProperty("Pending").GetInt32()).IsEqualTo(4);
        await Assert.That(timings.GetProperty("counters").GetProperty("score").GetDouble()).IsEqualTo(1.0);
    }

    /// <summary>全件を判定した走行が completed と書かれ、未判定の内訳が零であること</summary>
    [Test]
    public async Task Completed_run_records_a_complete_state_without_pending_mutants()
    {
        var (report, timings) = await WriteAsync(Result([new MutantVerdict.Killed("Calc.Add"), new MutantVerdict.Survived()]));

        await AssertRunAsync(report.GetProperty("run"), "completed", unjudged: 0, notRun: 0, interrupted: 0, contended: 0);
        await AssertRunAsync(timings.GetProperty("run"), "completed", unjudged: 0, notRun: 0, interrupted: 0, contended: 0);
        await Assert.That(report.GetProperty("run").GetProperty("complete").GetBoolean()).IsTrue();
        await Assert.That(timings.GetProperty("counters").GetProperty("byStatus").TryGetProperty("Pending", out _)).IsFalse();
    }

    /// <summary>test runner の失敗が、Pending と理由つきで報告に残り、検出に数えられず、score の母数に入らず、走行を unjudged にすること</summary>
    [Test]
    public async Task Runner_failure_is_reported_pending_with_its_reason_and_stays_out_of_the_score()
    {
        var verdicts = new MutantVerdict[]
        {
            new MutantVerdict.Killed("Calc.Add"),
            new MutantVerdict.Survived(),
            new MutantVerdict.Unjudged(new UnjudgedCause.RunnerFailure("the test host did not open: no host\n   at Host.Open()")),
        };

        var (report, timings) = await WriteAsync(Result(verdicts));

        var mutants = report.GetProperty("files").GetProperty("src/A.cs").GetProperty("mutants").EnumerateArray().ToArray();
        await Assert.That(mutants.Select(m => m.GetProperty("status").GetString()!).ToArray())
            .IsEquivalentTo(["Killed", "Survived", "Pending"]);
        await Assert.That(mutants[2].GetProperty("statusReason").GetString())
            .IsEqualTo(
                "Unjudged: the test runner failed, so no test result exists for this mutant; it is not counted as detected (the test host did not open: no host)"
            );
        await AssertRunAsync(report.GetProperty("run"), "unjudged", unjudged: 1, notRun: 0, interrupted: 0, contended: 0, runnerFailure: 1);
        await AssertRunAsync(timings.GetProperty("run"), "unjudged", unjudged: 1, notRun: 0, interrupted: 0, contended: 0, runnerFailure: 1);
        await Assert.That(report.GetProperty("run").GetProperty("complete").GetBoolean()).IsFalse();
        await Assert.That(timings.GetProperty("counters").GetProperty("score").GetDouble()).IsEqualTo(0.5);
    }

    /// <summary>取り消しで判定に至らなかった対象の名前が run に残ること</summary>
    [Test]
    public async Task Targets_abandoned_by_the_interruption_are_named_in_the_run_record()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Completed(Target([new MutantVerdict.Killed("Calc.Add")])),
                new TargetOutcome.Failed("Later", new PipelineFailure.Interrupted(), 0),
            ],
            new PhaseTimings(0, 0, 0, 0, 0, 0)
        );

        var (report, timings) = await WriteAsync(result);

        await Assert.That(report.GetProperty("run").GetProperty("state").GetString()).IsEqualTo("interrupted");
        await Assert.That(report.GetProperty("run").GetProperty("interruptedTargets").EnumerateArray().Select(e => e.GetString()!).ToArray())
            .IsEquivalentTo(["Later"]);
        await Assert.That(timings.GetProperty("targets").EnumerateArray().Last().GetProperty("reason").GetString())
            .IsEqualTo("Interrupted");
    }

    /// <summary>取り消しではない失敗で判定に至らなかった対象が、run を failed かつ complete でないものにし、名前を failedTargets に残すこと</summary>
    [Test]
    public async Task Targets_that_failed_before_judgement_are_named_and_the_run_is_not_complete()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Completed(Target([new MutantVerdict.Killed("Calc.Add")])),
                new TargetOutcome.Failed("Broken", new PipelineFailure.BuildFailed("error CS0001"), 0),
            ],
            new PhaseTimings(0, 0, 0, 0, 0, 0)
        );

        var (report, timings) = await WriteAsync(result);

        foreach (var run in new[] { report.GetProperty("run"), timings.GetProperty("run") })
        {
            await Assert.That(run.GetProperty("state").GetString()).IsEqualTo("failed");
            await Assert.That(run.GetProperty("complete").GetBoolean()).IsFalse();
            await Assert.That(run.GetProperty("failedTargets").EnumerateArray().Select(e => e.GetString()!).ToArray())
                .IsEquivalentTo(["Broken"]);
            await Assert.That(run.GetProperty("interruptedTargets").GetArrayLength()).IsEqualTo(0);
        }

        await Assert.That(timings.GetProperty("targets").EnumerateArray().Last().GetProperty("reason").GetString())
            .IsEqualTo("BuildFailed");
    }

    /// <summary>run の記録が、状態と未判定の内訳を持つこと</summary>
    private static async Task AssertRunAsync(
        JsonElement run,
        string state,
        int unjudged,
        int notRun,
        int interrupted,
        int contended,
        int runnerFailure = 0
    )
    {
        await Assert.That(run.GetProperty("state").GetString()).IsEqualTo(state);
        await Assert.That(run.GetProperty("unjudged").GetInt32()).IsEqualTo(unjudged);
        var by = run.GetProperty("unjudgedBy");
        await Assert.That(by.GetProperty("notRun").GetInt32()).IsEqualTo(notRun);
        await Assert.That(by.GetProperty("interrupted").GetInt32()).IsEqualTo(interrupted);
        await Assert.That(by.GetProperty("contendedTimeout").GetInt32()).IsEqualTo(contended);
        await Assert.That(by.GetProperty("runnerFailure").GetInt32()).IsEqualTo(runnerFailure);
    }

    /// <summary>判定を連番順に持つ一対象の走行結果</summary>
    private static MutationRunResult Result(MutantVerdict[] verdicts) =>
        new([new TargetOutcome.Completed(Target(verdicts))], new PhaseTimings(0, 0, 0, 0, 0, 0));

    /// <summary>連番順の判定を持つ対象一件の確定結果</summary>
    private static TargetResult Target(MutantVerdict[] verdicts)
    {
        var ids = verdicts.Select((_, index) => new MutantId(index)).ToArray();
        var mutants = ids
            .Select(id => new Mutant(id, "BinaryOperatorMutator", "/repo/src/A.cs", new SourceSpan(1, 1, 1, 5), "a + b", "a - b", false))
            .ToArray();
        return new TargetResult(
            "T",
            mutants,
            ids.Zip(verdicts).ToDictionary(pair => pair.First, pair => pair.Second),
            [],
            TargetTimings.None,
            [],
            0,
            new Dictionary<MutantId, DetectionRow>()
        );
    }

    /// <summary>二つの報告の書き出しと読み戻し</summary>
    private static async Task<(JsonElement Report, JsonElement Timings)> WriteAsync(MutationRunResult result)
    {
        var directory = Directory.CreateTempSubdirectory("mutation-run-state").FullName;
        try
        {
            ReportBundle.Write(result, ProjectRoot, directory);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "mutation-report.json")));
            using var timings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "timings.json")));
            return (report.RootElement.Clone(), timings.RootElement.Clone());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
