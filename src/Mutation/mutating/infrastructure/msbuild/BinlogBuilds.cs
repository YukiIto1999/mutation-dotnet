using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>dotnet build による IBinlogBuilds の adapter</summary>
public sealed class BinlogBuilds : IBinlogBuilds
{
    /// <inheritdoc />
    public Task<Result<string, PipelineFailure>> BuildAsync(
        IReadOnlyList<TargetProjects> projects,
        string workDirectory
    ) => BuildExecution.BuildWithBinlog(TestProjectsOf(projects), ConfigurationOf(projects), workDirectory);

    /// <inheritdoc />
    public Task<Result<string, PipelineFailure>> RebuildAsync(
        IReadOnlyList<TargetProjects> projects,
        string workDirectory
    ) => BuildExecution.RebuildWithBinlog(TestProjectsOf(projects), ConfigurationOf(projects), workDirectory);

    /// <summary>build するテスト project の path の取り出し</summary>
    private static string[] TestProjectsOf(IReadOnlyList<TargetProjects> projects) =>
        projects.Select(p => p.TestProjectPath).ToArray();

    /// <summary>対象間で共通の build 構成の取り出し</summary>
    private static string ConfigurationOf(IReadOnlyList<TargetProjects> projects) => projects[0].Configuration;
}
