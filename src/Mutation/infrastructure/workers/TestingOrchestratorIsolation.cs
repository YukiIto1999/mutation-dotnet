using Mutation.Domain;

namespace Mutation.Infrastructure.Workers;

/// <summary>隔離 process での実行と、期限を超えた実行の確かめの記録</summary>
public sealed partial class TestingOrchestrator
{
    /// <summary>応答を得られなかった共有実行の記録。期限切れだけは、競合による遅れかを確かめてから記録する</summary>
    private async Task RecordLostRunAsync(
        MutantPlan plan,
        WorkerExchange exchange,
        MutantVerdict settled,
        double elapsedMs,
        RunTally tally,
        CancellationToken cancellation
    )
    {
        if (!exchange.DeadlineExpired)
        {
            tally.Record(plan, settled, 0, elapsedMs, isolated: false, Row(plan.Execution.ResidentSlice.TestOrder, exchange.Response));
            return;
        }

        var outcome = await adjudication.AdjudicateAsync(plan, cancellation).ConfigureAwait(false);
        RecordIsolated(plan, outcome, tally, cancellation);
    }

    /// <summary>隔離 process での一計画の実行と記録。期限を超えたら競合による遅れかを確かめる</summary>
    private async Task RunIsolatedAsync(MutantPlan plan, RunTally tally, CancellationToken cancellation)
    {
        var outcome = await isolationRuns.RunAsync(plan, cancellation).ConfigureAwait(false);
        if (outcome.DeadlineExpired && !cancellation.IsCancellationRequested)
        {
            outcome = await adjudication.AdjudicateAsync(plan, cancellation).ConfigureAwait(false);
        }

        RecordIsolated(plan, outcome, tally, cancellation);
    }

    /// <summary>隔離側の結果の記録。取り消しを受けたら結果を採らない</summary>
    private void RecordIsolated(MutantPlan plan, IsolationOutcome outcome, RunTally tally, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            tally.Abandon(plan, new UnjudgedCause.Interrupted());
            return;
        }

        if (sharedKills.TryRemove(plan.MutantId, out var kill))
        {
            tally.Record(
                plan,
                kill.Verdict,
                kill.ExecutedTests + outcome.ExecutedTests,
                kill.ElapsedMs + outcome.ElapsedMs,
                isolated: true,
                DetectionRows.Merge(kill.Row, outcome.Detection!)
            );
            return;
        }

        tally.Record(plan, outcome.Verdict, outcome.ExecutedTests, outcome.ElapsedMs, isolated: true, outcome.Detection);
    }
}
