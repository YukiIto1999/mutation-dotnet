using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>書き出し済みの変異検査の報告の読み戻しを担う port</summary>
public interface IRecordedReports
{
    /// <summary>報告の読み込み</summary>
    /// <param name="reportPath">mutation-report.json の path</param>
    /// <returns>成功なら報告の内容、読めなければ理由</returns>
    Result<RecordedReport, PipelineFailure> Read(string reportPath);
}

/// <summary>読み戻した変異検査の報告</summary>
/// <param name="ProjectRoot">報告内の相対 path の基準になる directory の絶対 path</param>
/// <param name="Mutants">記録された全変異</param>
/// <param name="Unanalysed">報告の run が残した、`--since` の選別で未分析にした関係と件数の記述。差分運用でない報告や前の版の報告では不在</param>
/// <param name="Run">報告の run が残した、走行が全対象の判定へ至ったかの記録。この道具が走行の状態を書かなかった報告では不在で、不在は全対象の判定に至った走行とは別</param>
public sealed record RecordedReport(
    string ProjectRoot,
    IReadOnlyList<RecordedMutant> Mutants,
    IReadOnlyList<string>? Unanalysed = null,
    RecordedRun? Run = null
);

/// <summary>報告を書いた走行が、全対象の判定へ至ったかの記録</summary>
[Closed]
public abstract record RecordedRun
{
    /// <summary>列挙した派生以外の記録を塞ぐ基底の構築</summary>
    private RecordedRun()
    {
    }

    /// <summary>全対象が判定に至って終わった走行。判定を確定できなかった変異は残りうる</summary>
    public sealed record Finished : RecordedRun;

    /// <summary>取り消しを受けて止まった走行</summary>
    /// <param name="Targets">取り消しで判定に至らなかった対象の名前</param>
    public sealed record Interrupted(IReadOnlyList<string> Targets) : RecordedRun;

    /// <summary>取り消しではない失敗で、判定に至らなかった対象を残した走行</summary>
    /// <param name="Targets">失敗で判定に至らなかった対象の名前</param>
    public sealed record Failed(IReadOnlyList<string> Targets) : RecordedRun;
}
