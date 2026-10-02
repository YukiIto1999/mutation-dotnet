using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>差分運用の変更ファイル解決を担う port</summary>
public interface IChangeSets
{
    /// <summary>基点から変わった行の取得</summary>
    /// <param name="projectDirectory">対象 project の directory</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変更行の範囲、失敗なら理由</returns>
    Result<ChangedLines, PipelineFailure> Resolve(string projectDirectory, string sinceRef);
}
