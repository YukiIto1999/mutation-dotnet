using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>binlog からの対象特定を担う port</summary>
public interface ILocateTargets
{
    /// <summary>対象 project の csc 呼び出しとテスト assembly の実行時 path と、build が示す project の入力の特定</summary>
    /// <param name="binlogPath">読み取る binlog の path</param>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <returns>成功なら対象の csc 呼び出しとテスト assembly の path と、対象とテストと参照する project の入力</returns>
    Result<LocatedTarget, PipelineFailure> Locate(string binlogPath, TargetProjects projects);
}
