using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変異とそれを実行するテストの対応表</summary>
/// <param name="TestsByMutant">変異の連番から被覆するテスト連番集合への対応</param>
/// <param name="AmbientMutants">テスト境界の外か static 初期化の区間で実行が観測された変異の連番集合</param>
/// <param name="StaticTestsByMutant">変異の連番から、その static 初期化を引き起こしたテスト連番集合への対応</param>
public sealed record CoverageMap(
    IReadOnlyDictionary<MutantId, IReadOnlyList<TestIndex>> TestsByMutant,
    IReadOnlySet<MutantId> AmbientMutants,
    IReadOnlyDictionary<MutantId, IReadOnlyList<TestIndex>>? StaticTestsByMutant
)
{
    /// <summary>変異の static 初期化を引き起こしたテスト連番の列挙</summary>
    /// <param name="mutantId">対象の変異の連番</param>
    /// <returns>引き起こしたテストの連番。なければ空</returns>
    public IReadOnlyList<TestIndex> TriggeringTests(MutantId mutantId) =>
        StaticTestsByMutant is { } byMutant && byMutant.TryGetValue(mutantId, out var tests) ? tests : [];

    /// <summary>変異を被覆するテスト連番の列挙</summary>
    /// <param name="mutantId">対象の変異の連番</param>
    /// <returns>被覆するテストの連番。被覆がなければ空</returns>
    public IReadOnlyList<TestIndex> CoveringTests(MutantId mutantId) =>
        TestsByMutant.TryGetValue(mutantId, out var tests) ? tests : [];

    /// <summary>テスト境界の外で実行された変異か</summary>
    /// <param name="mutantId">対象の変異の連番</param>
    /// <returns>境界外の実行が観測されていれば真</returns>
    public bool IsAmbient(MutantId mutantId) => AmbientMutants.Contains(mutantId);
}
