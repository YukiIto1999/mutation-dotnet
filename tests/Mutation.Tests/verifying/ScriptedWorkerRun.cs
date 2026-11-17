using System.Diagnostics;
using System.Text.Json;
using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using Mutation.Verifying.Infrastructure.Workers;

namespace Mutation.Tests;

/// <summary>台本 worker を使う実行ごとの検査環境</summary>
/// <remarks>
/// test assembly と同梱した Mutation.ScriptedWorker と本物の WorkerClient が通信する構成
/// 共有 worker と隔離 process で異なる結果を返す台本
/// 検出かつ未到達の応答後に後続の run も失敗させる process の汚染状態
/// テスト識別子ごとに設定する baseline の観測
/// 他の検査と状態を共有しない専用の一時 directory
/// </remarks>
internal sealed class ScriptedWorkerRun : IDisposable
{
    /// <summary>台本の worker の assembly 名</summary>
    private const string WorkerAssembly = "Mutation.ScriptedWorker.dll";

    /// <summary>Mutation.ScriptedWorker と共有する台本の file 名</summary>
    private const string ScriptFileName = "script.json";

    /// <summary>Mutation.ScriptedWorker と共有する応答前の待ちの台本の file 名</summary>
    private const string DelayFileName = "delays.json";

    /// <summary>Mutation.ScriptedWorker と共有する、応答の後に書く出力の台本の file 名</summary>
    private const string OutputFileName = "outputs.json";

    /// <summary>台本の待ちに指定する、殺されるまで応えない値</summary>
    public const int NeverAnswers = -1;

    /// <summary>共有の worker が init に答える台本のキー</summary>
    public const string SharedInitKey = "init:shared";

    /// <summary>環境変数で活性化済みの隔離 process が init に答える台本のキー</summary>
    public const string IsolatedInitKey = "init:isolated";

    /// <summary>Mutation.ScriptedWorker と共有する受信記録の file 名</summary>
    private const string JournalFileName = "journal.log";

    /// <summary>終了を命じた worker の process が消えるのを待つ上限</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(10);

    /// <summary>取り消す時点に至るまで worker の指示を待つ上限</summary>
    private static readonly TimeSpan ReachPatience = TimeSpan.FromSeconds(60);

    /// <summary>process の消滅を見る間隔</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>この実行の台本と記録の専用 directory</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"mutation-tests-{Guid.NewGuid():N}");

    /// <summary>worker の起動情報</summary>
    private readonly WorkerLaunchPlan launch;

    /// <summary>run 応答を待つ上限の決め方。期限を縮めた検査が置き換える</summary>
    public RunDeadlines Deadlines { get; init; } = RunDeadlines.Standard;

    /// <summary>台本の書き込みと起動情報の組み立て</summary>
    /// <param name="script">指示のキーに対する応答の台本</param>
    public ScriptedWorkerRun(params (string Key, WorkerResponse Answer)[] script)
    {
        Directory.CreateDirectory(directory);
        var answers = script.ToDictionary(entry => entry.Key, entry => entry.Answer);
        File.WriteAllText(Path.Combine(directory, ScriptFileName), JsonSerializer.Serialize(answers, Serializer));
        launch = new WorkerLaunchPlan(
            Path.Combine(AppContext.BaseDirectory, WorkerAssembly),
            Path.Combine(directory, "Scripted.Tests.dll"),
            directory,
            "Scripted.Target",
            directory
        );
    }

    /// <summary>応答の前に待つ指示を持つ台本。待ちのキーは SharedKey などで作る</summary>
    /// <param name="delaysMs">台本のキーから応答の前に待つ ms への対応。NeverAnswers なら応えない</param>
    /// <param name="script">指示のキーに対する応答の台本</param>
    public ScriptedWorkerRun(IReadOnlyDictionary<string, int> delaysMs, params (string Key, WorkerResponse Answer)[] script)
        : this(script) =>
        File.WriteAllText(Path.Combine(directory, DelayFileName), JsonSerializer.Serialize(delaysMs, Serializer));

    /// <summary>run の応答の後で完了の目印の前に、worker が実 stdout へ書く行を持つ台本</summary>
    /// <param name="lateOutput">台本のキーから、応答の後に書く行への対応</param>
    /// <param name="script">指示のキーに対する応答の台本</param>
    /// <returns>出力の台本を書いた検査環境</returns>
    public static ScriptedWorkerRun WithLateOutput(
        IReadOnlyDictionary<string, string[]> lateOutput,
        params (string Key, WorkerResponse Answer)[] script
    )
    {
        var run = new ScriptedWorkerRun(script);
        File.WriteAllText(Path.Combine(run.directory, OutputFileName), JsonSerializer.Serialize(lateOutput, Serializer));
        return run;
    }

