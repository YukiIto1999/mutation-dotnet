
namespace Mutation.Verifying.Domain;

/// <summary>worker の起動と初期化に要る情報一式</summary>
/// <param name="WorkerDllPath">worker assembly の絶対 path</param>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="MutatedDirectory">変異 assembly の置き場</param>
/// <param name="TargetAssemblyName">差し替え対象 assembly の単純名</param>
/// <param name="ResultsDirectory">worker 内のテスト実行体が成果物を書く directory</param>
public sealed record WorkerLaunchPlan(
    string WorkerDllPath,
    string TestAssemblyPath,
    string MutatedDirectory,
    string TargetAssemblyName,
    string ResultsDirectory
);
