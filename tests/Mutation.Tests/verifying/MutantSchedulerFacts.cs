using Mutation.Verifying.Domain;
using Mutation.Shared;
using CsCheck;

using TUnit.Core;

namespace Mutation.Tests;

/// <summary>変異実行計画の純粋計算の検査</summary>
public sealed class MutantSchedulerFacts
{
    /// <summary>被覆テストの所要時間昇順での並び</summary>
    [Test]
    public async Task Covered_tests_are_ordered_by_baseline_duration()
    {
        var mutants = new[] { Mutant(0, inStatic: false) };
        var tests = new[]
        {
            new TestCaseInfo(new TestIndex(0), "slow", 300, BaselinePassed: true),
            new TestCaseInfo(new TestIndex(1), "fast", 10, BaselinePassed: true),
            new TestCaseInfo(new TestIndex(2), "mid", 100, BaselinePassed: true),
        };
        var coverage = Coverage(new Dictionary<int, IReadOnlyList<int>> { [0] = [0, 1, 2] });
        var outcome = MutantScheduler.Plan(mutants, coverage, tests, excludeStatic: false);
        var execution = (MutantExecution.Resident)outcome.Plans[0].Execution;
        await Assert.That(execution.TestOrder.SequenceEqual(Order(1, 2, 0))).IsTrue();
    }

    /// <summary>baseline で失敗したテストの選択からの除外</summary>
    [Test]
    public async Task Failing_baseline_tests_are_excluded()
    {
        var mutants = new[] { Mutant(0, inStatic: false) };
        var tests = new[]
        {
            new TestCaseInfo(new TestIndex(0), "broken", 10, BaselinePassed: false),
            new TestCaseInfo(new TestIndex(1), "green", 20, BaselinePassed: true),
        };
        var coverage = Coverage(new Dictionary<int, IReadOnlyList<int>> { [0] = [0, 1] });
        var outcome = MutantScheduler.Plan(mutants, coverage, tests, excludeStatic: false);
        var execution = (MutantExecution.Resident)outcome.Plans[0].Execution;
        await Assert.That(execution.TestOrder.SequenceEqual(Order(1))).IsTrue();
    }

    /// <summary>static 文脈の変異への全 passing テストと分離の割り当て</summary>
    [Test]
    public async Task Static_mutants_get_isolation_and_all_passing_tests()
    {
        var mutants = new[] { Mutant(0, inStatic: true) };
        var tests = new[]
        {
            new TestCaseInfo(new TestIndex(0), "a", 30, BaselinePassed: true),
            new TestCaseInfo(new TestIndex(1), "b", 10, BaselinePassed: true),
        };
        var outcome = MutantScheduler.Plan(mutants, Coverage(new Dictionary<int, IReadOnlyList<int>>()), tests, excludeStatic: false);
        var execution = (MutantExecution.Isolated)outcome.Plans[0].Execution;
        await Assert.That(execution.TestOrder.SequenceEqual(Order(1, 0))).IsTrue();
    }

    /// <summary>static 初期化中に hit し通常の被覆も持つ変異は、共有ホストで先に試し生存時に隔離する計画になること</summary>
    [Test]
    public async Task Static_hit_mutants_with_regular_coverage_are_isolated_only_on_survival()
    {
        var mutants = new[] { Mutant(0, inStatic: false) };
        var tests = new[]
        {
            new TestCaseInfo(new TestIndex(0), "a", 30, BaselinePassed: true),
            new TestCaseInfo(new TestIndex(1), "b", 10, BaselinePassed: true),
        };
        var coverage = new CoverageMap(
            new Dictionary<MutantId, IReadOnlyList<TestIndex>> { [new MutantId(0)] = [new TestIndex(0)] },
            new HashSet<MutantId> { new MutantId(0) },
            null
        );
        var plan = MutantScheduler.Plan(mutants, coverage, tests, excludeStatic: false).Plans[0];
        var execution = (MutantExecution.ResidentThenIsolated)plan.Execution;
        await Assert.That(execution.ResidentOrder.SequenceEqual(Order(0))).IsTrue();
        await Assert.That(execution.IsolatedOrder.SequenceEqual(Order(0, 1))).IsTrue();
    }

    /// <summary>static 除外の指定で隔離が要る変異だけが対象外になること</summary>
    [Test]
    public async Task Exclude_static_drops_only_isolation_requiring_mutants()
    {
        var mutants = new[] { Mutant(0, inStatic: true), Mutant(1, inStatic: false) };
        var tests = new[] { new TestCaseInfo(new TestIndex(0), "a", 10, BaselinePassed: true) };
        var coverage = Coverage(new Dictionary<int, IReadOnlyList<int>> { [1] = [0] });
        var outcome = MutantScheduler.Plan(mutants, coverage, tests, excludeStatic: true);
        await Assert.That(outcome.ExcludedStaticMutants.SequenceEqual([new MutantId(0)])).IsTrue();
        await Assert.That(outcome.Plans.Select(p => p.MutantId).SequenceEqual([new MutantId(1)])).IsTrue();
    }

    /// <summary>全変異の計画か被覆なしのどちらかへの必ずの分類</summary>
    [Test]
    public void Every_mutant_lands_in_exactly_one_bucket()
    {
        var generator = Gen.Select(
            Gen.Int[0, 20].List[0, 20],
            Gen.Int[1, 8],
            (covered, testCount) => (covered, testCount)
        );
        generator.Sample(input =>
        {
            var mutants = Enumerable.Range(0, 20).Select(id => Mutant(id, inStatic: false)).ToArray();
            var tests = Enumerable
                .Range(0, input.testCount)
                .Select(i => new TestCaseInfo(new TestIndex(i), $"t{i}", i, BaselinePassed: true))
                .ToArray();
            var map = input
                .covered.Select((mutantId, index) => (mutantId, test: index % input.testCount))
                .GroupBy(pair => pair.mutantId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<int>)group.Select(p => p.test).Distinct().ToArray()
                );
            var outcome = MutantScheduler.Plan(mutants, Coverage(map), tests, excludeStatic: false);
            var planned = outcome.Plans.Select(p => p.MutantId);
            var all = planned.Concat(outcome.NoCoverageMutants).Select(id => id.Value).Order().ToArray();
            return all.SequenceEqual(Enumerable.Range(0, 20));
        });
    }

    private static Mutant Mutant(int id, bool inStatic) =>
        new(new MutantId(id), "op", "file.cs", new SourceSpan(1, 1, 1, 2), "a + b", "a - b", inStatic);

    private static TestIndex[] Order(params int[] indexes) => indexes.Select(i => new TestIndex(i)).ToArray();

    private static CoverageMap Coverage(IReadOnlyDictionary<int, IReadOnlyList<int>> map) =>
        new(
            map.ToDictionary(
                pair => new MutantId(pair.Key),
                pair => (IReadOnlyList<TestIndex>)pair.Value.Select(i => new TestIndex(i)).ToArray()
            ),
            new HashSet<MutantId>(),
            null
        );
}
