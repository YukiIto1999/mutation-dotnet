using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>差分の基点から変わった行の解決を担う port</summary>
public interface IChangedLines
{
    /// <summary>基点と作業木の差分と未追跡ファイルの、変わった行としての取得</summary>
    /// <param name="directory">git repo の中にある directory</param>
    /// <param name="sinceRef">差分の基点になる git の参照</param>
    /// <returns>成功なら変わったファイルと行、差分を取れなければ理由</returns>
    Result<ChangedLines, PipelineFailure> Resolve(string directory, string sinceRef);
}
