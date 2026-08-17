using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Mutation.Protocol;

namespace Mutation.Worker;

/// <summary>Microsoft.Testing.Platform の実行体を自 process で繰り返し起動するホスト</summary>
public sealed class PlatformTestHost : ITestHost
{
    /// <summary>一致するテストがなかったことを表す実行体の終了コード</summary>
    private const int ExitZeroTests = 8;

    /// <summary>テスト実行体の entry point</summary>
    private readonly MethodInfo entryPoint;

    /// <summary>変異 assembly の切替クラスへの操作</summary>
    private readonly SwitchAccessor accessor;

    /// <summary>テスト assembly の絶対 path</summary>
    private readonly string testAssembly;

    /// <summary>基盤ごとの引数の違い</summary>
    private readonly PlatformFlavor flavor;

    /// <summary>毎回の実行に付ける共通引数</summary>
    private readonly string[] commonArguments;

    private PlatformTestHost(
        MethodInfo entryPoint,
        SwitchAccessor accessor,
        string testAssembly,
        PlatformFlavor flavor,
        string resultsDirectory
    )
    {
        this.entryPoint = entryPoint;
        this.accessor = accessor;
        this.testAssembly = testAssembly;
        this.flavor = flavor;
        commonArguments =
        [
            "--no-ansi",
            "--results-directory",
            resultsDirectory,
            .. flavor.CommonArguments,
        ];
    }

    /// <summary>assembly の読込先の差し替えと実行体の入口の取得によるホストの開設</summary>
    /// <param name="testAssembly">テスト assembly の絶対 path</param>
    /// <param name="mutatedDirectory">変異 assembly の置き場</param>
    /// <param name="targetAssemblyName">差し替え対象 assembly の単純名</param>
    /// <param name="resultsDirectory">実行体が成果物を書く directory</param>
    /// <returns>実行準備の整ったホスト</returns>
    public static PlatformTestHost Open(
        string testAssembly,
        string mutatedDirectory,
        string targetAssemblyName,
        string resultsDirectory
    )
    {
        var mutatedAssembly = HostEnvironment.Prepare(testAssembly, mutatedDirectory, targetAssemblyName);
        var accessor = new SwitchAccessor(mutatedAssembly);
        Environment.SetEnvironmentVariable("TESTINGPLATFORM_TELEMETRY_OPTOUT", "1");
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(testAssembly);
        var entryPoint =
            assembly.EntryPoint ?? throw new InvalidOperationException($"{testAssembly} に entry point がない");
        Directory.CreateDirectory(resultsDirectory);
        var flavor = PlatformFlavor.Detect(Path.GetDirectoryName(testAssembly) ?? ".");
        return new PlatformTestHost(entryPoint, accessor, testAssembly, flavor, resultsDirectory);
    }

    /// <summary>発見済みテストの控え。初回の Discover まで不在</summary>
    private IReadOnlyList<DiscoveredTest>? discovered;

    /// <inheritdoc />
    public IReadOnlyList<string> Discover() => Discovered().Select(t => t.Uid).Distinct(StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public WorkerResponse.Baselined Baseline(IReadOnlyList<string> testIds)
    {
        var selected = Discovered()
            .Where(t => testIds.Contains(t.Uid, StringComparer.Ordinal))
            .DistinctBy(t => t.Uid, StringComparer.Ordinal)
            .ToArray();
        accessor.StartCoverage();
        var ambient = new HashSet<int>(accessor.DrainHits());
        ambient.UnionWith(accessor.DrainStaticHits());
        accessor.TakeHitCount();
        var results = new List<WorkerTestResult>(selected.Length);
        foreach (var test in selected)
        {
            var stopwatch = Stopwatch.StartNew();
            var code = Invoke(["--filter-uid", test.Uid, .. commonArguments]);
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            var hits = accessor.DrainHits();
            var staticHits = accessor.DrainStaticHits();
            ambient.UnionWith(staticHits);
            results.Add(
                new WorkerTestResult(test.Uid, test.DisplayName, elapsed, code == 0, hits, accessor.TakeHitCount(), staticHits)
            );
        }

        accessor.Activate(-1, long.MaxValue);
        return new WorkerResponse.Baselined(results, ambient.ToArray());
    }

    /// <inheritdoc />
    public WorkerResponse Run(WorkerRequest.Run request)
    {
        var stopwatch = Stopwatch.StartNew();
        var uids = request.TestIds;
        if (uids.Count == 0)
        {
            return new WorkerResponse.RunCompleted(WorkerContract.OutcomeSurvived, null, 0, 0);
        }

        if (!HostEnvironment.IsPreactivated)
        {
            accessor.Activate(request.MutantId, request.HitLimit ?? long.MaxValue);
        }

        var code = Invoke(["--filter-uid", .. uids, .. commonArguments, .. flavor.FailFastArguments]);
        var tripped = accessor.TakeHitLimitTripped();
        if (!HostEnvironment.IsPreactivated)
        {
            accessor.Activate(-1, long.MaxValue);
        }

        if (code == ExitZeroTests)
        {
            return new WorkerResponse.Failed("指定した uid に一致するテストがない");
        }

        var outcome = code switch
        {
            0 => WorkerContract.OutcomeSurvived,
            _ when tripped => WorkerContract.OutcomeHitLimit,
            _ => WorkerContract.OutcomeKilled,
        };
        return new WorkerResponse.RunCompleted(outcome, null, null, stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>発見済みテストの取得。初回だけ子 process で発見する形</summary>
    private IReadOnlyList<DiscoveredTest> Discovered() => discovered ??= PlatformDiscovery.List(testAssembly);

    /// <summary>実行体の entry point の呼び出しと終了コードの取得</summary>
    private int Invoke(string[] arguments)
    {
        try
        {
            return entryPoint.Invoke(null, [arguments]) is int code ? code : -1;
        }
        catch (TargetInvocationException)
        {
            return -1;
        }
    }
}
