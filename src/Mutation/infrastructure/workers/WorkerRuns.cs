using Mutation.Domain;
using Mutation.Application;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Workers;

/// <summary>worker process 群による IWorkerRuns の adapter</summary>
/// <param name="host">worker を起動する host</param>
public sealed class WorkerRuns(WorkerHost host) : IWorkerRuns
{
    /// <summary>この process の環境と配置から決めた既定の host で worker を起動する adapter</summary>
    public WorkerRuns()
        : this(DefaultWorkerHost.Resolve()) { }

    /// <inheritdoc />
    public WorkerHost Host { get; } = host;

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
        using var orchestrator = new TestingOrchestrator(launch, roster, progress, detectionMatrix, RunDeadlines.Standard);
        return await orchestrator.ExecuteAsync(plans, concurrency, control).ConfigureAwait(false);
    }
}
