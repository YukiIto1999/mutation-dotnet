using Mutation.Domain;
using Mutation.Application;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Git;

/// <summary>検査の IChangedLines を、`--since` と同じ git 差分の解決へ写す adapter</summary>
public sealed class ChangedLinesFromGit : IChangedLines
{
    /// <inheritdoc />
    public Result<ChangedLines, PipelineFailure> Resolve(string directory, string sinceRef) =>
        ChangedFiles.Resolve(directory, sinceRef);
}
