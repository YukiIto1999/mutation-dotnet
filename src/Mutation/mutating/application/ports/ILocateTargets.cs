using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>binlog からの対象特定を担う port</summary>
public interface ILocateTargets
{
    /// <summary>対象 project の csc 呼び出しとテスト assembly の実行時 path の特定</summary>
    /// <param name="binlogPath">読み取る binlog の path</param>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <returns>成功なら対象の csc 呼び出しとテスト assembly の path の対</returns>
    Result<(CscInvocation Sut, string TestAssembly), PipelineFailure> Locate(string binlogPath, TargetProjects projects);
}
