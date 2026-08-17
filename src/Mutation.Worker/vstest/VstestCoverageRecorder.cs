using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Mutation.Protocol;

namespace Mutation.Worker;

/// <summary>baseline 実行でテストごとの結果と被覆を集める VSTest の受け口</summary>
public sealed class VstestCoverageRecorder(SwitchAccessor accessor) : VstestRecorder
{
    /// <summary>test case の一意識別子から観測の累積への対応</summary>
    private readonly Dictionary<string, Accumulated> byTestCase = [];
    /// <summary>観測した順の一意識別子</summary>
    private readonly List<string> order = [];
    /// <summary>テスト境界の外で観測した変異の連番</summary>
    private readonly List<int> ambient = [];

    /// <summary>テスト境界の外で観測された変異の連番</summary>
    public IReadOnlyList<int> Ambient => ambient;

    /// <summary>観測したテスト結果の文書順での取り出し</summary>
    /// <returns>テストごとの結果の列</returns>
    public IReadOnlyList<WorkerTestResult> TakeResults() =>
        order
            .Select(id =>
            {
                var accumulated = byTestCase[id];
                return new WorkerTestResult(
                    id,
                    accumulated.Name,
                    accumulated.Ms,
                    accumulated.Started > 0 && accumulated.Failed == 0 && !accumulated.Skipped,
                    accumulated.Hits.ToArray(),
                    accumulated.ProbeCount,
                    accumulated.StaticHits.ToArray()
                );
            })
            .ToArray();

    /// <inheritdoc />
    public override void RecordStart(TestCase testCase)
    {
        ambient.AddRange(accessor.DrainHits());
        ambient.AddRange(accessor.DrainStaticHits());
        accessor.TakeHitCount();
        Accumulate(testCase.Id.ToString(), testCase.DisplayName).Started++;
    }

    /// <inheritdoc />
    public override void RecordResult(TestResult testResult)
    {
        var testCase = testResult.TestCase;
        var accumulated = Accumulate(testCase.Id.ToString(), testCase.DisplayName);
        accumulated.Ms += testResult.Duration.TotalMilliseconds;
        accumulated.Hits.UnionWith(accessor.DrainHits());
        var staticHits = accessor.DrainStaticHits();
        accumulated.StaticHits.UnionWith(staticHits);
        ambient.AddRange(staticHits);
        accumulated.ProbeCount += accessor.TakeHitCount();
        if (testResult.Outcome == TestOutcome.Failed)
        {
            accumulated.Failed++;
        }

        if (testResult.Outcome is not (TestOutcome.Passed or TestOutcome.Failed))
        {
            accumulated.Skipped = true;
        }
    }

    /// <summary>一意識別子に対応する累積の取得。初出なら登録する形</summary>
    private Accumulated Accumulate(string id, string? displayName)
    {
        if (!byTestCase.TryGetValue(id, out var accumulated))
        {
            accumulated = new Accumulated { Name = displayName ?? id };
            byTestCase[id] = accumulated;
            order.Add(id);
        }

        return accumulated;
    }

    /// <summary>一テストの観測の累積</summary>
    private sealed class Accumulated
    {
        public required string Name { get; init; }

        public double Ms { get; set; }

        public int Started { get; set; }

        public int Failed { get; set; }

        public bool Skipped { get; set; }

        public long ProbeCount { get; set; }

        public HashSet<int> Hits { get; } = [];

        public HashSet<int> StaticHits { get; } = [];
    }
}
