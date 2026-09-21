using Mutation.Verifying.Domain;



namespace Mutation.Cli;

/// <summary>進行の出来事から端末へ出す一行への変換</summary>
public static class ProgressLines
{
    /// <summary>一つの出来事の、人が読む一行への変換</summary>
    /// <param name="progress">実行中に起きた出来事</param>
    /// <returns>端末へ出す一行</returns>
    public static string Describe(RunProgress progress) =>
        progress switch
        {
            RunProgress.BuildCompleted build => $"build 完了 {build.Ms:F0}ms",
            RunProgress.TargetStarted target => $"[{target.Index}/{target.Total}] {target.Name}",
            RunProgress.TargetAbandoned abandoned =>
                $"[中断] {abandoned.Name}: {FailureLines.Describe(abandoned.Failure)}",
            RunProgress.SnapshotMatched => "前回の保存と生成入力が完全一致。変異と判定を前回から再利用",
            RunProgress.MutantsGenerated generated =>
                $"変異 {generated.Mutants} 件生成 (compile error {generated.CompileErrors} 件) "
                    + $"mutate {generated.MutateMs:F0}ms compile {generated.CompileMs:F0}ms",
            RunProgress.BaselineCompleted baseline =>
                $"baseline 完了 {baseline.Ms:F0}ms テスト {baseline.Tests} 件"
                    + (baseline.FailingExcluded > 0 ? $" (失敗 {baseline.FailingExcluded} 件は除外)" : ""),
            RunProgress.ExecutionPlanned planned =>
                $"実行対象 {planned.Planned} 件 (被覆なし {planned.NoCoverage} 件は即確定"
                    + (planned.ExcludedStatic > 0 ? $"、static 除外 {planned.ExcludedStatic} 件" : "")
                    + ")",
            RunProgress.VerdictsInherited inherited =>
                inherited.Count > 0
                    ? $"前回の保存から {inherited.Count} 件の判定を継承"
                    : "前回の保存から継承できる判定はない",
            RunProgress.ExecutionStarted started =>
                $"共有ホスト {started.SharedCount} 件 / 隔離 process {started.IsolatedCount} 件を {started.Lanes} lane で実行",
            RunProgress.VerdictProgress verdicts => $"  {verdicts.Done}/{verdicts.Total} 件判定",
        };
}
