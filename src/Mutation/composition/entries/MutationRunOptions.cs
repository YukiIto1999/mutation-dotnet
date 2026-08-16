namespace Mutation.Composition;

/// <summary>外から受け取る一回の変異検査の設定</summary>
/// <param name="ProjectPath">変異対象 project の csproj の絶対 path</param>
/// <param name="TestProjectPath">テスト project の csproj の絶対 path</param>
/// <param name="Configuration">build 構成の名前</param>
/// <param name="Concurrency">同時に走らせる worker 数</param>
/// <param name="ValidateSurvivors">生存した変異を新規 process で再検証するか</param>
/// <param name="ExcludeStatic">static 初期化でしか実行されない変異を対象外にするか</param>
/// <param name="MutatePatterns">変異対象に含めるファイルの glob。先頭 `!` は除外。空なら全ファイル</param>
/// <param name="SinceRef">差分運用の基点になる git の参照。使わないなら不在</param>
/// <param name="IgnoredOperators">除外する変異演算子の名前の列</param>
/// <param name="IgnoredMethods">その呼び出しの中を変異させない method 名の列</param>
/// <param name="OutputDirectory">報告と中間物を置く directory</param>
/// <param name="WithBaseline">前回実行の保存から判定を継承するか</param>
public sealed record MutationRunOptions(
    string ProjectPath,
    string TestProjectPath,
    string Configuration,
    int Concurrency,
    bool ValidateSurvivors,
    bool ExcludeStatic,
    IReadOnlyList<string> MutatePatterns,
    string? SinceRef,
    IReadOnlyList<string> IgnoredOperators,
    IReadOnlyList<string> IgnoredMethods,
    string OutputDirectory,
    bool WithBaseline
)
{
    /// <summary>変異の集合と判定に影響する設定の、指紋計算へ渡す要約</summary>
    /// <returns>設定項目を一行ずつ並べた要約</returns>
    public string FingerprintSettings() =>
        $"mutate:{string.Join(',', MutatePatterns)}\n"
        + $"since:{SinceRef}\n"
        + $"ignore-operators:{string.Join(',', IgnoredOperators)}\n"
        + $"ignore-methods:{string.Join(',', IgnoredMethods)}\n"
        + $"exclude-static:{ExcludeStatic}\n"
        + $"validate-survivors:{ValidateSurvivors}";
}
