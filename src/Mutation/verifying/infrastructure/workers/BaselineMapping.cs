
using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker が返したテスト結果から被覆表と素性への写像</summary>
public static class BaselineMapping
{
    /// <summary>観測結果への連番の付与と、被覆表と static hit の帰属の組み立て</summary>
    /// <param name="observed">全 worker のテスト結果を連ねた列</param>
    /// <param name="ambient">テスト境界の外か static 初期化区間で観測された変異の連番</param>
    /// <returns>被覆とテストの素性</returns>
    public static BaselineProfile Map(IReadOnlyList<WorkerTestResult> observed, IReadOnlyList<int> ambient)
    {
        var tests = new List<TestCaseInfo>();
        var testIds = new List<string>();
        var probeCounts = new List<long>();
        var testsByMutant = new Dictionary<MutantId, List<TestIndex>>();
        var staticTestsByMutant = new Dictionary<MutantId, List<TestIndex>>();
        var ambientSet = ambient.Select(id => new MutantId(id)).ToHashSet();
        foreach (var (result, index) in observed.Select((r, i) => (r, new TestIndex(i))))
        {
            tests.Add(new TestCaseInfo(index, result.Name, result.Ms, result.Passed));
            testIds.Add(result.Id);
            probeCounts.Add(result.ProbeCount);
            Attribute(testsByMutant, result.Hits, index);
            Attribute(staticTestsByMutant, result.StaticHits ?? [], index);
            ambientSet.UnionWith((result.StaticHits ?? []).Select(id => new MutantId(id)));
        }

        var coverage = new CoverageMap(
            testsByMutant.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TestIndex>)pair.Value),
            ambientSet,
            staticTestsByMutant.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<TestIndex>)pair.Value)
        );
        return new BaselineProfile(tests, new TestRoster(testIds, probeCounts), coverage);
    }

    /// <summary>観測された変異の、そのテスト連番への帰属</summary>
    private static void Attribute(Dictionary<MutantId, List<TestIndex>> byMutant, IReadOnlyList<int> mutantIds, TestIndex testIndex)
    {
        foreach (var rawId in mutantIds)
        {
            var mutantId = new MutantId(rawId);
            if (!byMutant.TryGetValue(mutantId, out var list))
            {
                list = [];
                byMutant[mutantId] = list;
            }

            list.Add(testIndex);
        }
    }
}
