using Mutation.Verifying.Domain;
using Mutation.Protocol;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>起動計画から protocol 指示への写像</summary>
public static class WorkerRequests
{
    /// <summary>ホストを開く init 指示の組み立て</summary>
    /// <param name="launch">worker の起動情報</param>
    /// <returns>起動直後の worker へ送る指示</returns>
    public static WorkerRequest.Init Init(WorkerLaunchPlan launch) =>
        new(launch.TestAssemblyPath, launch.MutatedDirectory, launch.TargetAssemblyName);
}
