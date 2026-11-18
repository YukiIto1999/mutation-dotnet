namespace Mutation.Domain;

/// <summary>走行の間、全ての対象で共通の、入力の閉包の部分</summary>
/// <param name="Digest">engine と compiler と worker の実体と runtime の版、実行環境、設定の要約から求めた hash</param>
/// <param name="OwnedDirectory">engine 自身が成果物を書く directory の絶対 path。test の directory の中にあっても、test の閉包に入れない</param>
public sealed record RunClosure(string Digest, string OwnedDirectory);
