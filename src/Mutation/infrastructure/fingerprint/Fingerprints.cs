using Mutation.Application;
using Mutation.Domain;

namespace Mutation.Infrastructure;

/// <summary>指紋計算による IFingerprints の adapter</summary>
public sealed class Fingerprints : IFingerprints
{
    /// <inheritdoc />
    public RunClosure? Capture(ReuseSettings settings, string outputDirectory, WorkerHost worker) =>
        CompilationFingerprint.Capture(settings, outputDirectory, worker);

    /// <inheritdoc />
    public ReuseIdentity? Compute(
        RunClosure run,
        CscInvocation invocation,
        string testAssemblyPath,
        ChangedLines? changes,
        string selection
    ) => CompilationFingerprint.Compute(run, invocation, testAssemblyPath, changes, selection);
}
