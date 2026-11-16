using System.Globalization;
using Mutation.Shared;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Infrastructure.Reports;

/// <summary>走行が全件の判定を確定したかの、二つの報告への記述</summary>
internal static class RunStateReport
{
    /// <summary>走行の状態と、判定を確定できなかった変異の内訳の記述</summary>
    /// <param name="result">実行全体の確定結果</param>
    /// <returns>mutation-report.json と timings.json の run の欄</returns>
    public static object Describe(MutationRunResult result)
    {
        var causes = result.Completed.SelectMany(t => t.Verdicts.Values).OfType<MutantVerdict.Unjudged>().Select(v => v.Cause).ToArray();
        return new
        {
            state = result.State,
            complete = result.IsComplete,
            unjudged = causes.Length,
            unjudgedBy = new
            {
                notRun = causes.OfType<UnjudgedCause.NotRun>().Count(),
                interrupted = causes.OfType<UnjudgedCause.Interrupted>().Count(),
                contendedTimeout = causes.OfType<UnjudgedCause.ContendedTimeout>().Count(),
            },
            interruptedTargets = result
                .Failures.Where(failure => failure.Failure is PipelineFailure.Interrupted)
                .Select(failure => failure.Name)
                .ToArray(),
        };
    }

    /// <summary>判定を確定できなかった理由の、報告の statusReason への記述</summary>
    /// <param name="cause">確定できなかった理由</param>
    /// <returns>変異の statusReason に書く英文</returns>
    public static string Reason(UnjudgedCause cause) =>
        cause switch
        {
            UnjudgedCause.NotRun => "Unjudged: the run stopped before this mutant was tested",
            UnjudgedCause.Interrupted => "Unjudged: the run stopped while this mutant was being tested",
            UnjudgedCause.ContendedTimeout contended => string.Create(
                CultureInfo.InvariantCulture,
                $"Unjudged: the tests timed out while the machine was too slow to trust the timeout ({Control(contended)} against a {contended.BudgetMs:F0} ms budget)"
            ),
        };

    /// <summary>対照の実行の結果の記述</summary>
    private static string Control(UnjudgedCause.ContendedTimeout contended) =>
        contended.ControlMs is { } ms
            ? string.Create(CultureInfo.InvariantCulture, $"an unmutated control run took {ms:F0} ms")
            : "an unmutated control run did not finish";
}
