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
