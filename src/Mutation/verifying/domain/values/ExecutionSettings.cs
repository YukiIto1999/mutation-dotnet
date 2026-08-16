
namespace Mutation.Verifying.Domain;

/// <summary>実行と判定の制御</summary>
/// <param name="Concurrency">同時に走らせる worker 数</param>
/// <param name="ValidateSurvivors">生存した変異を新規 process で再検証するか</param>
/// <param name="ExcludeStatic">static 初期化でしか実行されない変異を対象外にするか</param>
public sealed record ExecutionSettings(int Concurrency, bool ValidateSurvivors, bool ExcludeStatic);
