using Mutation.Shared;
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

        foreach (var target in result.Completed.Where(target => target.Selection is not null))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"選別 {target.Name}: {Selection(target.Selection!)}");
        }

        var byStatus = result
            .Completed.SelectMany(t => t.Verdicts.Values)
            .GroupBy(VerdictNames.For)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in byStatus)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{group.Key,-13} {group.Count(),6}");
        }

        if (result.IsInterrupted || result.UnjudgedCount > 0)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"走行 {result.State}: 判定を確定できなかった変異 {result.UnjudgedCount} 件を Pending と報告し、score の母数に入れていない"
            );
        }

        if (result.FailedTargets.Any())
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"走行 {result.State}: 判定に至らなかった対象 {string.Join(", ", result.FailedTargets.Select(failure => failure.Name))} がある。完了した走行として扱えない"
            );
        }

        if (result.RunnerFailureCount > 0)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"test runner の失敗で判定できなかった変異 {result.RunnerFailureCount} 件がある。検出に数えておらず、テストが変異を検出した結果ではない"
            );
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

    /// <summary>`--since` の実行で、対象の変異候補を選んだ範囲と理由の一行</summary>
    private static string Selection(CandidateSelection selection) =>
        selection.Scope switch
        {
            SelectionScope.NoImpact =>
                $"no-impact: 対象の入力に届く変更がなく、候補は空(対象の入力でない変更 {selection.NotInputs.Count} 件)",
            SelectionScope.FullProduction =>
                $"full-production: {Relations(selection.Reasons)} が対象へ届き、届く先を file より細かく選べないため、対象の全範囲を候補にした",
            SelectionScope.ExplicitSubset =>
                $"changed-lines: 変更行に重なる変異だけの明示した部分集合で、影響する範囲の分析ではない。未分析: {Relations(selection.Unanalysed)}",
        };

    /// <summary>変更した file が対象へ届く関係と件数の列挙。なければ「なし」</summary>
    private static string Relations(IReadOnlyList<ImpactReason> reasons) =>
        reasons.Count == 0
            ? "なし"
            : string.Join(", ", reasons.Select(reason => $"{reason.Relation.Name} {reason.Paths.Count} 件"));

    /// <summary>対象一件の検出率。検出も非検出もなければ不在</summary>
    private static double? Score(TargetResult target) =>
        target.DetectedCount + target.UndetectedCount == 0
            ? null
            : (double)target.DetectedCount / (target.DetectedCount + target.UndetectedCount);

    /// <summary>検出率の百分率表記。不在なら横棒</summary>
    private static string Percent(double? score) =>
        score is { } value ? value.ToString("P2", CultureInfo.InvariantCulture) : "-";
}
