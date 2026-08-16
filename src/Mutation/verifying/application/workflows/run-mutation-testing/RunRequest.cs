using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>一回の変異検査の workflow への入力</summary>
/// <param name="Target">対象とテストの project の指定</param>
/// <param name="Selection">変異対象の選別の指定</param>
/// <param name="Execution">実行と判定の制御</param>
/// <param name="FingerprintSettings">指紋へ畳む設定の要約。継承を使わないなら不在</param>
/// <param name="OutputDirectory">報告と中間物を置く directory</param>
/// <param name="WithBaseline">前回実行の保存から判定を継承するか</param>
public sealed record RunRequest(
    TargetRequest Target,
    SelectionRequest Selection,
    ExecutionSettings Execution,
    string? FingerprintSettings,
    string OutputDirectory,
    bool WithBaseline
);
