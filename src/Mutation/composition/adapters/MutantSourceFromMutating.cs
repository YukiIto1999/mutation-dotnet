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
    /// <summary>PrepareAsync が特定し Generate が使う csc 呼び出し</summary>
    private CscInvocation? prepared;

    /// <inheritdoc />
    public async Task<Result<PreparedTarget, PipelineFailure>> PrepareAsync(
        TargetRequest target,
        string workDirectory,
        string? fingerprintSettings
    )
    {
        var projects = new TargetProjects(target.ProjectPath, target.TestProjectPath, target.Configuration);
        var located = await prepareTarget.ExecuteAsync(projects, workDirectory).ConfigureAwait(false);
        if (located is Result<(CscInvocation Sut, string TestAssembly), PipelineFailure>.Failed(var failure))
        {
            return new Result<PreparedTarget, PipelineFailure>.Failed(failure);
        }

        var (sut, testAssembly) = ((Result<(CscInvocation, string), PipelineFailure>.Succeeded)located).Value;
        prepared = sut;
        var fingerprint = fingerprintSettings is null ? null : fingerprints.Compute(sut, fingerprintSettings);
        return new Result<PreparedTarget, PipelineFailure>.Succeeded(
            new PreparedTarget(testAssembly, Path.GetFileNameWithoutExtension(sut.OutputPath), fingerprint)
        );
    }

    /// <inheritdoc />
    public Result<GeneratedMutants, PipelineFailure> Generate(SelectionRequest selection, string mutatedDirectory)
    {
        var sut = prepared ?? throw new InvalidOperationException("PrepareAsync の成功が先に要る");
        var compiled = generateMutants.Execute(
            sut,
            new MutationSelection(selection.MutatePatterns, selection.SinceRef, selection.IgnoredOperators, selection.IgnoredMethods),
            selection.ProjectPath,
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
