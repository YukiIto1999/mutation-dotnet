using System.Globalization;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>台本の worker が記録した一件の指示</summary>
/// <param name="Pid">指示を受けた process の id</param>
/// <param name="Request">init・discover・baseline・run の指示種別</param>
/// <param name="Mode">run の実行形またはそれ以外の指示の目印</param>
/// <param name="MutantId">run の対象の変異連番またはそれ以外の指示での不在</param>
/// <param name="TestIds">run と baseline が実行を求めるテスト識別子の列</param>
internal sealed record WorkerCall(int Pid, string Request, string Mode, int? MutantId, IReadOnlyList<string> TestIds)
{
    /// <summary>run の指示の種類名</summary>
    public const string RunRequest = "run";

    /// <summary>discover の指示の種類名</summary>
    public const string DiscoverRequest = "discover";

    /// <summary>baseline の指示の種類名</summary>
    public const string BaselineRequest = "baseline";

    /// <summary>共有の worker で受けた実行形の名前</summary>
    public const string SharedMode = "shared";

    /// <summary>環境変数で活性化済みの隔離 process で受けた実行形の名前</summary>
    public const string IsolatedMode = "isolated";

    /// <summary>記録の一行からの復元</summary>
    /// <param name="line">pid・指示・実行形・変異連番・テスト識別子を縦棒で区切った一行</param>
    /// <returns>その行が表す指示</returns>
    public static WorkerCall Parse(string line)
    {
        var fields = line.Split('|');
        return new WorkerCall(
            int.Parse(fields[0], CultureInfo.InvariantCulture),
            fields[1],
            fields[2],
            int.TryParse(fields[3], CultureInfo.InvariantCulture, out var mutantId) ? mutantId : null,
            fields[4].Split(',', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}

/// <summary>台本 worker を使う一回の変異実行の観測結果</summary>
/// <param name="Result">orchestrator が返した判定と集計</param>
/// <param name="Calls">受信順の worker 指示の記録</param>
/// <param name="SurvivingProcesses">実行を終えた後も動いていた worker の process id</param>
internal sealed record ScriptedExecution(
    OrchestrationResult Result,
    IReadOnlyList<WorkerCall> Calls,
    IReadOnlyList<int> SurvivingProcesses
)
{
    /// <summary>変異の最終の判定</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <returns>確定した判定</returns>
    public MutantVerdict Verdict(int mutantId) => Result.Verdicts[new MutantId(mutantId)];

    /// <summary>指定した変異を共有 worker で実行した記録の取得</summary>
    /// <param name="mutantId">変異の連番</param>
    /// <returns>該当する一件の run</returns>
    /// <exception cref="InvalidOperationException">該当する run が一件に定まらない状態</exception>
    public WorkerCall SharedRun(int mutantId) =>
        Calls.Single(call => call is { Request: WorkerCall.RunRequest, Mode: WorkerCall.SharedMode } && call.MutantId == mutantId);

    /// <summary>隔離 process が受けた run の全件</summary>
    /// <returns>受信順の隔離 process への run</returns>
    public WorkerCall[] IsolatedRuns() =>
        Calls.Where(call => call is { Request: WorkerCall.RunRequest, Mode: WorkerCall.IsolatedMode }).ToArray();

    /// <summary>実行の間に起動した worker の process の数</summary>
    /// <returns>init を受けた process の数</returns>
    public int StartedProcesses() => Calls.Select(call => call.Pid).Distinct().Count();

    /// <summary>隔離 process が run を受けた変異の連番</summary>
    /// <returns>run の受信順の変異連番</returns>
    public int[] IsolatedMutants() => IsolatedRuns().Select(call => call.MutantId).OfType<int>().ToArray();
}

/// <summary>台本 worker による基準観測の収集結果</summary>
/// <param name="Outcome">基準観測の集計結果または収集失敗</param>
/// <param name="Calls">process ごとの受信順を保つ worker 指示の記録</param>
/// <param name="SurvivingProcesses">実行を終えた後も動いていた worker の process id</param>
internal sealed record ScriptedBaseline(
    Result<BaselineProfile, PipelineFailure> Outcome,
    IReadOnlyList<WorkerCall> Calls,
    IReadOnlyList<int> SurvivingProcesses
)
{
    /// <summary>worker 群が受けた baseline の指示の全件</summary>
    /// <returns>各 shard の baseline 指示の列</returns>
    public WorkerCall[] BaselineRuns() => Calls.Where(call => call.Request == WorkerCall.BaselineRequest).ToArray();
}