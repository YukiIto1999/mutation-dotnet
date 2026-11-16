using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;

using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker 群への変異計画の分配と判定の回収</summary>
public sealed partial class TestingOrchestrator : IDisposable
{
    /// <summary>worker の起動情報</summary>
    private readonly WorkerLaunchPlan launch;

    /// <summary>連番順のテストの名簿</summary>
    private readonly TestRoster roster;

    /// <summary>進行を伝える通知先</summary>
    private readonly Action<RunProgress> progress;

    /// <summary>隔離 process での実行係</summary>
    private readonly IsolationRuns isolationRuns;

    /// <summary>変異ごとに全テストを実行し検出行列の行を残すか</summary>
    private readonly bool detectionMatrix;

    /// <summary>run 応答を待つ上限の決め方</summary>
    private readonly RunDeadlines deadlines;

    /// <summary>期限を超えた変異の、検出か競合による遅れかの確かめ</summary>
    private readonly TimeoutAdjudication adjudication;

    /// <summary>検出行列を求める実行で、共有側の検出を隔離側の行と合わせて確定するまで預ける変異ごとの結果</summary>
    private readonly ConcurrentDictionary<MutantId, SharedKill> sharedKills = new();

    /// <summary>分配に要る情報を束ねる構築</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <param name="detectionMatrix">変異ごとに全テストを実行し検出行列の行を残すか</param>
    /// <param name="deadlines">run 応答を待つ上限の決め方</param>
    public TestingOrchestrator(
        WorkerLaunchPlan launch,
        TestRoster roster,
        Action<RunProgress> progress,
        bool detectionMatrix,
        RunDeadlines deadlines
    )
    {
        this.launch = launch;
        this.roster = roster;
        this.progress = progress;
        this.detectionMatrix = detectionMatrix;
        this.deadlines = deadlines;
        isolationRuns = new IsolationRuns(launch, roster, detectionMatrix, deadlines);
        adjudication = new TimeoutAdjudication(isolationRuns, roster, detectionMatrix);
    }

    /// <inheritdoc />
    public void Dispose() => isolationRuns.Dispose();

    /// <summary>全計画の実行と、この呼び出し分に閉じた判定の集計</summary>
    /// <remarks>取り消しの要求を受けたら、実行中の変異は結果を採らず、残りの変異には取りかからない</remarks>
    /// <param name="plans">実行する変異計画の列</param>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <param name="control">判定が確定するたびの通知先と取り消しの要求</param>
    /// <returns>変異ごとの判定と実行の記録。判定を確定できなかった変異は未判定</returns>
    public async Task<OrchestrationResult> ExecuteAsync(
        IReadOnlyList<MutantPlan> plans,
        int concurrency,
        ExecutionControl control
    )
    {
        var shared = new ConcurrentQueue<MutantPlan>(plans.Where(p => p.Execution is not MutantExecution.Isolated));
        var isolated = new ConcurrentQueue<MutantPlan>(plans.Where(p => p.Execution is MutantExecution.Isolated));
        var tally = new RunTally(plans.Count, progress, control.Settled);
        progress(new RunProgress.ExecutionStarted(shared.Count, isolated.Count, Math.Max(1, concurrency)));
        var lanes = Enumerable
            .Range(0, Math.Max(1, concurrency))
            .Select(_ => Task.Run(() => LaneAsync(shared, isolated, tally, control.Cancellation), CancellationToken.None))
            .ToArray();
        await Task.WhenAll(lanes).ConfigureAwait(false);
        tally.AbandonAll([.. shared, .. isolated]);
        return tally.Result();
    }

    /// <summary>一 lane の実行。共有ホストの計画を出し切ってからの隔離の計画の消化</summary>
    private async Task LaneAsync(
        ConcurrentQueue<MutantPlan> shared,
        ConcurrentQueue<MutantPlan> isolated,
        RunTally tally,
        CancellationToken cancellation
    )
    {
        WorkerClient? client = null;
        while (!cancellation.IsCancellationRequested && shared.TryDequeue(out var plan))
        {
            client ??= await StartSharedAsync(cancellation).ConfigureAwait(false);
            if (client is null)
            {
                RecordStartFailure(plan, tally, cancellation);
                continue;
            }

            client = await RunSharedAsync(client, plan, isolated, tally, cancellation).ConfigureAwait(false);
        }

        client?.Dispose();
        while (!cancellation.IsCancellationRequested && isolated.TryDequeue(out var plan))
        {
            await RunIsolatedAsync(plan, tally, cancellation).ConfigureAwait(false);
        }
    }

