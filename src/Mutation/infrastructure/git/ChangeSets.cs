using Mutation.Application;
using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Git;

/// <summary>git 差分による IChangeSets の adapter</summary>
public sealed class ChangeSets : IChangeSets
{
    /// <inheritdoc />
    public Result<ChangedLines, PipelineFailure> Resolve(string projectDirectory, string sinceRef) =>
        ChangedFiles.Resolve(projectDirectory, sinceRef);
}
