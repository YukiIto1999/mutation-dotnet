using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;

using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker 群への変異計画の分配と判定の回収</summary>
public sealed class TestingOrchestrator : IDisposable
{
    /// <summary>worker の起動情報</summary>
    private readonly WorkerLaunchPlan launch;

    /// <summary>連番順のテストの名簿</summary>
    private readonly TestRoster roster;

    /// <summary>進行を伝える通知先</summary>
    private readonly Action<RunProgress> progress;

    /// <summary>隔離 process での実行係</summary>
    private readonly IsolationRuns isolationRuns;

    /// <summary>分配に要る情報を束ねる構築</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="progress">進行を伝える通知先</param>
    public TestingOrchestrator(WorkerLaunchPlan launch, TestRoster roster, Action<RunProgress> progress)
    {
        this.launch = launch;
        this.roster = roster;
        this.progress = progress;
        isolationRuns = new IsolationRuns(launch, roster);
    }

    /// <inheritdoc />
    public void Dispose() => isolationRuns.Dispose();

    /// <summary>全計画の実行と、この呼び出し分に閉じた判定の集計</summary>
    /// <param name="plans">実行する変異計画の列</param>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <returns>変異ごとの判定と実行の記録</returns>
    public async Task<OrchestrationResult> ExecuteAsync(IReadOnlyList<MutantPlan> plans, int concurrency)
    {
        var shared = new ConcurrentQueue<MutantPlan>(plans.Where(p => p.Execution is not MutantExecution.Isolated));
        var isolated = new ConcurrentQueue<MutantPlan>(plans.Where(p => p.Execution is MutantExecution.Isolated));
        var tally = new RunTally(plans.Count, progress);
        progress(new RunProgress.ExecutionStarted(shared.Count, isolated.Count, Math.Max(1, concurrency)));
        var lanes = Enumerable
            .Range(0, Math.Max(1, concurrency))
            .Select(_ => Task.Run(() => LaneAsync(shared, isolated, tally)))
            .ToArray();
        await Task.WhenAll(lanes).ConfigureAwait(false);
        return tally.Result();
    }

    /// <summary>一 lane の実行。共有ホストの計画を出し切ってからの隔離の計画の消化</summary>
    private async Task LaneAsync(
        ConcurrentQueue<MutantPlan> shared,
        ConcurrentQueue<MutantPlan> isolated,
        RunTally tally
    )
    {
        WorkerClient? client = null;
        while (shared.TryDequeue(out var plan))
        {
            client ??= await StartSharedAsync().ConfigureAwait(false);
            if (client is null)
            {
                tally.Record(plan, new MutantVerdict.TimedOut(), 0, 0, isolated: false);
                continue;
            }

            client = await RunSharedAsync(client, plan, isolated, tally).ConfigureAwait(false);
        }

        client?.Dispose();
        while (isolated.TryDequeue(out var plan))
        {
            var outcome = await isolationRuns.RunAsync(plan).ConfigureAwait(false);
            tally.Record(plan, outcome.Verdict, outcome.ExecutedTests, outcome.ElapsedMs, isolated: true);
        }
    }

    /// <summary>共有ホスト worker の起動と初期化。失敗なら不在</summary>
    private async Task<WorkerClient?> StartSharedAsync()
    {
        var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory);
        if (client is null)
        {
            return null;
        }

        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            client.Dispose();
            return null;
        }

        return client;
    }

    /// <summary>共有ホストでの一計画の実行と、時間切れ時の worker の作り直し</summary>
    private async Task<WorkerClient?> RunSharedAsync(
        WorkerClient client,
        MutantPlan plan,
        ConcurrentQueue<MutantPlan> isolated,
        RunTally tally
    )
    {
        var (order, budget) = plan.Execution.ResidentSlice;
        var stopwatch = Stopwatch.StartNew();
        var request = new WorkerRequest.Run(plan.MutantId.Value, order.Select(roster.Id).ToArray(), HitLimits.For(roster, order));
        var exchange = await client.SendAsync(request, VerdictInterpretation.Deadline(budget)).ConfigureAwait(false);
        var settled = VerdictInterpretation.Settle(exchange, client.HasExited);
        if (exchange.Response is not WorkerResponse.RunCompleted completed)
        {
            tally.Record(plan, settled, 0, stopwatch.Elapsed.TotalMilliseconds, isolated: false);
            client.Dispose();
            tally.CountRestart();
            return await StartSharedAsync().ConfigureAwait(false);
        }

        if (settled is MutantVerdict.Survived && plan.Execution is MutantExecution.ResidentThenIsolated)
        {
            isolated.Enqueue(plan with { Execution = plan.Execution.ToIsolated() });
            return client;
        }

        tally.Record(plan, settled, completed.ExecutedTests ?? order.Count, stopwatch.Elapsed.TotalMilliseconds, isolated: false);
        return client;
    }
}
