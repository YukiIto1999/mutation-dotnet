using Mutation.Mutating.Infrastructure.Git;
using Mutation.Shared;
using Mutation.Verifying.Application;
using TypeModeling.Domain;

namespace Mutation.Composition;

/// <summary>検査の IChangedLines を、`--since` と同じ git 差分の解決へ写す adapter</summary>
internal sealed class ChangedLinesFromGit : IChangedLines
{
    /// <inheritdoc />
    public Result<ChangedLines, PipelineFailure> Resolve(string directory, string sinceRef) =>
        ChangedFiles.Resolve(directory, sinceRef);
}
