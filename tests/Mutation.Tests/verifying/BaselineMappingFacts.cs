

using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Workers;
using Mutation.Shared;
using Mutation.Protocol;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>baseline 観測から被覆表と名簿への写像の検査</summary>
public sealed class BaselineMappingFacts
{
    /// <summary>hit がテスト連番へ帰属し、名簿が観測順に並ぶこと</summary>
    [Test]
    public async Task Hits_are_attributed_to_test_indexes()
    {
        var outcome = BaselineMapping.Map(
            [
                new WorkerTestResult("t-a", "A", 10, true, [0, 1], 5, []),
                new WorkerTestResult("t-b", "B", 20, true, [1], 7, []),
            ],
            []
        );
        await Assert.That(outcome.Roster.Ids.SequenceEqual(["t-a", "t-b"])).IsTrue();
        await Assert.That(outcome.Roster.ProbeCounts.SequenceEqual([5L, 7L])).IsTrue();
        await Assert.That(outcome.Coverage.CoveringTests(new MutantId(0)).SequenceEqual([new TestIndex(0)])).IsTrue();
        await Assert.That(outcome.Coverage.CoveringTests(new MutantId(1)).SequenceEqual([new TestIndex(0), new TestIndex(1)])).IsTrue();
    }

    /// <summary>static hit が引き起こしたテストへ帰属し、ambient にも数えられること</summary>
    [Test]
    public async Task Static_hits_become_triggering_and_ambient()
    {
        var outcome = BaselineMapping.Map(
            [new WorkerTestResult("t-a", "A", 10, true, [], 1, [3])],
            [9]
        );
        await Assert.That(outcome.Coverage.TriggeringTests(new MutantId(3)).SequenceEqual([new TestIndex(0)])).IsTrue();
        await Assert.That(outcome.Coverage.IsAmbient(new MutantId(3))).IsTrue();
        await Assert.That(outcome.Coverage.IsAmbient(new MutantId(9))).IsTrue();
        await Assert.That(outcome.Coverage.IsAmbient(new MutantId(0))).IsFalse();
    }
}
