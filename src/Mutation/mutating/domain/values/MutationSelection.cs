
namespace Mutation.Mutating.Domain;

/// <summary>変異対象の選別の入力</summary>
/// <param name="MutatePatterns">変異対象に含めるファイルの glob。先頭 `!` は除外。空なら全ファイル</param>
/// <param name="SinceRef">差分運用の基点になる git の参照。使わないなら不在</param>
/// <param name="IgnoredOperators">除外する変異演算子の名前の列</param>
/// <param name="IgnoredMethods">その呼び出しの中を変異させない method 名の列</param>
public sealed record MutationSelection(
    IReadOnlyList<string> MutatePatterns,
    string? SinceRef,
    IReadOnlyList<string> IgnoredOperators,
    IReadOnlyList<string> IgnoredMethods
);
