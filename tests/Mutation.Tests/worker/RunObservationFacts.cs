extern alias mutationWorker;

using mutationWorker::Mutation.Worker;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>worker が一変異の run で観測した失敗と完了の記録の検査</summary>
public sealed class RunObservationFacts
{
    /// <summary>最初の失敗が検出テストと probe 上限超過の有無を決め後続の失敗で変わらないこと</summary>
    [Test]
    public async Task First_failure_decides_the_killer_and_the_hit_limit()
    {
        var observation = new RunObservation();

        observation.Fail("id-b", "Calc.Sub", hitLimitExceeded: false);
        observation.Fail("id-c", "Calc.Mul", hitLimitExceeded: true);

        await Assert.That(observation.KillerTest).IsEqualTo("Calc.Sub");
        await Assert.That(observation.HitLimitExceeded).IsFalse();
    }

    /// <summary>失敗した全テストと結果まで観測した全テストが重複を除いて観測順に残ること</summary>
    [Test]
    public async Task Every_failed_and_completed_test_is_kept_once_in_observed_order()
    {
        var observation = new RunObservation();

        observation.Complete("id-a");
        observation.Fail("id-c", "Calc.Mul", hitLimitExceeded: false);
        observation.Complete("id-c");
        observation.Fail("id-b", "Calc.Sub", hitLimitExceeded: false);
        observation.Fail("id-b", "Calc.Sub", hitLimitExceeded: false);
        observation.Complete("id-b");
        observation.Complete("id-b");

        await Assert.That(string.Join(',', observation.FailedTestIds)).IsEqualTo("id-c,id-b");
        await Assert.That(string.Join(',', observation.CompletedTestIds)).IsEqualTo("id-a,id-c,id-b");
    }

    /// <summary>失敗のない run が検出テストを持たないこと</summary>
    [Test]
    public async Task Run_without_failures_has_no_killer()
    {
        var observation = new RunObservation();

        observation.Complete("id-a");

        await Assert.That(observation.KillerTest).IsNull();
        await Assert.That(observation.FailedTestIds).IsEmpty();
    }
}
