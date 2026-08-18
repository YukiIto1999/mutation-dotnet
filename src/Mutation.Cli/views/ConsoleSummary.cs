using Mutation.Verifying.Domain;
using System.Globalization;
using System.Text;

namespace Mutation.Cli;

/// <summary>実行結果の要約文の組み立て</summary>
public static class ConsoleSummary
{
    /// <summary>状態別件数と検出率と段階別時間の一枚への集約</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <returns>端末へ出す複数行の要約</returns>
    public static string Render(MutationRunResult result)
    {
        var builder = new StringBuilder();
        var byStatus = result
            .Verdicts.Values.GroupBy(VerdictNames.For)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in byStatus)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{group.Key,-13} {group.Count(),6}");
        }

        var score = result.Score is { } value ? value.ToString("P2", CultureInfo.InvariantCulture) : "-";
        builder.AppendLine(CultureInfo.InvariantCulture, $"mutation score {score}");
        var t = result.Timings;
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"build {t.BuildMs:F0}ms / mutate {t.MutateMs:F0}ms / compile {t.CompileMs:F0}ms"
                + $" / baseline {t.BaselineMs:F0}ms / testing {t.TestingMs:F0}ms / total {t.TotalMs:F0}ms"
        );
        return builder.ToString();
    }
}
