using System.Diagnostics;
using Mutation.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Mutation.Worker;

/// <summary>xunit v2 のテスト assembly を常駐で繰り返し実行するホスト</summary>
public sealed class XunitTestHost : ITestHost
{
    /// <summary>xunit v2 の常駐実行の入口</summary>
    private readonly XunitFrontController controller;

    /// <summary>変異 assembly の切替クラスへの操作</summary>
    private readonly SwitchAccessor accessor;

    /// <summary>一意識別子から test case への対応</summary>
    private readonly Dictionary<string, ITestCase> byId;

    /// <summary>並列無効を固定した実行設定</summary>
    private readonly ITestFrameworkExecutionOptions executionOptions;

    private XunitTestHost(
        XunitFrontController controller,
        SwitchAccessor accessor,
        Dictionary<string, ITestCase> byId,
        ITestFrameworkExecutionOptions executionOptions
    )
    {
        this.controller = controller;
        this.accessor = accessor;
        this.byId = byId;
        this.executionOptions = executionOptions;
    }

    /// <summary>assembly の読込先の差し替えとテスト発見によるホストの開設</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <param name="mutatedDirectory">変異 assembly の置き場</param>
    /// <param name="targetAssemblyName">差し替え対象 assembly の単純名</param>
    /// <returns>実行準備の整ったホスト</returns>
    public static XunitTestHost Open(string testAssembly, string mutatedDirectory, string targetAssemblyName)
    {
        var mutatedAssembly = HostEnvironment.Prepare(testAssembly, mutatedDirectory, targetAssemblyName);
        var accessor = new SwitchAccessor(mutatedAssembly);
        var controller = new XunitFrontController(AppDomainSupport.Denied, testAssembly, shadowCopy: false);
        var discoveryOptions = TestFrameworkOptions.ForDiscovery();
        discoveryOptions.SetValue("xunit.discovery.PreEnumerateTheories", true);
        var discovery = new DiscoverySink();
        controller.Find(includeSourceInformation: false, discovery, discoveryOptions);
        discovery.Finished.Wait();
        var byId = discovery.TestCases.ToDictionary(c => c.UniqueID, c => c);
        var executionOptions = TestFrameworkOptions.ForExecution();
        executionOptions.SetValue("xunit.execution.DisableParallelization", true);
        executionOptions.SetValue("xunit.execution.MaxParallelThreads", 1);
        executionOptions.SetValue("xunit.execution.SynchronousMessageReporting", true);
        return new XunitTestHost(controller, accessor, byId, executionOptions);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Discover() => byId.Keys.Distinct(StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public WorkerResponse.Baselined Baseline(IReadOnlyList<string> testIds)
    {
        accessor.StartCoverage();
        var sink = new CoverageSink(accessor);
        var cases = testIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        controller.RunTests(cases, sink, executionOptions);
        sink.Finished.Wait();
        accessor.Activate(-1, long.MaxValue);
        var ambient = sink.Ambient.Distinct().ToArray();
        return new WorkerResponse.Baselined(sink.TakeResults(), ambient);
    }

    /// <inheritdoc />
    public WorkerResponse Run(WorkerRequest.Run request)
    {
        var stopwatch = Stopwatch.StartNew();
        var cases = request.TestIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        if (!HostEnvironment.IsPreactivated)
        {
            accessor.Activate(request.MutantId, request.HitLimit ?? long.MaxValue);
        }

        var sink = new MutantRunSink(accessor, failFast: true);
        controller.RunTests(cases, sink, executionOptions);
        sink.Finished.Wait();
        if (!HostEnvironment.IsPreactivated)
        {
            accessor.Activate(-1, long.MaxValue);
        }

        return new WorkerResponse.RunCompleted(
            RunOutcomes.From(sink.KillerTest, sink.HitLimitExceeded),
            sink.KillerTest,
            sink.ExecutedTests,
            stopwatch.Elapsed.TotalMilliseconds
        );
    }

    /// <inheritdoc />
    public void Dispose() => controller.Dispose();
}
