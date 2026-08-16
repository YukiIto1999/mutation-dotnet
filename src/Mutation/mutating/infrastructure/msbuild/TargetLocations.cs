using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>binlog 読取による ILocateTargets の adapter</summary>
public sealed class TargetLocations : ILocateTargets
{
    /// <inheritdoc />
    public Result<(CscInvocation Sut, string TestAssembly), PipelineFailure> Locate(
        string binlogPath,
        TargetProjects projects
    ) => TargetLocation.Locate(binlogPath, projects);
}
