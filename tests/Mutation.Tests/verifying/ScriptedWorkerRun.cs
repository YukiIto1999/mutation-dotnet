using System.Diagnostics;
using System.Text.Json;
using Mutation.Protocol;
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

    /// <summary>Mutation.ScriptedWorker と共有する受信記録の file 名</summary>
    private const string JournalFileName = "journal.log";

    /// <summary>終了を命じた worker の process が消えるのを待つ上限</summary>
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(10);

    /// <summary>process の消滅を見る間隔</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>この実行の台本と記録の専用 directory</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"mutation-tests-{Guid.NewGuid():N}");

    /// <summary>worker の起動情報</summary>
    private readonly WorkerLaunchPlan launch;

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

    /// <summary>一本の lane での変異実行と worker の終了の観測</summary>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plans">共有の計画を実行順に並べた変異計画</param>
    /// <returns>判定と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedExecution> ExecuteAsync(TestRoster roster, params MutantPlan[] plans)
    {
        using var orchestrator = new TestingOrchestrator(launch, roster, _ => { });
        var result = await orchestrator.ExecuteAsync(plans, concurrency: 1);
        var calls = await ReadJournalAsync();
        var surviving = await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray());
        return new ScriptedExecution(result, calls, surviving);
    }

    /// <summary>基準観測の収集と worker の終了の観測</summary>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <returns>基準観測の収集結果と操作の記録および終了後の process の状態</returns>
    public async Task<ScriptedBaseline> BaselineAsync(int concurrency)
    {
        var outcome = await BaselineExecution.RunAsync(launch, concurrency);
        var calls = await ReadJournalAsync();
        var surviving = await StillRunningAsync(calls.Select(call => call.Pid).Distinct().ToArray());
        return new ScriptedBaseline(outcome, calls, surviving);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, recursive: true);

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