    /// <summary>共有の worker が変異の run に答える台本のキー</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <returns>台本のキー</returns>
    public static string SharedKey(int mutantId) => $"{WorkerCall.SharedMode}:{mutantId}";

    /// <summary>隔離 process が変異の run に答える台本のキー。連番に負を渡すと、変異のない対照の run を指す</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <returns>台本のキー</returns>
    public static string IsolatedKey(int mutantId) => $"{WorkerCall.IsolatedMode}:{mutantId}";

    /// <summary>起動情報に載せたテスト assembly の path</summary>
    /// <value>worker の初期化へ渡すテスト実行体の path</value>
    public string TestAssemblyPath => launch.TestAssemblyPath;

    /// <summary>共有の worker が変異の run に返す応答の台本の一件</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <param name="answer">返す応答</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) SharedAnswer(int mutantId, WorkerResponse answer) =>
        ($"{WorkerCall.SharedMode}:{mutantId}", answer);

    /// <summary>隔離 process が変異の run に返す応答の台本の一件</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <param name="answer">返す応答</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) IsolatedAnswer(int mutantId, WorkerResponse answer) =>
        ($"{WorkerCall.IsolatedMode}:{mutantId}", answer);

    /// <summary>discover に返す応答の台本の一件</summary>
    /// <param name="answer">不正な種類も指定できる discover の応答</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) DiscoverAnswer(WorkerResponse answer) =>
        (WorkerCall.DiscoverRequest, answer);

    /// <summary>baseline がテスト一件に返す観測の台本の一件</summary>
    /// <param name="observation">識別子を台本のキーに使うテストの観測</param>
    /// <param name="ambient">そのテストの実行で境界の外に観測された変異の連番</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) BaselineAnswer(WorkerTestResult observation, params int[] ambient) =>
        ($"{WorkerCall.BaselineRequest}:{observation.Id}", new WorkerResponse.Baselined([observation], ambient));

    /// <summary>baseline の指示に失敗で応えさせるテスト一件の台本の一件</summary>
    /// <param name="testId">baseline に失敗を返させるテストの識別子</param>
    /// <param name="error">失敗の理由</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) BaselineFailure(string testId, string error) =>
        ($"{WorkerCall.BaselineRequest}:{testId}", new WorkerResponse.Failed(error));

    /// <summary>init に返す応答の台本の一件</summary>
    /// <param name="key">SharedInitKey か IsolatedInitKey</param>
    /// <param name="answer">開設の失敗など台本が決める応答</param>
    /// <returns>台本の一件</returns>
    public static (string Key, WorkerResponse Answer) InitAnswer(string key, WorkerResponse answer) => (key, answer);

    /// <summary>隔離 process での一件の実行と worker の起動の記録の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plan">隔離の実行形の変異計画</param>
    /// <param name="cancellation">取り消しの要求。取り消し済みも渡せる</param>
    /// <returns>確定結果と実行の記録および受信順の worker 指示の記録</returns>
    public async Task<(IsolationOutcome Outcome, IReadOnlyList<WorkerCall> Calls)> IsolateAsync(
        TestRoster roster,
        MutantPlan plan,
        CancellationToken cancellation
    )
    {
        using var isolations = new IsolationRuns(launch, roster, detectionMatrix: false, Deadlines);
        var outcome = await isolations.RunAsync(plan, cancellation);
        return (outcome, await ReadJournalAsync());
    }

