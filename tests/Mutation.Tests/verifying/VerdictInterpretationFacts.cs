

using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Workers;
using Mutation.Protocol;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>worker とのやり取りの結末解釈の検査</summary>
public sealed class VerdictInterpretationFacts
{
    /// <summary>killed の応答が検出テスト名付きの Killed になること</summary>
    [Test]
    public async Task Killed_outcome_carries_killer_test()
    {
        var exchange = new WorkerExchange(
            new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, "CalcTests.Add", 1, 10),
            []
        );
        var verdict = VerdictInterpretation.Settle(exchange, workerExited: false);
        await Assert.That(verdict is MutantVerdict.Killed { KillerTest: "CalcTests.Add" }).IsTrue();
    }

    /// <summary>killer 不明の killed は端末出力から表示名を拾うこと</summary>
    [Test]
    public async Task Killer_name_falls_back_to_terminal_output()
    {
        var exchange = new WorkerExchange(
            new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, null, 1, 10),
            ["  failed CalcTests.Above (12ms)"]
        );
        var verdict = VerdictInterpretation.Settle(exchange, workerExited: false);
        await Assert.That(verdict is MutantVerdict.Killed { KillerTest: "CalcTests.Above" }).IsTrue();
    }

    /// <summary>hitlimit の応答が TimedOut になること</summary>
    [Test]
    public async Task Hit_limit_outcome_becomes_timed_out()
    {
        var exchange = new WorkerExchange(
            new WorkerResponse.RunCompleted(WorkerContract.OutcomeHitLimit, null, 1, 10),
            []
        );
        await Assert.That(VerdictInterpretation.Settle(exchange, workerExited: false) is MutantVerdict.TimedOut).IsTrue();
    }

    /// <summary>応答がなく process が落ちていれば crash の Killed になること</summary>
    [Test]
    public async Task Missing_response_with_exited_worker_is_a_crash_kill()
    {
        var verdict = VerdictInterpretation.Settle(new WorkerExchange(null, []), workerExited: true);
        await Assert.That(verdict is MutantVerdict.Killed { KillerTest: "(process crash)" }).IsTrue();
    }

    /// <summary>期限切れでも取り消しでもなく応答がなく process が生きていれば、検出に数えず runner の失敗の未判定になること</summary>
    [Test]
    public async Task Missing_response_with_live_worker_is_an_unjudged_runner_failure()
    {
        var verdict = VerdictInterpretation.Settle(new WorkerExchange(null, []), workerExited: false);
        await Assert.That(verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
    }

    /// <summary>期限切れで止めた worker が既に終了していても、crash の検出でなく TimedOut になること</summary>
    [Test]
    public async Task Expired_deadline_is_timed_out_even_when_the_killed_worker_has_exited()
    {
        var verdict = VerdictInterpretation.Settle(new WorkerExchange(null, [], DeadlineExpired: true), workerExited: true);
        await Assert.That(verdict is MutantVerdict.TimedOut).IsTrue();
    }

    /// <summary>失敗応答は検出にも時間超過にも数えず、worker が示した理由を持つ runner の失敗の未判定になること</summary>
    [Test]
    public async Task Failed_response_is_an_unjudged_runner_failure_carrying_the_reason()
    {
        var exchange = new WorkerExchange(new WorkerResponse.Failed("adapter error"), []);
        var verdict = VerdictInterpretation.Settle(exchange, workerExited: false);
        await Assert.That(verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure { Detail: var detail } } && detail.Contains("adapter error", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>失敗応答を返した後に worker が終了していても、変異による crash の検出でなく runner の失敗になること</summary>
    [Test]
    public async Task Failed_response_stays_a_runner_failure_when_the_worker_has_exited()
    {
        var exchange = new WorkerExchange(new WorkerResponse.Failed("adapter error"), []);
        await Assert.That(VerdictInterpretation.Settle(exchange, workerExited: true) is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
    }

    /// <summary>run への応答として想定外の種類が返ったら、検出に数えず runner の失敗になること</summary>
    [Test]
    public async Task Unexpected_response_kind_is_an_unjudged_runner_failure()
    {
        var verdict = VerdictInterpretation.Settle(new WorkerExchange(new WorkerResponse.Opened(), []), workerExited: false);
        await Assert.That(verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure }).IsTrue();
    }

    /// <summary>init の失敗の理由が、worker の失敗応答、想定外の応答、期限切れ、終了の別に残ること</summary>
    [Test]
    public async Task Init_failure_names_why_the_host_did_not_open()
    {
        var failed = VerdictInterpretation.InitFailure(new WorkerExchange(new WorkerResponse.Failed("no host"), []));
        var unexpected = VerdictInterpretation.InitFailure(new WorkerExchange(new WorkerResponse.Done(), []));
        var expired = VerdictInterpretation.InitFailure(new WorkerExchange(null, [], DeadlineExpired: true));
        var exited = VerdictInterpretation.InitFailure(new WorkerExchange(null, []));

        await Assert.That(failed.Detail).IsEqualTo("the test host did not open: no host");
        await Assert.That(unexpected.Detail).Contains("Done");
        await Assert.That(expired.Detail).Contains("deadline");
        await Assert.That(exited.Detail).Contains("exited");
    }

    /// <summary>worker が約束した生存の値が Survived になること</summary>
    [Test]
    public async Task Survived_outcome_becomes_survived()
    {
        var exchange = new WorkerExchange(new WorkerResponse.RunCompleted(WorkerContract.OutcomeSurvived, null, 1, 10), []);
        await Assert.That(VerdictInterpretation.Settle(exchange, workerExited: false) is MutantVerdict.Survived).IsTrue();
    }

    /// <summary>未知の結末の値を、生存と見なさず、値を示す runner の失敗の未判定にすること</summary>
    [Test]
    public async Task Unknown_outcome_is_an_unjudged_runner_failure_not_a_survival()
    {
        var exchange = new WorkerExchange(new WorkerResponse.RunCompleted("surviveed", null, 1, 10), []);
        var verdict = VerdictInterpretation.Settle(exchange, workerExited: false);
        await Assert.That(verdict is MutantVerdict.Unjudged { Cause: UnjudgedCause.RunnerFailure { Detail: var detail } } && detail.Contains("surviveed", StringComparison.Ordinal)).IsTrue();
    }
}
