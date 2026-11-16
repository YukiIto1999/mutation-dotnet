using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>worker process 群による IWorkerRuns の adapter</summary>
public sealed class WorkerRuns : IWorkerRuns
{
    /// <inheritdoc />
    public Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(
        WorkerLaunchPlan launch,
        int concurrency,
        CancellationToken cancellation
    ) => BaselineExecution.RunAsync(launch, concurrency, cancellation);

    /// <inheritdoc />
    public async Task<OrchestrationResult> ExecuteAsync(
        WorkerLaunchPlan launch,
        TestRoster roster,
        IReadOnlyList<MutantPlan> plans,
        int concurrency,
        bool detectionMatrix,
        Action<RunProgress> progress,
        ExecutionControl control
    )
    {
        using var orchestrator = new TestingOrchestrator(launch, roster, progress, detectionMatrix);
        return await orchestrator.ExecuteAsync(plans, concurrency, control).ConfigureAwait(false);
    }
}
