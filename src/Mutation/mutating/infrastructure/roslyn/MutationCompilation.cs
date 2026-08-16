using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>Roslyn 再構築と schemata コンパイルによる IMutationCompilation の adapter</summary>
public sealed class MutationCompilation : IMutationCompilation
{
    /// <inheritdoc />
    public Result<MutatedArtifact, PipelineFailure> Compile(
        CscInvocation sut,
        string mutatedDirectory,
        MutationPolicy policy
    ) => SchemataCompiler.Compile(TargetCompilationLoader.Load(sut), mutatedDirectory, policy);
}
