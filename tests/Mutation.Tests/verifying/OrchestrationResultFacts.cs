

using Mutation.Verifying.Domain;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>実行集計の合成の検査</summary>
public sealed class OrchestrationResultFacts
{
    /// <summary>後続の判定と検出行列の行が上書きし記録と回数が合算されること</summary>
    [Test]
    public async Task Overlaid_prefers_later_verdicts_and_rows_and_sums_the_rest()
    {
        var first = new OrchestrationResult(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.Survived(), [new MutantId(1)] = new MutantVerdict.Killed("a") },
            [new MutantTiming(new MutantId(0), 10, 1), new MutantTiming(new MutantId(1), 20, 1)],
            1,
            new Dictionary<MutantId, DetectionRow> { [new MutantId(0)] = Row(failed: []), [new MutantId(1)] = Row(failed: [new TestIndex(0)]) }
        );
        var later = new OrchestrationResult(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.Killed("b") },
            [new MutantTiming(new MutantId(0), 30, 2, Isolated: true)],
            2,
            new Dictionary<MutantId, DetectionRow> { [new MutantId(0)] = Row(failed: [new TestIndex(1)]) }
        );
        var merged = first.Overlaid(later);
        await Assert.That(merged.Verdicts[new MutantId(0)] is MutantVerdict.Killed { KillerTest: "b" }).IsTrue();
        await Assert.That(merged.Verdicts[new MutantId(1)] is MutantVerdict.Killed { KillerTest: "a" }).IsTrue();
        await Assert.That(merged.Detections[new MutantId(0)].Failed.Single()).IsEqualTo(new TestIndex(1));
        await Assert.That(merged.Detections[new MutantId(1)].Failed.Single()).IsEqualTo(new TestIndex(0));
        await Assert.That(merged.Timings.Count).IsEqualTo(3);
        await Assert.That(merged.WorkerRestarts).IsEqualTo(3);
    }

    /// <summary>二件のテストを計画し未実行を残さない検出行列の行</summary>
    /// <param name="failed">失敗したテストの連番</param>
    /// <returns>指定の失敗を持つ行</returns>
    private static DetectionRow Row(TestIndex[] failed) => new([new TestIndex(0), new TestIndex(1)], failed, []);
}
