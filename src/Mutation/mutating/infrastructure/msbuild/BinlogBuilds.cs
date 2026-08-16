using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>dotnet build による IBinlogBuilds の adapter</summary>
public sealed class BinlogBuilds : IBinlogBuilds
{
    /// <inheritdoc />
    public Task<Result<string, PipelineFailure>> BuildAsync(TargetProjects projects, string workDirectory) =>
        BuildExecution.BuildWithBinlog(projects.TestProjectPath, projects.Configuration, workDirectory);

    /// <inheritdoc />
    public Task<Result<string, PipelineFailure>> RebuildAsync(TargetProjects projects, string workDirectory) =>
        BuildExecution.RebuildWithBinlog(projects.TestProjectPath, projects.Configuration, workDirectory);
}
