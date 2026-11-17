using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定を確定できなかった変異の、数え方と走行の状態の検査</summary>
public sealed class UnjudgedVerdictFacts
{
    /// <summary>三つの理由の未判定。連番 0 から 2</summary>
    private static readonly MutantVerdict[] UnjudgedOfEveryCause =
    [
        new MutantVerdict.Unjudged(new UnjudgedCause.NotRun()),
        new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()),
        new MutantVerdict.Unjudged(new UnjudgedCause.ContendedTimeout(9_000, 4_000)),
    ];

    /// <summary>未判定が Pending の名前を持ち、検出にも未検出にも数えられないこと</summary>
    [Test]
    public async Task Unjudged_is_pending_and_neither_detected_nor_undetected()
    {
        var target = Target(
            new MutantVerdict.Killed("t"),
            new MutantVerdict.Survived(),
            new MutantVerdict.Unjudged(new UnjudgedCause.NotRun())
        );

        await Assert.That(VerdictNames.For(UnjudgedOfEveryCause[0])).IsEqualTo("Pending");
        await Assert.That(target.DetectedCount).IsEqualTo(1);
        await Assert.That(target.UndetectedCount).IsEqualTo(1);
        await Assert.That(target.UnjudgedCount).IsEqualTo(1);
        await Assert.That(new MutationRunResult([new TargetOutcome.Completed(target)], Timings).Score).IsEqualTo(0.5);
    }

    /// <summary>実行に至らなかった未判定と実行の途中の未判定が走行を途中にし、競合の未判定は完了した走行に残ること</summary>
    [Test]
    [Arguments(0, "interrupted")]
    [Arguments(1, "interrupted")]
    [Arguments(2, "unjudged")]
    public async Task Run_state_follows_the_cause_of_the_unjudged_mutants(int cause, string state)
    {
        var result = new MutationRunResult(
            [new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t"), UnjudgedOfEveryCause[cause]))],
            Timings
        );

        await Assert.That(result.State).IsEqualTo(state);
        await Assert.That(result.IsComplete).IsFalse();
    }

    /// <summary>未判定がなく取り消された対象もない走行が完了と言えること</summary>
    [Test]
    public async Task Run_without_unjudged_mutants_is_completed()
    {
        var result = new MutationRunResult([new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t")))], Timings);

        await Assert.That(result.State).IsEqualTo("completed");
        await Assert.That(result.IsComplete).IsTrue();
    }

    /// <summary>取り消しで判定に至らなかった対象が走行を途中にすること</summary>
    [Test]
    public async Task Target_abandoned_by_the_interruption_makes_the_run_interrupted()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t"))),
                new TargetOutcome.Failed("B", new PipelineFailure.Interrupted(), 0),
            ],
            Timings
        );

        await Assert.That(result.State).IsEqualTo("interrupted");
    }

    /// <summary>複数の対象のうち一つだけが実行に至らなかった変異を持っても 走行を途中にすること</summary>
    [Test]
    public async Task One_target_with_mutants_that_never_ran_makes_a_multi_target_run_interrupted()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t"))),
                new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t"), UnjudgedOfEveryCause[0])),
            ],
            Timings
        );

        await Assert.That(result.State).IsEqualTo("interrupted");
    }

    /// <summary>取り消しではない失敗で判定に至らなかった対象があれば、他の対象が全件確定していても、走行を完了と言わず failed にすること</summary>
    [Test]
    public async Task A_target_that_failed_before_judgement_makes_the_run_failed_not_completed()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Completed(Target(new MutantVerdict.Killed("t"))),
                new TargetOutcome.Failed("B", new PipelineFailure.BuildFailed("error CS0001"), 0),
            ],
            Timings
        );

        await Assert.That(result.State).IsEqualTo("failed");
        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailedTargets.Select(failure => failure.Name).ToArray()).IsEquivalentTo(["B"]);
    }

    /// <summary>取り消しと失敗の対象が並ぶ走行は、取り消しを優先して interrupted にし、失敗の対象を失わないこと</summary>
    [Test]
    public async Task Interruption_takes_precedence_over_a_failed_target_in_the_state()
    {
        var result = new MutationRunResult(
            [
                new TargetOutcome.Failed("A", new PipelineFailure.BuildFailed("error CS0001"), 0),
                new TargetOutcome.Failed("B", new PipelineFailure.Interrupted(), 0),
            ],
            Timings
        );

        await Assert.That(result.State).IsEqualTo("interrupted");
        await Assert.That(result.FailedTargets.Select(failure => failure.Name).ToArray()).IsEquivalentTo(["A"]);
    }

    /// <summary>連番順の判定を持つ対象一件の確定結果</summary>
    private static TargetResult Target(params MutantVerdict[] verdicts) =>
        new(
            "T",
            [],
            verdicts.Select((verdict, index) => (verdict, id: new MutantId(index))).ToDictionary(pair => pair.id, pair => pair.verdict),
            [],
            TargetTimings.None,
            [],
            0,
            new Dictionary<MutantId, DetectionRow>()
        );

    /// <summary>所要時間を持たない段階別時間</summary>
    private static PhaseTimings Timings => new(0, 0, 0, 0, 0, 0);
}
