using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text;

namespace Mutation.Cli;

/// <summary>実行結果の要約文の組み立て</summary>
public static class ConsoleSummary
{
    /// <summary>対象ごとの帰結と、状態別件数と検出率と段階別時間の一枚への集約</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <returns>端末へ出す複数行の要約</returns>
    public static string Render(MutationRunResult result)
    {
        var builder = new StringBuilder();
        foreach (var outcome in result.Outcomes)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{Describe(outcome)}");
        }

        var byStatus = result
            .Completed.SelectMany(t => t.Verdicts.Values)
            .GroupBy(VerdictNames.For)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in byStatus)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{group.Key,-13} {group.Count(),6}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"mutation score {Percent(result.Score)}");
        var t = result.Timings;
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"build {t.BuildMs:F0}ms / mutate {t.MutateMs:F0}ms / compile {t.CompileMs:F0}ms"
                + $" / baseline {t.BaselineMs:F0}ms / testing {t.TestingMs:F0}ms / total {t.TotalMs:F0}ms"
        );
        return builder.ToString();
    }

    /// <summary>対象一件の帰結の一行</summary>
    private static string Describe(TargetOutcome outcome) =>
        outcome switch
        {
            TargetOutcome.Completed completed => string.Create(
                CultureInfo.InvariantCulture,
                $"{completed.Result.Name,-40} 変異 {completed.Result.Mutants.Count,6}  score {Percent(Score(completed.Result))}"
            ),
            TargetOutcome.Failed failed => string.Create(
                CultureInfo.InvariantCulture,
                $"{failed.Name,-40} 中断  {FailureLines.Describe(failed.Failure)}"
            ),
        };

    /// <summary>対象一件の検出率。検出も非検出もなければ不在</summary>
    private static double? Score(TargetResult target) =>
        target.DetectedCount + target.UndetectedCount == 0
            ? null
            : (double)target.DetectedCount / (target.DetectedCount + target.UndetectedCount);

    /// <summary>検出率の百分率表記。不在なら横棒</summary>
    private static string Percent(double? score) =>
        score is { } value ? value.ToString("P2", CultureInfo.InvariantCulture) : "-";
}
