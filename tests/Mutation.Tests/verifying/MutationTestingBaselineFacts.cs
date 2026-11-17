using Mutation.Composition;
using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>前回の実行の保存を読む走行と 読まない走行の境界の検査</summary>
/// <remarks>検出行列は被覆する全テストの実行でしか得られないので 前回の判定を使う指定と同時に求められても 保存を読まない</remarks>
public sealed class MutationTestingBaselineFacts
{
    /// <summary>前回の保存を読むのが 継承を求め かつ検出行列を求めない走行だけであること</summary>
    /// <param name="withBaseline">前回実行の保存から判定を継承する指定</param>
    /// <param name="detectionMatrix">検出行列を求める指定</param>
    /// <param name="reads">前回の保存を読む走行か</param>
    /// <returns>走行まで含む非同期検査</returns>
    [Test]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    public async Task The_previous_snapshot_is_read_only_by_a_baseline_run_that_is_not_a_detection_matrix(
        bool withBaseline,
        bool detectionMatrix,
        bool reads
    )
    {
        var pipeline = new FakePipeline("Only");
        var operation = new MutationTesting(pipeline.Workflow, new JudgeChangedLines(new UnusedReports(), new UnusedChanges()));
        var target = pipeline.TargetRequests().Single();
        var options = new MutationRunOptions(
            [new TargetSpec(target.ProjectPath, target.TestProjectPath)],
            "Debug",
            1,
            false,
            false,
            [],
            null,
            false,
            [],
            [],
            "/out",
            withBaseline,
            detectionMatrix
        );

        var run = await operation.RunAsync(options, _ => { });

        await Assert.That(run.IsFailed(out _, out _)).IsFalse();
        await Assert.That(pipeline.Consulted.Count > 0).IsEqualTo(reads);
        await Assert.That(pipeline.Inherited > 0).IsEqualTo(reads);
    }

    /// <summary>この検査では使わない報告の読み戻し</summary>
    private sealed class UnusedReports : IRecordedReports
    {
        /// <inheritdoc />
        public Result<RecordedReport, PipelineFailure> Read(string reportPath) => throw new NotSupportedException();
    }

    /// <summary>この検査では使わない変わった行の解決</summary>
    private sealed class UnusedChanges : IChangedLines
    {
        /// <inheritdoc />
        public Result<ChangedLines, PipelineFailure> Resolve(string directory, string sinceRef) => throw new NotSupportedException();
    }
}
