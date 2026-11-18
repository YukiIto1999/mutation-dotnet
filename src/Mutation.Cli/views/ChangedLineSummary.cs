using Mutation.Domain;
using System.Globalization;
using System.Text;

namespace Mutation.Cli;

/// <summary>変わった行に重なる変異の判定結果の要約文の組み立て</summary>
public static class ChangedLineSummary
{
    /// <summary>未検出の変異の一覧と、変わった行に重なる変異の件数の一枚への集約</summary>
    /// <param name="verdicts">変わった行に重なる変異の確定結果</param>
    /// <param name="baseDirectory">表示する path の基準になる directory</param>
    /// <returns>端末へ出す複数行の要約</returns>
    public static string Render(ChangedLineVerdicts verdicts, string baseDirectory)
    {
        var builder = new StringBuilder();
        foreach (var mutant in verdicts.Undetected)
        {
            var path = Path.GetRelativePath(baseDirectory, mutant.FilePath).Replace('\\', '/');
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"{path}:{mutant.Span.Line}:{mutant.Span.Column} {VerdictNames.For(mutant.Verdict)} {mutant.OperatorName}: {mutant.Replacement}"
            );
        }

        var unjudged = verdicts.Unjudged.Count > 0 ? $"、判定を確定できなかったもの {verdicts.Unjudged.Count} 件" : "";
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"変わった行の変異 {verdicts.OnChangedLines.Count} 件のうち未検出 {verdicts.Undetected.Count} 件{unjudged}"
        );
        if (verdicts.Unanalysed.Count > 0)
        {
            builder.AppendLine(OmittedImpact(verdicts.Unanalysed));
        }

        return builder.ToString();
    }

    /// <summary>判定が含まない影響範囲の一文。確定した判定にも、確定できなかった失敗にも同じ文を添える</summary>
    /// <param name="unanalysed">変更行の選別が未分析として残した影響の関係と件数</param>
    /// <returns>未分析の影響範囲を挙げ、判定がそれらを含まないと断る一行</returns>
    public static string OmittedImpact(IReadOnlyList<string> unanalysed) =>
        $"未分析の影響範囲 {string.Join(", ", unanalysed)}。この判定は変更行の変異だけの結果で、影響する範囲を含まない";
}
