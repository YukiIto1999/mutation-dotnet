using Mutation.Mutating.Application;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>git 差分による IChangeSets の adapter</summary>
public sealed class ChangeSets : IChangeSets
{
    /// <inheritdoc />
    public Result<IReadOnlySet<string>, PipelineFailure> Resolve(string projectDirectory, string sinceRef) =>
        ChangedFiles.Resolve(projectDirectory, sinceRef);
}
