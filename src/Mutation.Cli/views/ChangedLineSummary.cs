using Mutation.Verifying.Domain;
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

        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"変わった行の変異 {verdicts.OnChangedLines.Count} 件のうち未検出 {verdicts.Undetected.Count} 件"
        );
        return builder.ToString();
    }
}
