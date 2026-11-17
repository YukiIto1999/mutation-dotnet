using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>取り消しを受けた走行が対象ごとに何を始め何を始めないかと 基準実行の失敗の帰結の検査</summary>
/// <remarks>資源に触れない port の代役の途中で取り消しを発し 対象の帰結と代役への呼び出しを見る</remarks>
public sealed class RunMutationTestingCancellationFacts
{
    /// <summary>取り消しの時点でまだ検査に取りかからない対象が 前回の保存との照合にも入らず中断になること</summary>
    /// <remarks>二件目の対象は前回の保存と完全一致するので 照合に入れば再構築されて完了になる</remarks>
    [Test]
    public async Task A_target_not_yet_started_when_the_run_is_cancelled_is_interrupted_without_consulting_its_snapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var profile = FakePipeline.ProfileOf([FakePipeline.MutantOf(0)]);
        var pipeline = new FakePipeline("First", "Second")
        {
            Baseline = profile,
            OnObserve = _ =>
            {
                cancellation.Cancel();
                return Result.Succeeded(profile);
            },
            OnRebuild = name => name == "Second" ? FakePipeline.RebuiltResult(name, [FakePipeline.MutantOf(0)]) : null,
        };

        var outcomes = await RunAsync(pipeline, withBaseline: true, cancellation.Token);

        await Assert.That(outcomes[0] is TargetOutcome.Completed).IsTrue();
        await Assert.That(outcomes[1] is TargetOutcome.Failed { Target: "Second", Failure: PipelineFailure.Interrupted }).IsTrue();
        await Assert.That(pipeline.Consulted.ToArray()).IsEquivalentTo(["First"]);
    }

    /// <summary>前回の保存の照合の最中に取り消しを受けた対象が 変異を生成せず中断になること</summary>
    [Test]
    public async Task A_cancellation_during_the_snapshot_check_stops_the_target_before_it_generates_mutants()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new FakePipeline("Only")
        {
            OnRebuild = _ =>
            {
                cancellation.Cancel();
                return null;
            },
        };

        var outcomes = await RunAsync(pipeline, withBaseline: true, cancellation.Token);

        await Assert.That(outcomes.Single() is TargetOutcome.Failed { Failure: PipelineFailure.Interrupted }).IsTrue();
        await Assert.That(pipeline.Generated).IsEmpty();
    }

    /// <summary>取り消しの無い走行の基準実行の失敗が その失敗のまま対象の帰結になること</summary>
    [Test]
    public async Task A_baseline_failure_without_a_cancellation_is_the_outcome_of_the_target()
    {
        var pipeline = new FakePipeline("Only") { OnObserve = _ => Failing("baseline worker did not open") };

        var outcomes = await RunAsync(pipeline, withBaseline: false, CancellationToken.None);

        await Assert.That(outcomes.Single() is TargetOutcome.Failed { Failure: PipelineFailure.WorkerFailed { Reason: "baseline worker did not open" } })
            .IsTrue();
        await Assert.That(pipeline.Executed).IsEmpty();
    }

    /// <summary>取り消しで止まった基準実行の失敗が 失敗でなく中断として対象の帰結になること</summary>
    [Test]
    public async Task A_baseline_stopped_by_the_cancellation_is_an_interruption_of_the_target()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new FakePipeline("Only")
        {
            OnObserve = _ =>
            {
                cancellation.Cancel();
                return Failing("baseline worker was stopped");
            },
        };

        var outcomes = await RunAsync(pipeline, withBaseline: false, cancellation.Token);

        await Assert.That(outcomes.Single() is TargetOutcome.Failed { Failure: PipelineFailure.Interrupted }).IsTrue();
        await Assert.That(pipeline.Executed).IsEmpty();
    }

    /// <summary>基準実行の失敗の結果</summary>
    private static Result<BaselineProfile, PipelineFailure> Failing(string reason) =>
        Result.Failed<PipelineFailure>(new PipelineFailure.WorkerFailed(reason));

    /// <summary>一回の走行と 成功したときの対象ごとの帰結</summary>
    private static async Task<IReadOnlyList<TargetOutcome>> RunAsync(
        FakePipeline pipeline,
        bool withBaseline,
        CancellationToken cancellation
    )
    {
        var run = await pipeline.Workflow.ExecuteAsync(pipeline.Request(withBaseline), _ => { }, cancellation);
        await Assert.That(run.IsFailed(out _, out var result)).IsFalse();
        return result!.Outcomes;
    }
}
