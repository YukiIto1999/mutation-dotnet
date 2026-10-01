using Mutation.Composition;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Cli.Commands;

/// <summary>変更ごとの検査として、変わった行に重なる未検出の変異を判定する command</summary>
public static class ChangedLinesCommand
{
    /// <summary>`run --since` の報告からの、変わった行に重なる変異の取り出しと未検出の有無の判定</summary>
    /// <param name="report">-r, `run` が書き出した mutation-report.json の path</param>
    /// <param name="since">差分の基点になる git の参照。`run` の `--since` に渡したものと同じ値</param>
    /// <returns>未検出が無ければ 0、報告か差分を得られなければ 1、未検出があれば 2</returns>
    public static async Task<int> Run(string report, string since)
    {
        var outcome = BuildCore.Create().OnChangedLines(Path.GetFullPath(report), since);
        if (outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(var failure))
        {
            await Console.Error.WriteLineAsync(FailureLines.Describe(failure)).ConfigureAwait(false);
            return 1;
        }

        var verdicts = ((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)outcome).Value;
        await Console.Out.WriteAsync(ChangedLineSummary.Render(verdicts, Environment.CurrentDirectory)).ConfigureAwait(false);
        return verdicts.Undetected.Count == 0 ? 0 : 2;
    }
}
