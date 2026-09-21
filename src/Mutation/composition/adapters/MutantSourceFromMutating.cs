using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;
using Mutation.Verifying.Application;
using TypeModeling.Domain;

namespace Mutation.Composition;

/// <summary>検査の IMutantSource を、変異文脈の公開 application へ写す adapter</summary>
/// <param name="prepareTarget">対象特定の use-case</param>
/// <param name="generateMutants">変異生成の use-case</param>
/// <param name="fingerprints">指紋計算の port 実装</param>
internal sealed class MutantSourceFromMutating(
    PrepareTarget prepareTarget,
    GenerateMutants generateMutants,
    IFingerprints fingerprints
) : IMutantSource
{
    /// <summary>PrepareAsync が特定し Generate が使う、対象 project の path から csc 呼び出しへの対応</summary>
    private readonly Dictionary<string, CscInvocation> prepared = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>> PrepareAsync(
        IReadOnlyList<TargetRequest> targets,
        string workDirectory,
        string? fingerprintSettings
    )
    {
        var projects = targets
            .Select(t => new TargetProjects(t.ProjectPath, t.TestProjectPath, t.Configuration))
            .ToArray();
        var located = await prepareTarget.ExecuteAsync(projects, workDirectory).ConfigureAwait(false);
        if (
            located
            is Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure>.Failed(var failure)
        )
        {
            return new Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>.Failed(failure);
        }

        var projectResults = (
            (Result<IReadOnlyList<Result<PreparedProject, PipelineFailure>>, PipelineFailure>.Succeeded)located
        ).Value;
        var results = new List<Result<PreparedTarget, PipelineFailure>>(targets.Count);
        foreach (var (target, project) in targets.Zip(projectResults))
        {
            results.Add(Describe(target, project, fingerprintSettings));
        }

        return new Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>.Succeeded(results);
    }

    /// <summary>対象一件の準備の結果の写し。成功なら csc 呼び出しの控えと指紋の算出</summary>
    private Result<PreparedTarget, PipelineFailure> Describe(
        TargetRequest target,
        Result<PreparedProject, PipelineFailure> project,
        string? fingerprintSettings
    )
    {
        if (project is Result<PreparedProject, PipelineFailure>.Failed failed)
        {
            return new Result<PreparedTarget, PipelineFailure>.Failed(failed.Failure);
        }

        var value = ((Result<PreparedProject, PipelineFailure>.Succeeded)project).Value;
        prepared[target.ProjectPath] = value.Sut;
        return new Result<PreparedTarget, PipelineFailure>.Succeeded(
            new PreparedTarget(
                target,
                value.TestAssembly,
                Path.GetFileNameWithoutExtension(value.Sut.OutputPath),
                fingerprintSettings is null ? null : fingerprints.Compute(value.Sut, fingerprintSettings)
            )
        );
    }

    /// <inheritdoc />
    public Result<GeneratedMutants, PipelineFailure> Generate(
        PreparedTarget target,
        SelectionRequest selection,
        string mutatedDirectory
    )
    {
        var sut = prepared.TryGetValue(target.Request.ProjectPath, out var found)
            ? found
            : throw new InvalidOperationException("PrepareAsync の成功が先に要る");
        var compiled = generateMutants.Execute(
            sut,
            new MutationSelection(
                selection.MutatePatterns,
                selection.SinceRef,
                selection.IgnoredOperators,
                selection.IgnoredMethods
            ),
            target.Request.ProjectPath,
            mutatedDirectory
        );
        if (compiled is Result<MutatedArtifact, PipelineFailure>.Failed(var failure))
        {
            return new Result<GeneratedMutants, PipelineFailure>.Failed(failure);
        }

        var artifact = ((Result<MutatedArtifact, PipelineFailure>.Succeeded)compiled).Value;
        return new Result<GeneratedMutants, PipelineFailure>.Succeeded(
            new GeneratedMutants(
                artifact.Mutants,
                artifact.CompileErrorIds,
                artifact.MutatedAssemblyPath,
                artifact.MutateMs,
                artifact.CompileMs
            )
        );
    }
}
