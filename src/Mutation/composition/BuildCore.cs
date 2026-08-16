using Mutation.Mutating.Application;
using Mutation.Mutating.Infrastructure;
using Mutation.Mutating.Infrastructure.Git;
using Mutation.Mutating.Infrastructure.Msbuild;
using Mutation.Mutating.Infrastructure.Roslyn;
using Mutation.Verifying.Application;
using Mutation.Verifying.Infrastructure.Reports;
using Mutation.Verifying.Infrastructure.Snapshot;
using Mutation.Verifying.Infrastructure.Workers;

namespace Mutation.Composition;

/// <summary>文脈の adapter を port へ配線して組み立て済みの API を返す、core の組立点</summary>
public static class BuildCore
{
    /// <summary>全 adapter の配線と公開 operation の組み立て</summary>
    /// <returns>外部へ公開する変異検査の operation</returns>
    public static MutationTesting Create()
    {
        var workerRuns = new WorkerRuns();
        var snapshots = new Snapshots();
        var mutantSource = new MutantSourceFromMutating(
            new PrepareTarget(new BinlogBuilds(), new TargetLocations()),
            new GenerateMutants(new MutationCompilation(), new ChangeSets()),
            new Fingerprints()
        );
        return new MutationTesting(
            new RunMutationTesting(
                mutantSource,
                new VerifyMutants(workerRuns, snapshots),
                workerRuns,
                snapshots,
                new VerificationReports()
            )
        );
    }
}
