using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;

namespace Mutation.Mutating.Infrastructure;

/// <summary>指紋計算による IFingerprints の adapter</summary>
public sealed class Fingerprints : IFingerprints
{
    /// <inheritdoc />
    public ReuseIdentity? Compute(
        CscInvocation invocation,
        string testAssemblyPath,
        string settings,
        ChangedLines? changes,
        string selection
    ) => CompilationFingerprint.Compute(invocation, testAssemblyPath, settings, changes, selection);
}
