using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker process 群による IWorkerRuns の adapter</summary>
public sealed class WorkerRuns : IWorkerRuns
{
    /// <inheritdoc />
    public Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(WorkerLaunchPlan launch, int concurrency) =>
        BaselineExecution.RunAsync(launch, concurrency);

    /// <inheritdoc />
    public async Task<OrchestrationResult> ExecuteAsync(
        WorkerLaunchPlan launch,
        TestRoster roster,
        IReadOnlyList<MutantPlan> plans,
        int concurrency,
        Action<RunProgress> progress
    )
    {
        using var orchestrator = new TestingOrchestrator(launch, roster, progress);
        return await orchestrator.ExecuteAsync(plans, concurrency).ConfigureAwait(false);
    }
}
