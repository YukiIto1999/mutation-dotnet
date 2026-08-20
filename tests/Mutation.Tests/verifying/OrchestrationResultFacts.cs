

using Mutation.Verifying.Domain;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>実行集計の合成の検査</summary>
public sealed class OrchestrationResultFacts
{
    /// <summary>後続の判定が上書きし、記録と回数は合算されること</summary>
    [Test]
    public async Task Overlaid_prefers_later_verdicts_and_sums_the_rest()
    {
        var first = new OrchestrationResult(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.Survived(), [new MutantId(1)] = new MutantVerdict.Killed("a") },
            [new MutantTiming(new MutantId(0), 10, 1), new MutantTiming(new MutantId(1), 20, 1)],
            1
        );
        var later = new OrchestrationResult(
            new Dictionary<MutantId, MutantVerdict> { [new MutantId(0)] = new MutantVerdict.Killed("b") },
            [new MutantTiming(new MutantId(0), 30, 2, Isolated: true)],
            2
        );
        var merged = first.Overlaid(later);
        await Assert.That(merged.Verdicts[new MutantId(0)] is MutantVerdict.Killed { KillerTest: "b" }).IsTrue();
        await Assert.That(merged.Verdicts[new MutantId(1)] is MutantVerdict.Killed { KillerTest: "a" }).IsTrue();
        await Assert.That(merged.Timings.Count).IsEqualTo(3);
        await Assert.That(merged.WorkerRestarts).IsEqualTo(3);
    }
}
