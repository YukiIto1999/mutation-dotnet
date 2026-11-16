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
/// <param name="deadlines">run 応答を待つ上限の決め方</param>
public sealed class IsolationRuns(
    WorkerLaunchPlan launch,
    TestRoster roster,
    bool detectionMatrix,
    RunDeadlines deadlines
) : IDisposable
{
    // 隔離 process はテスト実行体が内部で全並列に走るため、lane 数のまま重ねると CPU を奪い合う
    private const int ConcurrentIsolations = 2;

    /// <summary>どの変異にも当たらない連番。活性化する変異のない対照の run が指す</summary>
    private const int NoMutant = -1;

    /// <summary>隔離実行の同時本数の絞り</summary>
    private readonly SemaphoreSlim gate = new(ConcurrentIsolations, ConcurrentIsolations);

    /// <summary>一計画の隔離実行。同時実行は絞られる形</summary>
    /// <remarks>取り消しを受けたら、待っていた実行には取りかからず、実行中の process は止める。その結果は採らない</remarks>
    /// <param name="plan">実行する変異計画</param>
    /// <param name="cancellation">取り消しの要求</param>
    /// <returns>確定結果と実行の記録</returns>
    public async Task<IsolationOutcome> RunAsync(MutantPlan plan, CancellationToken cancellation)
    {
        var (order, budget) = plan.Execution.IsolatedSlice;
        var attempt = await AttemptAsync(plan.MutantId.Value, order, budget, detectionMatrix, cancellation).ConfigureAwait(false);
        return attempt is null
            ? new IsolationOutcome(new MutantVerdict.Unjudged(new UnjudgedCause.Interrupted()), 0, 0, Row(order, null))
            : Outcome(attempt, order);
    }

    /// <summary>変異のない同じテスト列を新規 process で走らせる対照の実行。今の機が予算に収まるかを測る</summary>
    /// <remarks>テストが落ちても結果は使わないので、途中で打ち切らず全テストを走らせ、所要時間を実行全体の長さにする</remarks>
    /// <param name="plan">対照にする変異計画。その隔離側のテスト列と予算を使う</param>
    /// <param name="cancellation">取り消しの要求</param>
    /// <returns>実行の所要時間。応答が無ければ不在</returns>
    public async Task<ControlRun> ControlAsync(MutantPlan plan, CancellationToken cancellation)
    {
        var (order, budget) = plan.Execution.IsolatedSlice;
        var attempt = await AttemptAsync(NoMutant, order, budget, allFailures: true, cancellation).ConfigureAwait(false);
        var finished = attempt?.Exchange?.Response is WorkerResponse.RunCompleted;
        return new ControlRun(finished ? attempt!.RunMs : null, attempt?.TotalMs ?? 0);
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    /// <summary>同時本数の絞りを待ってからの、新規 worker での一巡。取り消しで待ちを抜けたら不在</summary>
    private async Task<Attempt?> AttemptAsync(
        int activeId,
        IReadOnlyList<TestIndex> order,
        double budgetMs,
        bool allFailures,
        CancellationToken cancellation
    )
    {
        try
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            return await StartAndRunAsync(activeId, order, budgetMs, allFailures, cancellation).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>新規 worker の起動から run の応答までの一巡</summary>
    private async Task<Attempt> StartAndRunAsync(
        int activeId,
        IReadOnlyList<TestIndex> order,
        double budgetMs,
        bool allFailures,
        CancellationToken cancellation
    )
    {
        var total = Stopwatch.StartNew();
        var environment = new Dictionary<string, string>
        {
            [InjectionContract.ActiveEnvironmentVariable] = activeId.ToString(CultureInfo.InvariantCulture),
            [InjectionContract.HitLimitEnvironmentVariable] = HitLimits.For(roster, order).ToString(CultureInfo.InvariantCulture),
        };
        using var client = WorkerClient.Start(launch.WorkerDllPath, launch.ResultsDirectory, environment);
        var init = await client.SendAsync(WorkerRequests.Init(launch), TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
        if (init.Response is not WorkerResponse.Opened)
        {
            return new Attempt(null, false, total.Elapsed.TotalMilliseconds, 0);
        }

        var request = new WorkerRequest.Run(activeId, order.Select(roster.Id).ToArray(), AllFailures: allFailures);
        var run = Stopwatch.StartNew();
        var exchange = await client.SendAsync(request, deadlines.For(budgetMs), cancellation).ConfigureAwait(false);
        return new Attempt(exchange, client.HasExited, total.Elapsed.TotalMilliseconds, run.Elapsed.TotalMilliseconds);
    }

    /// <summary>一巡の結果からの確定結果と実行の記録</summary>
    private IsolationOutcome Outcome(Attempt attempt, IReadOnlyList<TestIndex> order)
    {
        if (attempt.Exchange is not { } exchange)
        {
            return new IsolationOutcome(new MutantVerdict.Killed("(initialization failure)"), 0, attempt.TotalMs, Row(order, null));
        }

        var executed = exchange.Response is WorkerResponse.RunCompleted completed ? completed.ExecutedTests ?? order.Count : 0;
        return new IsolationOutcome(
            VerdictInterpretation.Settle(exchange, attempt.WorkerExited),
            executed,
            attempt.TotalMs,
            Row(order, exchange.Response),
            exchange.DeadlineExpired
        );
    }

    /// <summary>検出行列を求める実行での run の応答からの行</summary>
    private DetectionRow? Row(IReadOnlyList<TestIndex> order, WorkerResponse? response) =>
        detectionMatrix ? DetectionRows.From(order, roster, response) : null;

    /// <summary>新規 worker での一巡の観測</summary>
    /// <param name="Exchange">run の応答。worker を開けなかったときは不在</param>
    /// <param name="WorkerExited">応答を得た後に worker が終了していたか</param>
    /// <param name="TotalMs">起動から応答までの所要時間</param>
    /// <param name="RunMs">run 指示を送ってから応答までの所要時間</param>
    private sealed record Attempt(WorkerExchange? Exchange, bool WorkerExited, double TotalMs, double RunMs);
}

/// <summary>隔離実行一件の確定結果と実行の記録</summary>
/// <param name="Verdict">変異の確定結果</param>
/// <param name="ExecutedTests">実行を始めたテスト数</param>
/// <param name="ElapsedMs">起動から確定までの所要時間</param>
/// <param name="Detection">検出行列を求めた実行での検出行列の行</param>
/// <param name="DeadlineExpired">応答の期限が尽きて止めたか</param>
public sealed record IsolationOutcome(
    MutantVerdict Verdict,
    int ExecutedTests,
    double ElapsedMs,
    DetectionRow? Detection,
    bool DeadlineExpired = false
);

/// <summary>変異のない対照の実行の観測</summary>
/// <param name="CompletedMs">run の所要時間。応答を得なかったときは不在</param>
/// <param name="ElapsedMs">起動から終わりまでの所要時間</param>
public sealed record ControlRun(double? CompletedMs, double ElapsedMs);
