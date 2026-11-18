using Mutation.Application;
using Mutation.Infrastructure;
using Mutation.Infrastructure.Git;
using Mutation.Infrastructure.Msbuild;
using Mutation.Infrastructure.Roslyn;
using Mutation.Infrastructure.Reports;
using Mutation.Infrastructure.Snapshot;
using Mutation.Infrastructure.Workers;

namespace Mutation.Composition;

/// <summary>変異検査の公開 application 入口と外部依存の組み立て</summary>
public static class BuildCore
{
    /// <summary>全 adapter の配線と公開 operation の組み立て</summary>
    /// <returns>外部へ公開する変異検査の operation</returns>
    public static MutationApi Create()
    {
        var workerRuns = new WorkerRuns();
        var snapshots = new Snapshots();
        var mutantSource = new MutationSource(
            new PrepareTarget(new BinlogBuilds(), new TargetLocations()),
            new GenerateMutants(new MutationCompilation(), new ChangeSets()),
            new Fingerprints()
        );
        return new MutationApi(
            new RunMutationTesting(
                mutantSource,
                new VerifyMutants(workerRuns, snapshots),
                workerRuns,
                snapshots,
                new VerificationReports()
            ),
            new JudgeChangedLines(new RecordedReports(), new ChangedLinesFromGit())
        );
    }
}
