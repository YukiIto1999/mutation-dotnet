using Mutation.Protocol;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>対象アセンブリへ注入する変異切替クラスのソースの生成</summary>
public static class MutantSwitchSource
{
    /// <summary>注入するクラスの名前空間</summary>
    public const string Namespace = "MutationInjected";

    /// <summary>注入するクラスの型名</summary>
    public const string ClassName = "MutantSwitch";

    /// <summary>活性化する変異の連番を渡す環境変数の名前</summary>
    public const string ActiveEnvironmentVariable = InjectionContract.ActiveEnvironmentVariable;

    /// <summary>probe 呼び出し回数の上限を渡す環境変数の名前</summary>
    public const string HitLimitEnvironmentVariable = InjectionContract.HitLimitEnvironmentVariable;

    /// <summary>被覆収集で起動することを伝える環境変数の名前</summary>
    public const string CoverageEnvironmentVariable = InjectionContract.CoverageEnvironmentVariable;

    /// <summary>probe 上限超過を伝える例外メッセージの目印</summary>
    public const string HitLimitMarker = InjectionContract.HitLimitMarker;

    /// <summary>schemata から呼ぶ活性判定の完全修飾名</summary>
    public const string IsActiveInvocation = Namespace + "." + ClassName + ".IsActive";

    /// <summary>static 初期化子を包む追跡関数の完全修飾名</summary>
    public const string TrackStaticInvocation = Namespace + "." + ClassName + ".TrackStatic";

    /// <summary>static 初期化文脈へ入る呼び出しの完全修飾名</summary>
    public const string EnterStaticInvocation = Namespace + "." + ClassName + ".EnterStatic";

    /// <summary>static 初期化文脈から出る呼び出しの完全修飾名</summary>
    public const string ExitStaticInvocation = Namespace + "." + ClassName + ".ExitStatic";

    /// <summary>変異数に合わせた切替クラスのソースの生成</summary>
    /// <param name="mutantCount">被覆記録表の大きさを決める変異の総数</param>
    /// <returns>対象コンパイルへ追加する C# ソース</returns>
    public static string Render(int mutantCount) =>
        Template.Replace(
            "__COUNT__",
            mutantCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );

    /// <summary>対象へ注入する切替クラスの雛形。__COUNT__ を変異数で置く</summary>
    private const string Template = $$"""
        namespace {{Namespace}}
        {
            public static class {{ClassName}}
            {
                private static bool coverage =
                    global::System.Environment.GetEnvironmentVariable("{{CoverageEnvironmentVariable}}") == "1";
                private static int active = (int)ReadInt("{{ActiveEnvironmentVariable}}", -1);
                private static long hitLimit = ReadInt("{{HitLimitEnvironmentVariable}}", long.MaxValue);
                private static long hitCount;
                private static bool hitLimitTripped;
                private static readonly bool[] hits = new bool[__COUNT__];
                private static readonly bool[] staticHits = new bool[__COUNT__];

                [global::System.ThreadStatic]
                private static int staticDepth;

                public static bool IsActive(int id)
                {
                    if (coverage)
                    {
                        if (staticDepth > 0)
                        {
                            staticHits[id] = true;
                        }
                        else
                        {
                            hits[id] = true;
                        }

                        hitCount++;
                        return false;
                    }

                    if (active < 0)
                    {
                        return false;
                    }

                    if (++hitCount > hitLimit)
                    {
                        hitLimitTripped = true;
                        throw new global::System.InvalidOperationException("{{HitLimitMarker}}");
                    }

                    return active == id;
                }

                public static void EnterStatic()
                {
                    staticDepth++;
                }

                public static void ExitStatic()
                {
                    staticDepth--;
                }

                public static T TrackStatic<T>(global::System.Func<T> initializer)
                {
                    staticDepth++;
                    try
                    {
                        return initializer();
                    }
                    finally
                    {
                        staticDepth--;
                    }
                }

                public static void Activate(int id, long limit)
                {
                    coverage = false;
                    hitLimit = limit;
                    hitCount = 0;
                    hitLimitTripped = false;
                    active = id;
                }

                public static void StartCoverage()
                {
                    active = -1;
                    coverage = true;
                }

                public static bool TakeHitLimitTripped()
                {
                    var tripped = hitLimitTripped;
                    hitLimitTripped = false;
                    return tripped;
                }

                public static long TakeHitCount()
                {
                    var taken = hitCount;
                    hitCount = 0;
                    return taken;
                }

                public static int[] DrainHits()
                {
                    return Drain(hits);
                }

                public static int[] DrainStaticHits()
                {
                    return Drain(staticHits);
                }

                private static int[] Drain(bool[] table)
                {
                    var drained = new global::System.Collections.Generic.List<int>();
                    for (var i = 0; i < table.Length; i++)
                    {
                        if (table[i])
                        {
                            drained.Add(i);
                            table[i] = false;
                        }
                    }

                    return drained.ToArray();
                }

                private static long ReadInt(string name, long fallback)
                {
                    var raw = global::System.Environment.GetEnvironmentVariable(name);
                    return long.TryParse(raw, out var value) ? value : fallback;
                }
            }
        }
        """;
}
