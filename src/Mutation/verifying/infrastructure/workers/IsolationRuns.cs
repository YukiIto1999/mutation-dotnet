using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Diagnostics;
using System.Globalization;

using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>環境変数で事前活性化した新規 process での変異一件の検査</summary>
/// <param name="launch">worker の起動情報</param>
/// <param name="roster">連番順のテストの名簿</param>
public sealed class IsolationRuns(WorkerLaunchPlan launch, TestRoster roster) : IDisposable
{
    // 隔離 process はテスト実行体が内部で全並列に走るため、lane 数のまま重ねると CPU を奪い合う
    private const int ConcurrentIsolations = 2;

    /// <summary>隔離実行の同時本数の絞り</summary>
    private readonly SemaphoreSlim gate = new(ConcurrentIsolations, ConcurrentIsolations);

    /// <summary>一計画の隔離実行。同時実行は絞られる形</summary>
    /// <param name="plan">実行する変異計画</param>
    /// <returns>確定結果と実行の記録</returns>
    public async Task<IsolationOutcome> RunAsync(MutantPlan plan)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RunOnceAsync(plan).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    /// <summary>新規 worker の起動から判定確定までの一巡</summary>
    private async Task<IsolationOutcome> RunOnceAsync(MutantPlan plan)
    {
        var (order, budget) = plan.Execution.IsolatedSlice;
        var stopwatch = Stopwatch.StartNew();
        var environment = new Dictionary<string, string>
        {
            [InjectionContract.ActiveEnvironmentVariable] = plan.MutantId.Value.ToString(CultureInfo.InvariantCulture),
            [InjectionContract.HitLimitEnvironmentVariable] = HitLimits.For(roster, order).ToString(CultureInfo.InvariantCulture),
        };
        using var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory, environment);
        if (client is null)
        {
            return new IsolationOutcome(new MutantVerdict.TimedOut(), 0, stopwatch.Elapsed.TotalMilliseconds);
        }

        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            return new IsolationOutcome(
                new MutantVerdict.Killed("(initialization failure)"),
                0,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }

        var request = new WorkerRequest.Run(plan.MutantId.Value, order.Select(roster.Id).ToArray());
        var exchange = await client.SendAsync(request, VerdictInterpretation.Deadline(budget)).ConfigureAwait(false);
        var verdict = VerdictInterpretation.Settle(exchange, client.HasExited);
        var executed = exchange.Response is WorkerResponse.RunCompleted completed
            ? completed.ExecutedTests ?? order.Count
            : 0;
        return new IsolationOutcome(verdict, executed, stopwatch.Elapsed.TotalMilliseconds);
    }
}

/// <summary>隔離実行一件の確定結果と実行の記録</summary>
/// <param name="Verdict">変異の確定結果</param>
/// <param name="ExecutedTests">実行を始めたテスト数</param>
/// <param name="ElapsedMs">起動から確定までの所要時間</param>
public sealed record IsolationOutcome(MutantVerdict Verdict, int ExecutedTests, double ElapsedMs);