    /// <summary>worker が指示を受けた時点で取り消す、一本の lane での変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="reached">受信した指示の記録から、取り消す時点に至ったかを決める条件</param>
    /// <param name="settled">判定が確定するたびに呼ぶ通知先</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedExecution> ExecuteCancellingAsync(
        TestRoster roster,
        Func<IReadOnlyList<WorkerCall>, bool> reached,
        Action<MutantId, MutantVerdict> settled,
        params MutantPlan[] plans
    )
    {
        using var cancellation = new CancellationTokenSource();
        using var orchestrator = new TestingOrchestrator(launch, roster, _ => { }, detectionMatrix: false, Deadlines);
        var running = orchestrator.ExecuteAsync(plans, concurrency: 1, new ExecutionControl(settled, cancellation.Token));
        await CancelWhenAsync(cancellation, reached);
        var result = await running;
        var calls = await ReadJournalAsync();
        return new ScriptedExecution(result, calls, await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray()));
    }

    /// <summary>実行の前に取り消し済みの token を渡した、変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedExecution> ExecuteAlreadyCancelledAsync(TestRoster roster, params MutantPlan[] plans)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var orchestrator = new TestingOrchestrator(launch, roster, _ => { }, detectionMatrix: false, Deadlines);
        var result = await orchestrator.ExecuteAsync(plans, concurrency: 1, new ExecutionControl((_, _) => { }, cancellation.Token));
        var calls = await ReadJournalAsync();
        return new ScriptedExecution(result, calls, await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray()));
    }

    /// <summary>worker が指示を受けた時点で取り消す、基準観測の収集と worker の終了の観測</summary>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <param name="reached">受信した指示の記録から、取り消す時点に至ったかを決める条件</param>
    /// <returns>基準観測の収集結果と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedBaseline> BaselineCancellingAsync(int concurrency, Func<IReadOnlyList<WorkerCall>, bool> reached)
    {
        using var cancellation = new CancellationTokenSource();
        var running = BaselineExecution.RunAsync(launch, concurrency, cancellation.Token);
        await CancelWhenAsync(cancellation, reached);
        var outcome = await running;
        var calls = await ReadJournalAsync();
        return new ScriptedBaseline(outcome, calls, await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray()));
    }

    /// <summary>一本の lane での変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    public Task<ScriptedExecution> ExecuteAsync(TestRoster roster, params MutantPlan[] plans) =>
        RunAsync(roster, detectionMatrix: false, ExecutionControl.None, plans);

    /// <summary>判定の確定ごとの通知を受ける一本の lane での変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="settled">判定が確定するたびに呼ぶ通知先</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    public Task<ScriptedExecution> ExecuteSettlingAsync(
        TestRoster roster,
        Action<MutantId, MutantVerdict> settled,
        params MutantPlan[] plans
    ) => RunAsync(roster, detectionMatrix: false, new ExecutionControl(settled, CancellationToken.None), plans);

    /// <summary>検出行列を求める一本の lane での変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と行と操作の記録および終了後の process の状態</returns>
    public Task<ScriptedExecution> ExecuteDetectionMatrixAsync(TestRoster roster, params MutantPlan[] plans) =>
        RunAsync(roster, detectionMatrix: true, ExecutionControl.None, plans);

    /// <summary>基準観測の収集と worker の終了の観測</summary>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <returns>基準観測の収集結果と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedBaseline> BaselineAsync(int concurrency)
    {
        var outcome = await BaselineExecution.RunAsync(launch, concurrency, CancellationToken.None);
        var calls = await ReadJournalAsync();
        var surviving = await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray());
        return new ScriptedBaseline(outcome, calls, surviving);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>一本の lane での変異実行と記録と process の状態の回収</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="detectionMatrix">変異ごとに全失敗の記録を求めるか</param>
    /// <param name="control">実行に添える通知先</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    private async Task<ScriptedExecution> RunAsync(
        TestRoster roster,
        bool detectionMatrix,
        ExecutionControl control,
        MutantPlan[] plans
    )
    {
        using var orchestrator = new TestingOrchestrator(launch, roster, _ => { }, detectionMatrix, Deadlines);
        var result = await orchestrator.ExecuteAsync(plans, concurrency: 1, control);
        var calls = await ReadJournalAsync();
        var surviving = await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray());
        return new ScriptedExecution(result, calls, surviving);
    }

    /// <summary>worker 群が残した記録の読み込み</summary>
    /// <returns>受信順の worker 指示の記録</returns>
    private async Task<List<WorkerCall>> ReadJournalAsync()
    {
        var path = Path.Combine(directory, JournalFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path);
        return lines.Select(WorkerCall.Parse).ToList();
    }

    /// <summary>worker が受けた指示が条件に至るまで待ってからの取り消し</summary>
    /// <param name="cancellation">取り消しを発する元</param>
    /// <param name="reached">受信した指示の記録から、取り消す時点に至ったかを決める条件</param>
    /// <returns>取り消しを発したことを表す task</returns>
    /// <exception cref="TimeoutException">条件に至らないまま待ちの上限を過ぎた状態</exception>
    private async Task CancelWhenAsync(CancellationTokenSource cancellation, Func<IReadOnlyList<WorkerCall>, bool> reached)
    {
        var waited = Stopwatch.StartNew();
        while (!reached(await ReadJournalAsync()))
        {
            if (waited.Elapsed > ReachPatience)
            {
                await cancellation.CancelAsync();
                throw new TimeoutException("worker が条件の指示を受けなかった");
            }

            await Task.Delay(PollInterval, CancellationToken.None);
        }

        await cancellation.CancelAsync();
    }

    /// <summary>終了猶予を過ぎても存続する worker の特定</summary>
    /// <param name="pids">終了を確かめる worker の process id の列</param>
    /// <returns>終了猶予を過ぎても存続する process id の列</returns>
    private static async Task<List<int>> StillRunningAsync(int[] pids)
    {
        var running = pids.Where(IsRunning).ToList();
        var waited = Stopwatch.StartNew();
        while (running.Count > 0 && waited.Elapsed < ExitGrace)
        {
            await Task.Delay(PollInterval);
            running = running.Where(IsRunning).ToList();
        }

        return running;
    }

    /// <summary>worker process の残存の判定</summary>
    /// <param name="pid">状態を確かめる process の id</param>
    /// <returns>実行中なら真</returns>
    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
