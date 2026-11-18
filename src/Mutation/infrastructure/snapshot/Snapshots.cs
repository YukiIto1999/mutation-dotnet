using Mutation.Domain;
using Mutation.Application;

namespace Mutation.Infrastructure.Snapshot;

/// <summary>snapshot file による ISnapshots の adapter</summary>
public sealed class Snapshots : ISnapshots
{
    /// <inheritdoc />
    public TargetResult? TryRebuild(
        string snapshotPath,
        ReuseIdentity? identity,
        string testAssemblyPath,
        string mutatedAssemblyPath,
        string targetName
    )
    {
        var document = SnapshotStore.Load(snapshotPath);
        if (!SnapshotReconstruction.IsFullMatch(document, identity?.Fingerprint, testAssemblyPath, mutatedAssemblyPath))
        {
            return null;
        }

        return SnapshotReconstruction.Rebuild(document!, targetName);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(
        string snapshotPath,
        ReuseIdentity? identity,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    ) =>
        SnapshotReuse.Inheritable(
            SnapshotStore.Load(snapshotPath),
            identity?.Closure,
            mutants,
            testAssemblyPath,
            coverage,
            roster
        );

    /// <inheritdoc />
    public IVerdictJournal Begin(SnapshotSaveRequest checkpoint) => new SnapshotJournal(checkpoint);
}
