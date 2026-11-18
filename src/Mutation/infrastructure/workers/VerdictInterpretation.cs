
using Mutation.Domain;
using Mutation.Protocol;

namespace Mutation.Infrastructure.Workers;

/// <summary>worker とのやり取りの結末から確定結果への変換</summary>
public static class VerdictInterpretation
{
    /// <summary>run のやり取り全体からの確定結果の導出</summary>
    /// <remarks>
    /// 検出に数えるのは、テストが失敗した結末と、probe 上限および期限という時間予算の超過と、変異で落ちた worker process の終了だけである
    /// worker が失敗を返した場合と応答を得られなかった場合は、テストが変異を検出した事実がないので判定を確定しない
    /// </remarks>
    /// <param name="exchange">run 指示の応答と worker の出力</param>
    /// <param name="workerExited">worker process が終了していたか</param>
    /// <returns>期限切れなら TimedOut、応答があれば結末の解釈、crash なら Killed、それ以外の応答の欠落と失敗は RunnerFailure の未判定</returns>
    public static MutantVerdict Settle(WorkerExchange exchange, bool workerExited) =>
        exchange switch
        {
            { DeadlineExpired: true } => new MutantVerdict.TimedOut(),
            { Response: WorkerResponse.RunCompleted completed } => Interpret(
                completed.Outcome,
                completed.KillerTest ?? TerminalOutput.KillerTest(exchange.Output)
            ),
            { Response: { } response } => RunnerFailed($"the worker answered the run with {Describe(response)}"),
            { Response: null } when workerExited => new MutantVerdict.Killed("(process crash)"),
            _ => RunnerFailed("the worker gave no answer to the run"),
        };

    /// <summary>run の結末の値と検出テスト名からの確定結果の導出。worker が約束した三つの値以外は、生存と見なさず runner の失敗にする</summary>
    /// <param name="outcome">worker が返した結末の値</param>
    /// <param name="killerTest">最初に失敗したテストの表示名</param>
    /// <returns>結末に対応する確定結果。未知の値は runner の失敗の未判定</returns>
    public static MutantVerdict Interpret(string outcome, string? killerTest) =>
        outcome switch
        {
            WorkerContract.OutcomeKilled => new MutantVerdict.Killed(killerTest ?? "(unknown test)"),
            WorkerContract.OutcomeHitLimit => new MutantVerdict.TimedOut(),
            WorkerContract.OutcomeSurvived => new MutantVerdict.Survived(),
            _ => RunnerFailed($"the worker reported an unknown run outcome '{outcome}'"),
        };

    /// <summary>init 指示のやり取りが worker を開けなかった理由の導出</summary>
    /// <param name="init">init 指示の応答と worker の出力</param>
    /// <returns>変異の判定を確かめられなかった理由</returns>
    public static UnjudgedCause.RunnerFailure InitFailure(WorkerExchange init) =>
        init switch
        {
            { Response: WorkerResponse.Failed failed } => new($"the test host did not open: {failed.Error}"),
            { Response: { } response } => new($"the worker answered init with {Describe(response)}"),
            { DeadlineExpired: true } => new("the test host did not open before its deadline"),
            _ => new("the worker exited before the test host opened"),
        };

    /// <summary>判定を確かめられなかった変異の確定結果の組み立て</summary>
    private static MutantVerdict.Unjudged RunnerFailed(string detail) =>
        new MutantVerdict.Unjudged(new UnjudgedCause.RunnerFailure(detail));

    /// <summary>run または init への想定外の応答の記述。失敗の応答は worker が示した理由を使う</summary>
    private static string Describe(WorkerResponse response) =>
        response is WorkerResponse.Failed failed ? $"a failure: {failed.Error}" : $"an unexpected {response.GetType().Name} response";
}
