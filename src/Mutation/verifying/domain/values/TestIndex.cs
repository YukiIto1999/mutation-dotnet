

namespace Mutation.Verifying.Domain;

/// <summary>baseline 実行で振ったテストの連番</summary>
/// <param name="Value">観測順の整数値</param>
public readonly record struct TestIndex(int Value);
