using System.Text.Json;
using Mutation.Protocol;

namespace Mutation.ScriptedWorker;

/// <summary>台本に基づく worker の応答と受信指示の記録</summary>
/// <remarks>
/// results directory の script.json に置くキー別の応答
/// run のキーは shared:{変異の連番} と isolated:{変異の連番}
/// 実行形の区別は run 指示の probe 上限の有無
/// discover のキーは discover で baseline のキーは baseline:{識別子}
/// baseline の結果は要求順の観測と重複を除いた Ambient
/// Baselined 以外の baseline 応答は観測の連結を打ち切る失敗
/// 台本にない指示や識別子は失敗の応答
/// journal.log の列は pid・指示・実行形・変異の連番・テスト識別子で区切りは縦棒
/// 検出かつ未到達の応答を返した共有 worker は後続の run も失敗させる汚染状態
/// </remarks>
internal sealed class ScriptedBehavior
{
    /// <summary>台本の file 名</summary>
    private const string ScriptFileName = "script.json";

    /// <summary>受けた指示の記録の file 名</summary>
    private const string JournalFileName = "journal.log";

    /// <summary>discover の応答を置く台本のキー</summary>
    private const string DiscoverKey = "discover";

    /// <summary>テスト識別子別の baseline 応答のキー接頭辞</summary>
    private const string BaselineKeyPrefix = "baseline";


    /// <summary>汚れた process が返す検出のテスト名</summary>
    private const string PollutedKillerTest = "(polluted shared worker)";

    /// <summary>run 以外の指示で実行形と変異の連番に書く値</summary>
    private const string NoValue = "-";

    /// <summary>一行 JSON の書式</summary>
    private static readonly JsonSerializerOptions Serializer = new(JsonSerializerDefaults.Web);

    /// <summary>実行形と変異の連番から run 応答への対応</summary>
    private readonly Dictionary<string, WorkerResponse> script;

    /// <summary>受けた指示の記録の path</summary>
    private readonly string journalPath;

    /// <summary>検出かつ未到達の応答を返して汚れたか</summary>
    private bool polluted;

    /// <summary>独立した実行の台本と記録先の束ね</summary>
    /// <param name="script">指示のキー別の応答</param>
    /// <param name="journalPath">この実行だけが使う記録の path</param>
    private ScriptedBehavior(Dictionary<string, WorkerResponse> script, string journalPath)
    {
        this.script = script;
        this.journalPath = journalPath;
    }

    /// <summary>results directory にある台本の読み込み</summary>
    /// <param name="resultsDirectory">台本と記録を置く directory</param>
    /// <returns>台本どおりに応える挙動</returns>
    public static ScriptedBehavior Load(string resultsDirectory)
    {
        var json = File.ReadAllText(Path.Combine(resultsDirectory, ScriptFileName));
        var script =
            JsonSerializer.Deserialize<Dictionary<string, WorkerResponse>>(json, Serializer)
            ?? throw new InvalidOperationException("台本が空");
        return new ScriptedBehavior(script, Path.Combine(resultsDirectory, JournalFileName));
    }

    /// <summary>一指示の記録と応答</summary>
    /// <param name="request">受信した指示または解釈できない場合の不在</param>
    /// <returns>台本に沿った応答</returns>
    public async Task<WorkerResponse> AnswerAsync(WorkerRequest? request) =>
        request switch
        {
            WorkerRequest.Init => await OpenAsync(),
            WorkerRequest.Discover => await DiscoverAsync(),
            WorkerRequest.Baseline baseline => await BaselineAsync(baseline),
            WorkerRequest.Run run => await RunAsync(run),
            WorkerRequest.Shutdown => new WorkerResponse.Done(),
            _ => new WorkerResponse.Failed("台本にない指示"),
        };

    /// <summary>init の記録と開設の応答</summary>
    /// <returns>開設済みを示す応答</returns>
    private async Task<WorkerResponse> OpenAsync()
    {
        await JournalAsync("init", NoValue, NoValue, []);
        return new WorkerResponse.Opened();
    }

    /// <summary>台本に基づくテスト発見の応答</summary>
    /// <returns>発見済みテストの応答または台本不足の失敗</returns>
    private async Task<WorkerResponse> DiscoverAsync()
    {
        await JournalAsync("discover", NoValue, NoValue, []);
        return script.TryGetValue(DiscoverKey, out var scripted) ? scripted : new WorkerResponse.Failed("台本にない discover");
    }

    /// <summary>要求されたテストの基準観測の連結</summary>
    /// <param name="baseline">観測を求めるテストの識別子の列</param>
    /// <returns>要求順の基準観測または最初に遭遇した収集失敗</returns>
    private async Task<WorkerResponse> BaselineAsync(WorkerRequest.Baseline baseline)
    {
        await JournalAsync("baseline", NoValue, NoValue, baseline.TestIds);
        var observations = new List<WorkerResponse.Baselined>();
        foreach (var testId in baseline.TestIds)
        {
            var key = $"{BaselineKeyPrefix}:{testId}";
            var answer = script.TryGetValue(key, out var scripted) ? scripted : new WorkerResponse.Failed($"台本にない baseline: {key}");
            if (answer is not WorkerResponse.Baselined observed)
            {
                return answer;
            }

            observations.Add(observed);
        }

        return new WorkerResponse.Baselined(
            observations.SelectMany(observed => observed.Tests).ToArray(),
            observations.SelectMany(observed => observed.Ambient).Distinct().ToArray()
        );
    }

    /// <summary>台本と process の汚染状態に応じた変異実行の応答</summary>
    /// <param name="run">活性化する変異とテスト列の指示</param>
    /// <returns>変異の検査結果または台本不足の失敗</returns>
    private async Task<WorkerResponse> RunAsync(WorkerRequest.Run run)
    {
        var mode = run.HitLimit is null ? "isolated" : "shared";
        await JournalAsync("run", mode, $"{run.MutantId}", run.TestIds);
        if (polluted)
        {
            return new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, PollutedKillerTest, null, 0, true);
        }

        var key = $"{mode}:{run.MutantId}";
        var answer = script.TryGetValue(key, out var scripted) ? scripted : new WorkerResponse.Failed($"台本にない run: {key}");
        polluted = answer is WorkerResponse.RunCompleted { Outcome: WorkerContract.OutcomeKilled, Reached: false };
        return answer;
    }

    /// <summary>実行ごとの排他で保護した受信指示の追記</summary>
    /// <param name="request">受信した指示の種別</param>
    /// <param name="mode">共有または隔離の実行形の識別子</param>
    /// <param name="mutant">変異の連番または連番を持たない指示の目印</param>
    /// <param name="testIds">指示が持つテスト識別子の列</param>
    /// <returns>追記完了を表す task</returns>
    /// <exception cref="IOException">journal への追記の失敗</exception>
    /// <exception cref="AbandonedMutexException">前の記録を書いていた worker の異常終了</exception>
    private Task JournalAsync(string request, string mode, string mutant, IEnumerable<string> testIds)
    {
        var line = $"{Environment.ProcessId}|{request}|{mode}|{mutant}|{string.Join(',', testIds)}";
        var runName = Path.GetFileName(Path.GetDirectoryName(journalPath));
        using var mutex = new Mutex(initiallyOwned: false, $"Mutation.ScriptedWorker.{runName}");
        mutex.WaitOne();
        try
        {
            File.AppendAllText(journalPath, line + Environment.NewLine);
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        return Task.CompletedTask;
    }
}
