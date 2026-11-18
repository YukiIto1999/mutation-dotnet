using System.Text.Json;
using Mutation.Domain;
using Mutation.Application;
using Mutation.Infrastructure.Reports;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>全対象に及ぶ準備の失敗が 報告と返り値と取り消しの優先にどう現れるかの検査</summary>
/// <remarks>準備だけを失敗させる変異一式の代役を 他の port の代役と組み合わせて workflow の公開の入口から通す</remarks>
public sealed class RunMutationTestingPreparationFailureFacts
{
    /// <summary>前の走行の成功した報告が 準備の失敗で 失敗して完了でなく対象を名指す報告に置き換わること</summary>
    [Test]
    public async Task A_failed_preparation_replaces_the_report_left_by_a_successful_run()
    {
        var output = Directory.CreateTempSubdirectory("mutation-preparation-failure").FullName;
        try
        {
            var pipeline = new FakePipeline("Only");
            var request = pipeline.Request() with { OutputDirectory = output };
            await WorkflowOf(pipeline, pipeline, new VerificationReports())
                .ExecuteAsync(request, _ => { }, CancellationToken.None);

            await WorkflowOf(new FailingPreparation(new PipelineFailure.BuildFailed("error CS0001")), pipeline, new VerificationReports())
                .ExecuteAsync(request, _ => { }, CancellationToken.None);

            await Assert.That(await RunRecordAsync(output)).IsEqualTo("failed complete=False failed=Only");
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    /// <summary>取り消しの無い準備の失敗が 成功の結果へ変わらず その失敗のまま返ること</summary>
    [Test]
    public async Task A_failed_preparation_without_a_cancellation_stays_the_typed_failure_of_the_run()
    {
        var pipeline = new FakePipeline("Only");
        var workflow = WorkflowOf(new FailingPreparation(new PipelineFailure.BuildFailed("error CS0001")), pipeline, pipeline);

        var run = await workflow.ExecuteAsync(pipeline.Request(), _ => { }, CancellationToken.None);

        await Assert.That(run.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.BuildFailed { Detail: "error CS0001" })
            .IsTrue();
    }

    /// <summary>準備の最中に取り消しを受け 準備が失敗しても 走行が失敗でなく中断として返ること</summary>
    [Test]
    public async Task A_cancellation_during_a_failing_preparation_takes_priority_over_the_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new FakePipeline("Only");
        var workflow = WorkflowOf(
            new FailingPreparation(new PipelineFailure.BuildFailed("error CS0001"), cancellation.Cancel),
            pipeline,
            pipeline
        );

        var run = await workflow.ExecuteAsync(pipeline.Request(), _ => { }, cancellation.Token);

        await Assert.That(run.IsFailed(out var failed, out var result) ? $"failed: {failed.Failure}" : result.State)
            .IsEqualTo("interrupted");
    }

    /// <summary>準備に変異一式の代役を使い 他の port には同じ代役を使う workflow</summary>
    private static RunMutationTesting WorkflowOf(IMutantSource source, FakePipeline pipeline, IVerificationReports reports) =>
        new(source, new VerifyMutants(pipeline, pipeline), pipeline, pipeline, reports);

    /// <summary>書き出された報告の run の欄の 状態と完了と失敗した対象の一行への要約</summary>
    private static async Task<string> RunRecordAsync(string output)
    {
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "reports", "mutation-report.json")));
        var run = report.RootElement.GetProperty("run");
        var failedTargets = run.GetProperty("failedTargets").EnumerateArray().Select(target => target.GetString());
        return $"{run.GetProperty("state").GetString()} complete={run.GetProperty("complete").GetBoolean()} failed={string.Join(",", failedTargets)}";
    }

    /// <summary>決めた失敗を準備の結果として返す変異一式の代役。準備の最中に起こす出来事を差し込める</summary>
    /// <param name="failure">準備の結果にする全対象に及ぶ失敗</param>
    /// <param name="whilePreparing">準備の最中に起こす出来事。なければ不在</param>
    private sealed class FailingPreparation(PipelineFailure failure, Action? whilePreparing = null) : IMutantSource
    {
        /// <inheritdoc />
        public Task<Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>> PrepareAsync(
            IReadOnlyList<TargetProjects> targets,
            MutationSelection selection,
            string workDirectory,
            ExecutionSettings execution,
            WorkerHost host
        )
        {
            whilePreparing?.Invoke();
            Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure> failed =
                Result.Failed<PipelineFailure>(failure);
            return Task.FromResult(failed);
        }

    }
}
