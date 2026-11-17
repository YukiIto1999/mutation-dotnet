using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TUnit.Assertions.Enums;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定の確定の workflow が 実行前に決まる判定と実行の判定を合わせることと 生存の再検証の検査</summary>
public sealed class VerifyMutantsFacts
{
    /// <summary>無効化 即時の未被覆 static 除外の判定が 実行した変異の判定と並ぶこと</summary>
    /// <remarks>連番 0 は static で除外 1 は無効化 2 は被覆されて検出 3 は被覆されない</remarks>
    [Test]
    public async Task Mutants_settled_before_execution_keep_their_verdicts_beside_the_executed_ones()
    {
        var excluded = FakePipeline.MutantOf(0) with { InStaticContext = true };
        var covered = FakePipeline.MutantOf(2);
        var pipeline = new FakePipeline("Target")
        {
            Mutants = [excluded, FakePipeline.MutantOf(1), covered, FakePipeline.MutantOf(3)],
            CompileErrors = new HashSet<MutantId> { new(1) },
            Baseline = FakePipeline.ProfileOf([covered]),
        };

        var result = await new VerifyMutants(pipeline, pipeline).ExecuteAsync(
            Request(pipeline, new ExecutionSettings(1, ValidateSurvivors: false, ExcludeStatic: true, DetectionMatrix: false))
        );

        await Assert.That(result.Verdicts.OrderBy(pair => pair.Key.Value).Select(pair => pair.Value).ToArray())
            .IsEquivalentTo(
                new MutantVerdict[]
                {
                    new MutantVerdict.Ignored(),
                    new MutantVerdict.CompileError(),
                    new MutantVerdict.Killed(FakePipeline.TestName),
                    new MutantVerdict.NoCoverage(),
                },
                CollectionOrdering.Matching
            );
        await Assert.That(pipeline.Executed.Single().Select(plan => plan.MutantId.Value).ToArray()).IsEquivalentTo([2]);
    }

    /// <summary>生存の再検証を求めた走行が 生存した変異だけを新規 process の実行形で実行し直すこと</summary>
    /// <remarks>連番 0 は検出 1 と 2 は共有の実行で生存</remarks>
    [Test]
    public async Task Survivors_alone_are_run_again_in_fresh_processes_when_validation_is_requested()
    {
        var pipeline = SurvivorsPipeline();

        await new VerifyMutants(pipeline, pipeline).ExecuteAsync(Request(pipeline, Validating));

        await Assert.That(pipeline.Executed.Count).IsEqualTo(2);
        await Assert.That(pipeline.Executed[1].Select(plan => plan.MutantId.Value).ToArray()).IsEquivalentTo([1, 2]);
        await Assert.That(pipeline.Executed[1].All(plan => plan.Execution is MutantExecution.Isolated)).IsTrue();
    }

    /// <summary>新規 process の実行で検出された生存が検出になり 他の変異の判定は変わらないこと</summary>
    /// <remarks>連番 1 は再検証で検出 連番 2 は再検証でも生存 連番 0 は再検証の対象でない</remarks>
    [Test]
    public async Task A_survivor_detected_by_the_fresh_process_becomes_detected_and_the_other_verdicts_stay()
    {
        var pipeline = SurvivorsPipeline();

        var result = await new VerifyMutants(pipeline, pipeline).ExecuteAsync(Request(pipeline, Validating));

        await Assert.That(result.Verdicts.OrderBy(pair => pair.Key.Value).Select(pair => pair.Value).ToArray())
            .IsEquivalentTo(
                new MutantVerdict[]
                {
                    new MutantVerdict.Killed(FakePipeline.TestName),
                    new MutantVerdict.Killed("fresh"),
                    new MutantVerdict.Survived(),
                },
                CollectionOrdering.Matching
            );
    }

    /// <summary>生存の再検証を求める実行と判定の制御</summary>
    private static ExecutionSettings Validating { get; } = new(1, ValidateSurvivors: true, ExcludeStatic: false, DetectionMatrix: false);

    /// <summary>三件の変異を持ち 共有の実行では連番 0 だけが検出され 新規 process では連番 1 だけが検出される環境</summary>
    private static FakePipeline SurvivorsPipeline()
    {
        var mutants = new[] { FakePipeline.MutantOf(0), FakePipeline.MutantOf(1), FakePipeline.MutantOf(2) };
        return new FakePipeline("Target")
        {
            Mutants = mutants,
            OnExecute = plans => FakePipeline.Verdicts(plans, Verdict),
        };
    }

    /// <summary>実行形と連番による台本の判定</summary>
    private static MutantVerdict Verdict(MutantPlan plan) =>
        (plan.Execution is MutantExecution.Isolated, plan.MutantId.Value) switch
        {
            (false, 0) => new MutantVerdict.Killed(FakePipeline.TestName),
            (true, 1) => new MutantVerdict.Killed("fresh"),
            _ => new MutantVerdict.Survived(),
        };

    /// <summary>この環境の変異の判定確定の入力。前回の保存は使わない</summary>
    private static VerificationRequest Request(FakePipeline pipeline, ExecutionSettings execution) =>
        new(
            pipeline.Mutants,
            pipeline.CompileErrors,
            pipeline.Baseline,
            new WorkerLaunchPlan("worker.dll", "tests.dll", "/mutated", "Target", "/results"),
            execution,
            WithBaseline: false,
            Fingerprint: null,
            MutatedAssemblyPath: "/mutated/Target.dll",
            SnapshotPath: "/work/snapshot.json",
            _ => { },
            CancellationToken.None
        );
}
