using System.Diagnostics;
using System.Runtime.CompilerServices;

// schemata の切替プローブ 1 回あたりの費用を、実装方式ごとに測る PoC。
// 被検コードは 1 反復に 8 箇所の mutant 位置を持つ整数演算ループ(典型的な純粋ロジックの代理)。
// 単一 thread と 4 thread(並列テストの代理)で測る。
static class Current // mutation-dotnet の MutantSwitch と同じ形(非 readonly の static と全プローブの hitCount++)
{
    static bool coverage = false;
    static int active = -1;
    static long hitLimit = long.MaxValue;
    static long hitCount;
    public static void Activate(int id) { coverage = false; active = id; hitCount = 0; }
    public static bool IsActive(int id)
    {
        if (coverage) { hitCount++; return false; }
        if (active < 0) return false;
        if (++hitCount > hitLimit) throw new InvalidOperationException("limit");
        return active == id;
    }
}

static class ThreadLocalCount // hit 数を thread 局所に持ち、共有 cache line への書き込みを無くす
{
    static int active = -1;
    [ThreadStatic] static long hitCount;
    static long hitLimit = long.MaxValue;
    public static void Activate(int id) { active = id; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsActive(int id)
    {
        var a = active;
        if (a < 0) return false;
        if (++hitCount > hitLimit) Throw();
        return a == id;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Throw() => throw new InvalidOperationException("limit");
}

static class ActiveOnly // 活性 mutant の枝に入ったときだけ数える(無限ループ検出は別機構に任せる前提)
{
    static int active = -1;
    public static void Activate(int id) { active = id; }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsActive(int id) => active == id;
}

static class ReadonlyFold // process 起動時に固定した mutant id。tier-1 JIT が定数として畳み込む
{
    static readonly int active = int.Parse(Environment.GetEnvironmentVariable("PROBE_ACTIVE") ?? "-1");
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsActive(int id) => active == id;
}

static class Work
{
    public static long Baseline(int n)
    {
        long acc = 0;
        for (var i = 0; i < n; i++)
        {
            var a = i * 3 + 1;
            var b = a % 7 == 0 ? a / 7 : a - 2;
            var c = b > 100 ? b - 100 : b + 5;
            acc += (c & 1) == 0 ? c : -c;
            acc ^= i < 50 ? 1 : 2;
        }
        return acc;
    }

    public static long Current_(int n)
    {
        long acc = 0;
        for (var i = 0; i < n; i++)
        {
            var a = Current.IsActive(0) ? i * 3 - 1 : i * 3 + 1;
            var b = (Current.IsActive(1) ? a % 7 != 0 : a % 7 == 0) ? (Current.IsActive(2) ? a * 7 : a / 7) : (Current.IsActive(3) ? a + 2 : a - 2);
            var c = (Current.IsActive(4) ? b >= 100 : b > 100) ? (Current.IsActive(5) ? b + 100 : b - 100) : b + 5;
            acc += (Current.IsActive(6) ? (c & 1) != 0 : (c & 1) == 0) ? c : -c;
            acc ^= (Current.IsActive(7) ? i <= 50 : i < 50) ? 1 : 2;
        }
        return acc;
    }

    public static long ThreadLocal_(int n)
    {
        long acc = 0;
        for (var i = 0; i < n; i++)
        {
            var a = ThreadLocalCount.IsActive(0) ? i * 3 - 1 : i * 3 + 1;
            var b = (ThreadLocalCount.IsActive(1) ? a % 7 != 0 : a % 7 == 0) ? (ThreadLocalCount.IsActive(2) ? a * 7 : a / 7) : (ThreadLocalCount.IsActive(3) ? a + 2 : a - 2);
            var c = (ThreadLocalCount.IsActive(4) ? b >= 100 : b > 100) ? (ThreadLocalCount.IsActive(5) ? b + 100 : b - 100) : b + 5;
            acc += (ThreadLocalCount.IsActive(6) ? (c & 1) != 0 : (c & 1) == 0) ? c : -c;
            acc ^= (ThreadLocalCount.IsActive(7) ? i <= 50 : i < 50) ? 1 : 2;
        }
        return acc;
    }

    public static long ActiveOnly_(int n)
    {
        long acc = 0;
        for (var i = 0; i < n; i++)
        {
            var a = ActiveOnly.IsActive(0) ? i * 3 - 1 : i * 3 + 1;
            var b = (ActiveOnly.IsActive(1) ? a % 7 != 0 : a % 7 == 0) ? (ActiveOnly.IsActive(2) ? a * 7 : a / 7) : (ActiveOnly.IsActive(3) ? a + 2 : a - 2);
            var c = (ActiveOnly.IsActive(4) ? b >= 100 : b > 100) ? (ActiveOnly.IsActive(5) ? b + 100 : b - 100) : b + 5;
            acc += (ActiveOnly.IsActive(6) ? (c & 1) != 0 : (c & 1) == 0) ? c : -c;
            acc ^= (ActiveOnly.IsActive(7) ? i <= 50 : i < 50) ? 1 : 2;
        }
        return acc;
    }

    public static long Readonly_(int n)
    {
        long acc = 0;
        for (var i = 0; i < n; i++)
        {
            var a = ReadonlyFold.IsActive(0) ? i * 3 - 1 : i * 3 + 1;
            var b = (ReadonlyFold.IsActive(1) ? a % 7 != 0 : a % 7 == 0) ? (ReadonlyFold.IsActive(2) ? a * 7 : a / 7) : (ReadonlyFold.IsActive(3) ? a + 2 : a - 2);
            var c = (ReadonlyFold.IsActive(4) ? b >= 100 : b > 100) ? (ReadonlyFold.IsActive(5) ? b + 100 : b - 100) : b + 5;
            acc += (ReadonlyFold.IsActive(6) ? (c & 1) != 0 : (c & 1) == 0) ? c : -c;
            acc ^= (ReadonlyFold.IsActive(7) ? i <= 50 : i < 50) ? 1 : 2;
        }
        return acc;
    }
}

static class Program
{
    const int N = 20_000_000;

    static double Measure(Func<int, long> body, int threads)
    {
        for (var w = 0; w < 5; w++) body(N / 20); // tier-up のための助走
        Thread.Sleep(200);
        for (var w = 0; w < 3; w++) body(N / 10);
        var samples = new List<double>();
        for (var r = 0; r < 7; r++)
        {
            var sw = Stopwatch.StartNew();
            if (threads == 1) body(N);
            else Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ => body(N));
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }

    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "inactive";
        // 実行時の状況: inactive = 変異を活性にしない(被覆外の部分の実行に相当), other = 別の mutant が活性(mutant 実行中に通る他の位置)
        var id = mode == "other" ? 999 : -1;
        Current.Activate(id);
        ThreadLocalCount.Activate(id);
        ActiveOnly.Activate(id);
        foreach (var threads in new[] { 1, 4 })
        {
            var baseline = Measure(Work.Baseline, threads);
            Console.WriteLine($"threads={threads} mode={mode} probes/iter=8 N={N}");
            Report("baseline (no probes)", baseline, baseline);
            Report("current (static fields + shared hitCount++)", Measure(Work.Current_, threads), baseline);
            Report("thread-local hitCount", Measure(Work.ThreadLocal_, threads), baseline);
            Report("active-only compare", Measure(Work.ActiveOnly_, threads), baseline);
            Report("static readonly fold (PROBE_ACTIVE env)", Measure(Work.Readonly_, threads), baseline);
        }
    }

    static void Report(string name, double ms, double baseline) =>
        Console.WriteLine($"  {name,-48} {ms,9:F1} ms  x{ms / baseline:F2}  {(ms - baseline) * 1e6 / (N * 8.0):F2} ns/probe");
}
