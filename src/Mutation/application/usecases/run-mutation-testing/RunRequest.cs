using Mutation.Domain;

namespace Mutation.Application;

/// <summary>一回の変異検査への入力</summary>
/// <param name="Targets">対象とテストの project の指定の列。並べた順に検査する</param>
/// <param name="Selection">変異対象の選別の指定</param>
/// <param name="Execution">実行と判定の制御</param>
/// <param name="OutputDirectory">報告と中間物を置く directory</param>
/// <param name="WithBaseline">前回実行の保存から判定を継承するか</param>
public sealed record RunRequest(
    IReadOnlyList<TargetProjects> Targets,
    MutationSelection Selection,
    ExecutionSettings Execution,
    string OutputDirectory,
    bool WithBaseline
)
{
    /// <summary>検出行列を要求せず、前回の判定の再利用を求めているか</summary>
    public bool ReuseEnabled => WithBaseline && !Execution.DetectionMatrix;
}
