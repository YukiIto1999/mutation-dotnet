using Mutation.Mutating.Application;
using Mutation.Mutating.Domain;
using Mutation.Shared;

namespace Mutation.Mutating.Infrastructure;

/// <summary>指紋計算による IFingerprints の adapter</summary>
public sealed class Fingerprints : IFingerprints
{
    /// <inheritdoc />
    public string? Compute(CscInvocation invocation, string settings, ChangedLines? changes) =>
        CompilationFingerprint.Compute(invocation, settings, changes);
}
