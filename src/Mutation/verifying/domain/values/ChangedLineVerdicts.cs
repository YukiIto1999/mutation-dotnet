using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変わった行に重なる変異の確定結果。変更ごとの検査の合否の材料</summary>
/// <param name="OnChangedLines">範囲が変わった行と一行でも重なる変異。報告に並んだ順</param>
public sealed record ChangedLineVerdicts(IReadOnlyList<RecordedMutant> OnChangedLines)
{
    /// <summary>テストが検出しなかった変異。生存と被覆なし</summary>
    public IReadOnlyList<RecordedMutant> Undetected { get; } =
        [.. OnChangedLines.Where(mutant => mutant.Verdict.IsUndetected)];

    /// <summary>報告の変異から、変わった行に重なるものだけの選別</summary>
    /// <remarks>変わった行の外にある変異は、確定結果によらず合否に入れない</remarks>
    /// <param name="mutants">報告に記録された全変異</param>
    /// <param name="changes">差分の基点から変わった行</param>
    /// <returns>変わった行に重なる変異の確定結果</returns>
    public static ChangedLineVerdicts Select(IEnumerable<RecordedMutant> mutants, ChangedLines changes) =>
        new([.. mutants.Where(mutant => changes.Overlaps(mutant.FilePath, mutant.Span.Line, mutant.Span.EndLine))]);
}
