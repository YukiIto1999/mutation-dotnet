using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>binlog 付き build を担う port</summary>
public interface IBinlogBuilds
{
    /// <summary>テスト project の binlog 付き build と binlog path の取得</summary>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら binlog の path、失敗なら build 出力付きの失敗</returns>
    Task<Result<string, PipelineFailure>> BuildAsync(TargetProjects projects, string workDirectory);

    /// <summary>増分 build を無効化し csc の再実行を強制する build と binlog path の取得</summary>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら binlog の path、失敗なら build 出力付きの失敗</returns>
    Task<Result<string, PipelineFailure>> RebuildAsync(TargetProjects projects, string workDirectory);
}
