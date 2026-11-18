namespace Mutation.Domain;

/// <summary>判定の再利用の同一性へ含める、変異の集合と判定を左右する設定</summary>
/// <remarks>同時実行数のように判定を変えない設定と、前回の保存を読むかの指定は含めない</remarks>
/// <param name="Selection">変異対象の選別</param>
/// <param name="ValidateSurvivors">生存した変異を新規 process で再検証するか</param>
/// <param name="ExcludeStatic">static 初期化でしか実行されない変異を対象外にするか</param>
public sealed record ReuseSettings(MutationSelection Selection, bool ValidateSurvivors, bool ExcludeStatic);
