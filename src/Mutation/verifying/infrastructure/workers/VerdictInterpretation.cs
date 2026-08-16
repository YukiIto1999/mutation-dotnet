
using Mutation.Verifying.Domain;
using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker とのやり取りの結末から確定結果への変換</summary>
public static class VerdictInterpretation
{
    /// <summary>応答往復に許す猶予</summary>
    private const double TimeoutGraceMs = 5000.0;

    /// <summary>run 応答を待つ上限。実行時間予算に応答往復の猶予を足した値</summary>
    /// <param name="budgetMs">変異一件の実行時間予算</param>
    /// <returns>これを超えたら worker を時間切れとみなす上限</returns>
    public static TimeSpan Deadline(double budgetMs) => TimeSpan.FromMilliseconds(budgetMs + TimeoutGraceMs);

    /// <summary>run のやり取り全体からの確定結果の導出</summary>
    /// <param name="exchange">run 指示の応答と worker の出力</param>
    /// <param name="workerExited">worker process が終了していたか</param>
    /// <returns>応答があれば結末の解釈、crash なら Killed、時間切れなら TimedOut</returns>
    public static MutantVerdict Settle(WorkerExchange exchange, bool workerExited) =>
        exchange.Response switch
        {
            WorkerResponse.RunCompleted completed => Interpret(
                completed.Outcome,
                completed.KillerTest ?? TerminalOutput.KillerTest(exchange.Output)
            ),
            null when workerExited => new MutantVerdict.Killed("(process crash)"),
            _ => new MutantVerdict.TimedOut(),
        };

    /// <summary>run の結末の値と検出テスト名からの確定結果の導出</summary>
    /// <param name="outcome">worker が返した結末の値</param>
    /// <param name="killerTest">最初に失敗したテストの表示名</param>
    /// <returns>結末に対応する確定結果</returns>
    public static MutantVerdict Interpret(string outcome, string? killerTest) =>
        outcome switch
        {
            WorkerContract.OutcomeKilled => new MutantVerdict.Killed(killerTest ?? "(unknown test)"),
            WorkerContract.OutcomeHitLimit => new MutantVerdict.TimedOut(),
            _ => new MutantVerdict.Survived(),
        };
}
