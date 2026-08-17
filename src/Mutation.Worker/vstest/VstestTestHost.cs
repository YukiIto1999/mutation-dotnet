using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Mutation.Protocol;

namespace Mutation.Worker;

/// <summary>VSTest adapter(NUnit / MSTest)を自 process に載せて繰り返し実行するホスト</summary>
public sealed class VstestTestHost : ITestHost
{
    /// <summary>対応する adapter assembly の file 名</summary>
    private static readonly string[] AdapterFiles =
    [
        "NUnit3.TestAdapter.dll",
        "Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.dll",
    ];

    /// <summary>変異 assembly の切替クラスへの操作</summary>
    private readonly SwitchAccessor accessor;

    /// <summary>実行のたびに executor を作る工場</summary>
    private readonly Func<ITestExecutor> executorFactory;

    /// <summary>一意識別子から test case への対応</summary>
    private readonly Dictionary<string, TestCase> byId;

    // adapter の executor は Cancel 後に再利用できないため、実行のたびに作り直す
    private VstestTestHost(SwitchAccessor accessor, Func<ITestExecutor> executorFactory, Dictionary<string, TestCase> byId)
    {
        this.accessor = accessor;
        this.executorFactory = executorFactory;
        this.byId = byId;
    }

    /// <summary>directory に置かれた対応 adapter の path。なければ不在</summary>
    /// <param name="directory">テスト assembly の directory</param>
    /// <returns>読み込める adapter assembly の絶対 path</returns>
    public static string? AdapterPath(string directory) =>
        AdapterFiles.Select(file => Path.Combine(directory, file)).FirstOrDefault(File.Exists);

    /// <summary>assembly の読込先の差し替えと adapter の読込とテスト発見によるホストの開設</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <param name="mutatedDirectory">変異 assembly の置き場</param>
    /// <param name="targetAssemblyName">差し替え対象 assembly の単純名</param>
    /// <param name="adapterPath">adapter assembly の絶対 path</param>
    /// <returns>実行準備の整ったホスト</returns>
    public static VstestTestHost Open(
        string testAssembly,
        string mutatedDirectory,
        string targetAssemblyName,
        string adapterPath
    )
    {
        var mutatedAssembly = HostEnvironment.Prepare(testAssembly, mutatedDirectory, targetAssemblyName);
        var accessor = new SwitchAccessor(mutatedAssembly);
        var adapter = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(adapterPath);
        var discoverer = Instantiate<ITestDiscoverer>(adapter);
        var sink = new VstestSession.CaseSink();
        discoverer.DiscoverTests([testAssembly], VstestSession.Context(), VstestSession.Logger, sink);
        var byId = new Dictionary<string, TestCase>(StringComparer.Ordinal);
        foreach (var testCase in sink.Cases)
        {
            byId.TryAdd(testCase.Id.ToString(), testCase);
        }

        return new VstestTestHost(accessor, () => Instantiate<ITestExecutor>(adapter), byId);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Discover() => byId.Keys.ToArray();

    /// <inheritdoc />
    public WorkerResponse.Baselined Baseline(IReadOnlyList<string> testIds)
    {
        accessor.StartCoverage();
        var recorder = new VstestCoverageRecorder(accessor);
        // adapter は RecordStart を一括で先送りし得るため、帰属はテスト 1 件ずつの実行で確保する
        foreach (var testCase in Select(testIds))
        {
            executorFactory().RunTests([testCase], VstestSession.Context(), recorder);
        }

        accessor.Activate(-1, long.MaxValue);
        return new WorkerResponse.Baselined(recorder.TakeResults(), recorder.Ambient.Distinct().ToArray());
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

        // Cancel は engine の停止待ちが長く状態も汚すため、失敗の記録だけで打ち切りに代える
        var recorder = new VstestRunRecorder(accessor);
        executorFactory().RunTests(cases, VstestSession.Context(), recorder);
        if (!HostEnvironment.IsPreactivated)
        {
            accessor.Activate(-1, long.MaxValue);
        }

        return new WorkerResponse.RunCompleted(
            RunOutcomes.From(recorder.KillerTest, recorder.HitLimitExceeded),
            recorder.KillerTest,
            recorder.ExecutedTests,
            stopwatch.Elapsed.TotalMilliseconds
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>一意識別子列に対応する test case の選択</summary>
    private IEnumerable<TestCase> Select(IReadOnlyList<string> testIds) =>
        testIds.Where(byId.ContainsKey).Select(id => byId[id]);

    /// <summary>adapter assembly からの契約実装の生成</summary>
    private static TContract Instantiate<TContract>(Assembly adapter)
        where TContract : class
    {
        var type =
            adapter.GetTypes().FirstOrDefault(t => !t.IsAbstract && typeof(TContract).IsAssignableFrom(t))
            ?? throw new InvalidOperationException($"{adapter.FullName} に {typeof(TContract).Name} の実装がない");
        return (TContract)Activator.CreateInstance(type)!;
    }
}
