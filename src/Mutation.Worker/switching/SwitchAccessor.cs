using System.Reflection;

namespace Mutation.Worker;

/// <summary>変異 assembly 内の切替クラスへの反射経由の操作</summary>
public sealed class SwitchAccessor
{
    /// <summary>Activate への束ね</summary>
    private readonly Action<int, long> activate;

    /// <summary>StartCoverage への束ね</summary>
    private readonly Action startCoverage;

    /// <summary>DrainHits への束ね</summary>
    private readonly Func<int[]> drainHits;

    /// <summary>DrainStaticHits への束ね</summary>
    private readonly Func<int[]> drainStaticHits;

    /// <summary>TakeHitCount への束ね</summary>
    private readonly Func<long> takeHitCount;

    /// <summary>TakeHitLimitTripped への束ね</summary>
    private readonly Func<bool> takeHitLimitTripped;

    /// <summary>変異 assembly の切替クラスの発見と操作の束ね</summary>
    /// <param name="mutatedAssembly">MutantSwitch を含む変異 assembly</param>
    public SwitchAccessor(Assembly mutatedAssembly)
    {
        var type =
            mutatedAssembly.GetType("MutationInjected.MutantSwitch")
            ?? throw new InvalidOperationException($"{mutatedAssembly.FullName} に MutantSwitch がない");
        activate = Bind<Action<int, long>>(type, "Activate");
        startCoverage = Bind<Action>(type, "StartCoverage");
        drainHits = Bind<Func<int[]>>(type, "DrainHits");
        drainStaticHits = Bind<Func<int[]>>(type, "DrainStaticHits");
        takeHitCount = Bind<Func<long>>(type, "TakeHitCount");
        takeHitLimitTripped = Bind<Func<bool>>(type, "TakeHitLimitTripped");
    }

    /// <summary>指定した変異の活性化と probe 上限の設定</summary>
    /// <param name="mutantId">活性化する変異の連番。負なら全て不活性</param>
    /// <param name="hitLimit">probe 呼び出し回数の上限</param>
    public void Activate(int mutantId, long hitLimit) => activate(mutantId, hitLimit);

    /// <summary>被覆収集モードへの切替</summary>
    public void StartCoverage() => startCoverage();

    /// <summary>観測済みの変異連番の取り出しと消去</summary>
    /// <returns>前回の取り出し以降に観測した変異の連番</returns>
    public int[] DrainHits() => drainHits();

    /// <summary>static 初期化中に観測した変異連番の取り出しと消去</summary>
    /// <returns>前回の取り出し以降に static 初期化中に観測した変異の連番</returns>
    public int[] DrainStaticHits() => drainStaticHits();

    /// <summary>probe 呼び出し回数の取り出しと零への戻し</summary>
    /// <returns>前回の取り出し以降の probe 呼び出し回数</returns>
    public long TakeHitCount() => takeHitCount();

    /// <summary>probe 上限超過が起きたかの取り出しと消去</summary>
    /// <returns>前回の取り出し以降に上限超過が起きていれば真</returns>
    public bool TakeHitLimitTripped() => takeHitLimitTripped();

    /// <summary>切替クラスの static method への delegate の束ね</summary>
    private static TDelegate Bind<TDelegate>(Type type, string name)
        where TDelegate : Delegate
    {
        var method =
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"MutantSwitch に {name} がない");
        return method.CreateDelegate<TDelegate>();
    }
}
