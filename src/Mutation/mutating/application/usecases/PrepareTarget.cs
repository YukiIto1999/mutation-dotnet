using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>全対象の一度の build と csc 呼び出しの特定。csc が走らなかった対象だけ再 build する use-case</summary>
/// <param name="builds">binlog 付き build の port</param>
/// <param name="locations">対象特定の port</param>
public sealed class PrepareTarget(IBinlogBuilds builds, ILocateTargets locations)
{
    /// <summary>build → 特定 → 見つからなかった対象だけ再 build → 特定、の遂行</summary>
    /// <remarks>build の失敗は全対象に及ぶ失敗、特定の失敗は対象ごとの失敗</remarks>
    /// <param name="projects">対象とテストの project の特定の列</param>
    /// <param name="workDirectory">binlog を置く作業 directory</param>
    /// <returns>成功なら入力と同じ順の対象ごとの準備の結果</returns>
    public async Task<Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure>> ExecuteAsync(
        IReadOnlyList<TargetProjects> projects,
        string workDirectory
    )
    {
        var built = await builds.BuildAsync(projects, workDirectory).ConfigureAwait(false);
        if (built is Result<string, PipelineFailure>.Failed(var buildFailure))
        {
            return Failure(buildFailure);
        }

        var located = Locate(((Result<string, PipelineFailure>.Succeeded)built).Value, projects);
        var missing = projects
            .Where(project => located[project] is Result<PreparedProject, PipelineFailure>.Failed)
            .ToArray();
        if (missing.Length == 0)
        {
            return Assemble(projects, located);
        }

        var rebuilt = await builds.RebuildAsync(missing, workDirectory).ConfigureAwait(false);
        if (rebuilt is Result<string, PipelineFailure>.Failed(var rebuildFailure))
        {
            return Failure(rebuildFailure);
        }

        foreach (var (project, result) in Locate(((Result<string, PipelineFailure>.Succeeded)rebuilt).Value, missing))
        {
            located[project] = result;
        }

        return Assemble(projects, located);
    }

    /// <summary>一つの binlog からの対象ごとの特定</summary>
    private Dictionary<TargetProjects, Result<PreparedProject, PipelineFailure>> Locate(
        string binlogPath,
        IReadOnlyList<TargetProjects> projects
    )
    {
        var located = new Dictionary<TargetProjects, Result<PreparedProject, PipelineFailure>>();
        foreach (var project in projects)
        {
            located[project] = locations.Locate(binlogPath, project) switch
            {
                Result<(CscInvocation Sut, string TestAssembly), PipelineFailure>.Succeeded succeeded =>
                    new Result<PreparedProject, PipelineFailure>.Succeeded(
                        new PreparedProject(project, succeeded.Value.Sut, succeeded.Value.TestAssembly)
                    ),
                Result<(CscInvocation Sut, string TestAssembly), PipelineFailure>.Failed failed =>
                    new Result<PreparedProject, PipelineFailure>.Failed(failed.Failure),
            };
        }

        return located;
    }

    /// <summary>入力順への組み直し</summary>
    private static Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure> Assemble(
        IReadOnlyList<TargetProjects> projects,
        Dictionary<TargetProjects, Result<PreparedProject, PipelineFailure>> located
    ) =>
        new Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure>.Succeeded(
            [.. projects.Select(project => located[project])]
        );

    /// <summary>全対象に及ぶ失敗の包み</summary>
    private static Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure> Failure(
        PipelineFailure failure
    ) => new Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure>.Failed(failure);
}

/// <summary>build と特定を終えた対象一件</summary>
/// <param name="Projects">対象とテストの project の特定</param>
/// <param name="Sut">対象 project の csc 呼び出し</param>
/// <param name="TestAssembly">テスト assembly の実行時 path</param>
public sealed record PreparedProject(TargetProjects Projects, CscInvocation Sut, string TestAssembly);
