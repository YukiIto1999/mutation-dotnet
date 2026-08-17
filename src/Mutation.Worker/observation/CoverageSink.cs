using Mutation.Protocol;
using Xunit.Abstractions;

namespace Mutation.Worker;

/// <summary>baseline 実行でテストごとの結果と被覆を集める受け口</summary>
public sealed class CoverageSink(SwitchAccessor accessor) : IMessageSink
{
    /// <summary>テストの一意識別子から観測の累積への対応</summary>
    private readonly Dictionary<string, Accumulated> byTestCase = [];
    /// <summary>観測した順の一意識別子</summary>
    private readonly List<string> order = [];
    /// <summary>テスト境界の外で観測した変異の連番</summary>
    private readonly List<int> ambient = [];

    /// <summary>assembly 実行の完了を伝える合図</summary>
    public ManualResetEventSlim Finished { get; } = new(false);

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
                    accumulated.Started > 0 && accumulated.Failed == 0,
                    accumulated.Hits.ToArray(),
                    accumulated.ProbeCount,
                    accumulated.StaticHits.ToArray()
                );
            })
            .ToArray();

    /// <summary>テスト境界の外で観測された変異の連番</summary>
    public IReadOnlyList<int> Ambient => ambient;

    /// <inheritdoc />
    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestStarting starting:
                ambient.AddRange(accessor.DrainHits());
                ambient.AddRange(accessor.DrainStaticHits());
                accessor.TakeHitCount();
                Accumulate(starting).Started++;
                break;
            case ITestPassed passed:
                Settle(passed, passed.ExecutionTime, failed: false);
                break;
            case ITestFailed failed:
                Settle(failed, failed.ExecutionTime, failed: true);
                break;
            case ITestSkipped skipped:
                Accumulate(skipped);
                break;
            case ITestAssemblyFinished:
                ambient.AddRange(accessor.DrainHits());
                ambient.AddRange(accessor.DrainStaticHits());
                Finished.Set();
                break;
        }

        return true;
    }

    /// <summary>一テストの完了の取り込みと hit の帰属</summary>
    private void Settle(ITestCaseMessage message, decimal executionTime, bool failed)
    {
        var accumulated = Accumulate(message);
        accumulated.Ms += (double)executionTime * 1000.0;
        accumulated.Hits.UnionWith(accessor.DrainHits());
        var staticHits = accessor.DrainStaticHits();
        accumulated.StaticHits.UnionWith(staticHits);
        ambient.AddRange(staticHits);
        accumulated.ProbeCount += accessor.TakeHitCount();
        if (failed)
        {
            accumulated.Failed++;
        }
    }

    /// <summary>一意識別子に対応する累積の取得。初出なら登録する形</summary>
    private Accumulated Accumulate(ITestCaseMessage message)
    {
        var id = message.TestCase.UniqueID;
        if (!byTestCase.TryGetValue(id, out var accumulated))
        {
            accumulated = new Accumulated { Name = message.TestCase.DisplayName };
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

        public long ProbeCount { get; set; }

        public HashSet<int> Hits { get; } = [];

        public HashSet<int> StaticHits { get; } = [];
    }
}
