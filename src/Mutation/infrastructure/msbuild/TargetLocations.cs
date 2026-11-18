using Mutation.Application;
using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Msbuild;

/// <summary>binlog 読取による ILocateTargets の adapter</summary>
public sealed class TargetLocations : ILocateTargets
{
    /// <inheritdoc />
    public Result<LocatedTarget, PipelineFailure> Locate(string binlogPath, TargetProjects projects) =>
        TargetLocation.Locate(binlogPath, projects);
}
