

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

    /// <summary>応答がなく process が生きていれば TimedOut になること</summary>
    [Test]
    public async Task Missing_response_with_live_worker_is_timed_out()
    {
        var verdict = VerdictInterpretation.Settle(new WorkerExchange(null, []), workerExited: false);
        await Assert.That(verdict is MutantVerdict.TimedOut).IsTrue();
    }

    /// <summary>失敗応答は TimedOut として作り直しに回ること</summary>
    [Test]
    public async Task Failed_response_is_timed_out()
    {
        var exchange = new WorkerExchange(new WorkerResponse.Failed("adapter error"), []);
        await Assert.That(VerdictInterpretation.Settle(exchange, workerExited: false) is MutantVerdict.TimedOut).IsTrue();
    }
}
