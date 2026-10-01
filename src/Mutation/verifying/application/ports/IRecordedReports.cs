using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

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
public sealed record RecordedReport(string ProjectRoot, IReadOnlyList<RecordedMutant> Mutants);
