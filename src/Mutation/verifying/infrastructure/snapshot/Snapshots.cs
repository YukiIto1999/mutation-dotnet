using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Infrastructure.Snapshot;

/// <summary>snapshot file による ISnapshots の adapter</summary>
public sealed class Snapshots : ISnapshots
{
    /// <inheritdoc />
    public MutationRunResult? TryRebuild(
        string snapshotPath,
        string? fingerprint,
        string testAssemblyPath,
        string mutatedAssemblyPath,
        PhaseTimings timings
    )
    {
        var document = SnapshotStore.Load(snapshotPath);
        if (!SnapshotReconstruction.IsFullMatch(document, fingerprint, testAssemblyPath, mutatedAssemblyPath))
        {
            return null;
        }

        return SnapshotReconstruction.Rebuild(document!, timings);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(
        string snapshotPath,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    ) => SnapshotReuse.Inheritable(SnapshotStore.Load(snapshotPath), mutants, testAssemblyPath, coverage, roster);

    /// <inheritdoc />
    public void Save(SnapshotSaveRequest request) =>
        SnapshotStore.Save(
            request.Mutants,
            request.Verdicts,
            request.Baseline.Tests,
            request.Baseline.Coverage,
            new SnapshotContext(
                request.TestAssemblyPath,
                request.Fingerprint,
                request.MutatedAssemblyPath,
                request.CompileErrorIds,
                request.Baseline.Roster,
                request.SnapshotPath
            )
        );
}
