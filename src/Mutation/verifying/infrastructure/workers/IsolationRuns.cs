using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Diagnostics;
using System.Globalization;

using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>環境変数で事前活性化した新規 process での変異一件の検査</summary>
/// <param name="launch">worker の起動情報</param>
/// <param name="roster">連番順のテストの名簿</param>
/// <param name="detectionMatrix">変異ごとに全テストを実行し検出行列の行を残すか</param>
public sealed class IsolationRuns(WorkerLaunchPlan launch, TestRoster roster, bool detectionMatrix) : IDisposable
{
    // 隔離 process はテスト実行体が内部で全並列に走るため、lane 数のまま重ねると CPU を奪い合う
    private const int ConcurrentIsolations = 2;

    /// <summary>隔離実行の同時本数の絞り</summary>
    private readonly SemaphoreSlim gate = new(ConcurrentIsolations, ConcurrentIsolations);

    /// <summary>一計画の隔離実行。同時実行は絞られる形</summary>
    /// <remarks>取り消しを受けたら、待っていた実行には取りかからず、実行中の process は止める。その結果は採らない</remarks>
    /// <param name="plan">実行する変異計画</param>
    /// <param name="cancellation">取り消しの要求</param>
    /// <returns>確定結果と実行の記録</returns>
    public async Task<IsolationOutcome> RunAsync(MutantPlan plan, CancellationToken cancellation)
    {
        try
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Interrupted(plan.Execution.IsolatedSlice.TestOrder);
        }

        try
        {
            return await RunOnceAsync(plan, cancellation).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    /// <summary>新規 worker の起動から判定確定までの一巡</summary>
    private async Task<IsolationOutcome> RunOnceAsync(MutantPlan plan, CancellationToken cancellation)
    {
        var (order, budget) = plan.Execution.IsolatedSlice;
        var stopwatch = Stopwatch.StartNew();
        var environment = new Dictionary<string, string>
        {
            [InjectionContract.ActiveEnvironmentVariable] = plan.MutantId.Value.ToString(CultureInfo.InvariantCulture),
            [InjectionContract.HitLimitEnvironmentVariable] = HitLimits.For(roster, order).ToString(CultureInfo.InvariantCulture),
        };
        using var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory, environment);
        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            return new IsolationOutcome(
                new MutantVerdict.Killed("(initialization failure)"),
                0,
                stopwatch.Elapsed.TotalMilliseconds,
                Row(order, null)
            );
        }

        var request = new WorkerRequest.Run(plan.MutantId.Value, order.Select(roster.Id).ToArray(), AllFailures: detectionMatrix);
        var exchange = await client.SendAsync(request, VerdictInterpretation.Deadline(budget), cancellation).ConfigureAwait(false);
        var verdict = VerdictInterpretation.Settle(exchange, client.HasExited);
        var executed = exchange.Response is WorkerResponse.RunCompleted completed
            ? completed.ExecutedTests ?? order.Count
            : 0;
        return new IsolationOutcome(verdict, executed, stopwatch.Elapsed.TotalMilliseconds, Row(order, exchange.Response));
    }

    /// <summary>取り消しで実行に至らなかった実行の記録</summary>
    private IsolationOutcome Interrupted(IReadOnlyList<TestIndex> order) =>
        new(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()), 0, 0, Row(order, null));

    /// <summary>検出行列を求める実行での run の応答からの行</summary>
    private DetectionRow? Row(IReadOnlyList<TestIndex> order, WorkerResponse? response) =>
        detectionMatrix ? DetectionRows.From(order, roster, response) : null;
}

/// <summary>隔離実行一件の確定結果と実行の記録</summary>
/// <param name="Verdict">変異の確定結果</param>
/// <param name="ExecutedTests">実行を始めたテスト数</param>
/// <param name="ElapsedMs">起動から確定までの所要時間</param>
/// <param name="Detection">検出行列を求めた実行での検出行列の行</param>
public sealed record IsolationOutcome(MutantVerdict Verdict, int ExecutedTests, double ElapsedMs, DetectionRow? Detection);