    /// <summary>共有ホストの初期化と継続利用の可否の判定</summary>
    /// <returns>初期化済みの worker または初期化失敗時の不在</returns>
    private async Task<WorkerClient?> StartSharedAsync(CancellationToken cancellation)
    {
        var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory);
        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            client.Dispose();
            return null;
        }

        return client;
    }

    /// <summary>共有ホストを開けなかった計画の記録。取り消しで開けなかったときは結果を採らない</summary>
    private void RecordStartFailure(MutantPlan plan, RunTally tally, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested)
        {
            tally.Abandon(plan, new UnjudgedCause.Interrupted());
            return;
        }

        tally.Record(plan, new MutantVerdict.TimedOut(), 0, 0, isolated: false, Row(plan.Execution.ResidentSlice.TestOrder, null));
    }

    /// <summary>共有実行後の worker の継続可否の決定</summary>
    /// <param name="client">指示を送る共有 worker</param>
    /// <param name="plan">共有実行する変異とテスト列の計画</param>
    /// <param name="isolated">新規 process で再判定する計画の追加先</param>
    /// <param name="tally">確定した判定と worker 再起動の集計先</param>
    /// <param name="cancellation">取り消しの要求</param>
    /// <returns>後続の共有実行に使う worker または再起動が必要な場合の不在</returns>
    private async Task<WorkerClient?> RunSharedAsync(
        WorkerClient client,
        MutantPlan plan,
        ConcurrentQueue<MutantPlan> isolated,
        RunTally tally,
        CancellationToken cancellation
    )
    {
        var (order, budget) = plan.Execution.ResidentSlice;
        var stopwatch = Stopwatch.StartNew();
        var request = new WorkerRequest.Run(
            plan.MutantId.Value,
            order.Select(roster.Id).ToArray(),
            HitLimits.For(roster, order),
            detectionMatrix
        );
        var exchange = await client.SendAsync(request, deadlines.For(budget), cancellation).ConfigureAwait(false);
        if (cancellation.IsCancellationRequested)
        {
            client.Dispose();
            tally.Abandon(plan, new UnjudgedCause.Interrupted());
            return null;
        }

        var settled = VerdictInterpretation.Settle(exchange, client.HasExited);
        if (exchange.Response is not WorkerResponse.RunCompleted completed)
        {
            client.Dispose();
            tally.CountRestart();
            await RecordLostRunAsync(plan, exchange, settled, stopwatch.Elapsed.TotalMilliseconds, tally, cancellation).ConfigureAwait(false);
            return null;
        }

        return SettleCompleted(client, plan, completed, settled, stopwatch.Elapsed.TotalMilliseconds, isolated, tally);
    }

    /// <summary>応答を得た共有実行の、確定か再判定への回しか、worker の継続可否の決定</summary>
    private WorkerClient? SettleCompleted(
        WorkerClient client,
        MutantPlan plan,
        WorkerResponse.RunCompleted completed,
        MutantVerdict settled,
        double elapsedMs,
        ConcurrentQueue<MutantPlan> isolated,
        RunTally tally
    )
    {
        var order = plan.Execution.ResidentSlice.TestOrder;
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

        // 行列は検出の有無でなく検出する全テストを求めるため、共有側の検出で隔離側のテストを省かない
        if (detectionMatrix && settled is MutantVerdict.Killed && plan.Execution is MutantExecution.ResidentThenIsolated)
        {
            sharedKills[plan.MutantId] = new SharedKill(
                settled,
                DetectionRows.From(order, roster, completed),
                completed.ExecutedTests ?? order.Count,
                elapsedMs
            );
            isolated.Enqueue(plan with { Execution = plan.Execution.ToIsolated() });
            return client;
        }

        tally.Record(plan, settled, completed.ExecutedTests ?? order.Count, elapsedMs, isolated: false, Row(order, completed));
        return client;
    }

    /// <summary>検出行列を求める実行での run の応答からの行</summary>
    private DetectionRow? Row(IReadOnlyList<TestIndex> order, WorkerResponse? response) =>
        detectionMatrix ? DetectionRows.From(order, roster, response) : null;

    /// <summary>活性の変異の枝へ一度も入らずに出た生存か検出かの判定</summary>
    /// <remarks>先行する検査が process 内に残した状態(memo、遅延初期化、static cache)が変異の経路を飛ばすか、汚れた状態がテストを落とした疑いがあり、新規 process で確かめ直す</remarks>
    private static bool IsUnreached(WorkerResponse.RunCompleted completed, MutantVerdict settled) =>
        completed.Reached == false && settled is MutantVerdict.Survived or MutantVerdict.Killed;
}

/// <summary>隔離側の行と合わせるまで預ける共有側の検出</summary>
/// <param name="Verdict">共有側で確定した検出</param>
/// <param name="Row">共有側の検出行列の行</param>
/// <param name="ExecutedTests">共有側で実行を始めたテスト数</param>
/// <param name="ElapsedMs">共有側の所要時間</param>
internal sealed record SharedKill(MutantVerdict Verdict, DetectionRow Row, int ExecutedTests, double ElapsedMs);
