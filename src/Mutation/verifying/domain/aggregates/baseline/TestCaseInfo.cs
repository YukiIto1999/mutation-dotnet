

namespace Mutation.Verifying.Domain;

/// <summary>初回実行で観測したテスト一件の素性</summary>
/// <param name="Index">被覆表と実行指示で使う連番</param>
/// <param name="DisplayName">テストの表示名</param>
/// <param name="BaselineMs">変異なし実行での所要時間</param>
/// <param name="BaselinePassed">変異なし実行で成功したか</param>
public sealed record TestCaseInfo(TestIndex Index, string DisplayName, double BaselineMs, bool BaselinePassed);
