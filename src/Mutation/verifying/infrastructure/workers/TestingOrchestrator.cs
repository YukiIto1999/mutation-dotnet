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

    /// <summary>共有ホストの初期化と継続利用の可否の判定</summary>
    /// <returns>初期化済みの worker または初期化失敗時の不在</returns>
    private async Task<WorkerClient?> StartSharedAsync()
    {
        var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory);
        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            client.Dispose();
            return null;
        }

        return client;
    }

    /// <summary>共有実行後の worker の継続可否の決定</summary>
    /// <param name="client">指示を送る共有 worker</param>
    /// <param name="plan">共有実行する変異とテスト列の計画</param>
    /// <param name="isolated">新規 process で再判定する計画の追加先</param>
    /// <param name="tally">確定した判定と worker 再起動の集計先</param>
    /// <returns>後続の共有実行に使う worker または再起動が必要な場合の不在</returns>
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

        if (IsUnreached(completed, settled))
        {
            isolated.Enqueue(plan with { Execution = plan.Execution.ToIsolated() });
            if (settled is not MutantVerdict.Killed)
            {
                return client;
            }

            client.Dispose();
            tally.CountRestart();
            return null;
        }

        if (settled is MutantVerdict.Survived && plan.Execution is MutantExecution.ResidentThenIsolated)
        {
            isolated.Enqueue(plan with { Execution = plan.Execution.ToIsolated() });
            return client;
        }

        tally.Record(plan, settled, completed.ExecutedTests ?? order.Count, stopwatch.Elapsed.TotalMilliseconds, isolated: false);
        return client;
    }

    /// <summary>活性の変異の枝へ一度も入らずに出た生存か検出かの判定</summary>
    /// <remarks>先行する検査が process 内に残した状態(memo、遅延初期化、static cache)が変異の経路を飛ばすか、汚れた状態がテストを落とした疑いがあり、新規 process で確かめ直す</remarks>
    private static bool IsUnreached(WorkerResponse.RunCompleted completed, MutantVerdict settled) =>
        completed.Reached == false && settled is MutantVerdict.Survived or MutantVerdict.Killed;
}
