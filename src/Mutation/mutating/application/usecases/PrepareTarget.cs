using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>対象の build と csc 呼び出しの特定。csc が走らなければ一度だけ再 build する use-case</summary>
/// <param name="builds">binlog 付き build の port</param>
/// <param name="locations">対象特定の port</param>
public sealed class PrepareTarget(IBinlogBuilds builds, ILocateTargets locations)
{
    /// <summary>build → 特定 → 必要なら再 build → 特定、の遂行</summary>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら対象の csc 呼び出しとテスト assembly の path の対</returns>
    public async Task<Result<(CscInvocation Sut, string TestAssembly), PipelineFailure>> ExecuteAsync(
        TargetProjects projects,
        string workDirectory
    )
    {
        var built = await builds.BuildAsync(projects, workDirectory).ConfigureAwait(false);
        if (built is Result<string, PipelineFailure>.Failed(var buildFailure))
        {
            return new Result<(CscInvocation, string), PipelineFailure>.Failed(buildFailure);
        }

        var binlogPath = ((Result<string, PipelineFailure>.Succeeded)built).Value;
        var located = locations.Locate(binlogPath, projects);
        if (located is Result<(CscInvocation, string), PipelineFailure>.Succeeded)
        {
            return located;
        }

        var rebuilt = await builds.RebuildAsync(projects, workDirectory).ConfigureAwait(false);
        if (rebuilt is Result<string, PipelineFailure>.Failed(var rebuildFailure))
        {
            return new Result<(CscInvocation, string), PipelineFailure>.Failed(rebuildFailure);
        }

        return locations.Locate(((Result<string, PipelineFailure>.Succeeded)rebuilt).Value, projects);
    }
}
