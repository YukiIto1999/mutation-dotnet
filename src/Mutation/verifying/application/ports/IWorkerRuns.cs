using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>worker 群での基準実行と変異計画の実行を担う port</summary>
public interface IWorkerRuns
{
    /// <summary>被覆収集モードでの基準実行の観測</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <returns>成功なら基準実行の観測、失敗なら理由</returns>
    Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(WorkerLaunchPlan launch, int concurrency);

    /// <summary>変異計画の実行と判定の集計</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="plans">実行する変異計画の列</param>
    /// <param name="concurrency">同時に走らせる worker 数</param>
    /// <param name="detectionMatrix">変異ごとに全テストを実行し検出行列の行を残すか</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <param name="control">実行に添える通知先</param>
    /// <returns>この呼び出し分の判定と記録</returns>
    Task<OrchestrationResult> ExecuteAsync(
        WorkerLaunchPlan launch,
        TestRoster roster,
        IReadOnlyList<MutantPlan> plans,
        int concurrency,
        bool detectionMatrix,
        Action<RunProgress> progress,
        ExecutionControl control
    );
}

/// <summary>変異計画の実行に添える制御</summary>
/// <param name="Settled">判定が確定するたびに呼ぶ通知先。判定を確定できなかった変異では呼ばない</param>
public sealed record ExecutionControl(Action<MutantId, MutantVerdict> Settled)
{
    /// <summary>通知を求めない制御</summary>
    public static ExecutionControl None { get; } = new((_, _) => { });
}
