using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>差分運用の変更ファイル解決を担う port</summary>
public interface IChangeSets
{
    /// <summary>基点からの差分と未追跡ファイルの、絶対 path 集合としての取得</summary>
    /// <param name="projectDirectory">対象 project の directory。git repo の中にあること</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変更ファイルの絶対 path 集合、失敗なら理由</returns>
    Result<IReadOnlySet<string>, PipelineFailure> Resolve(string projectDirectory, string sinceRef);
}
