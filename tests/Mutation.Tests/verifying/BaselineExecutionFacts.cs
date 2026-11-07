using Mutation.Protocol;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>複数 shard の基準観測の集計と不正な発見応答の拒否の検査</summary>
/// <remarks>
/// 台本に基づく実 process の worker による観測
/// 連番ごとに異なる状態と所要時間を持つ 16 件の入力
/// </remarks>
public sealed class BaselineExecutionFacts
{
    /// <summary>複数 shard の収集に使うテストの件数</summary>
    private const int TestCount = 16;

    /// <summary>並行に走らせる worker 数</summary>
    private const int Concurrency = 2;

    /// <summary>テストごとに固有に被覆される変異の連番の起点</summary>
    private const int OwnMutantBase = 100;

    /// <summary>複数の shard にまたがるテストが共に被覆する変異の連番</summary>
    private const int SharedMutant = 7;

    /// <summary>二つの shard のテストがそれぞれ static 初期化の区間で観測した変異の連番</summary>
    private const int StaticMutant = 50;

    /// <summary>一件のテストだけが static 初期化の区間で観測した変異の連番</summary>
    private const int StaticOnlyMutant = 51;

    /// <summary>連番 0 のテストの境界外で観測される変異連番</summary>
    private const int AmbientOfFirstTest = 900;

    /// <summary>連番 1 のテストの境界外で観測される変異連番</summary>
    private const int AmbientOfSecondTest = 901;

    /// <summary>StaticMutant を観測する一方の shard のテスト連番</summary>
    private const int FirstTriggerTest = 6;

    /// <summary>二つの static 変異を観測する他方の shard のテスト連番</summary>
    private const int SecondTriggerTest = 9;

    /// <summary>baseline の収集失敗を返すテストの連番</summary>
    private const int FailingTest = 5;

    /// <summary>連番 0 のテストの所要時間</summary>
    private const double BaseDurationMs = 20;

    /// <summary>二進で正確に表せる所要時間の連番ごとの増分</summary>
    private const double DurationStepMs = 1.5;

    /// <summary>連番 0 のテストの probe 呼び出し回数</summary>
    private const long BaseProbeCount = 1000;

    /// <summary>連番が一つ進むごとの probe 呼び出し回数の増分</summary>
    private const long ProbeStep = 7;

    /// <summary>二つの shard にまたがる baseline 失敗のテスト連番</summary>
    private static readonly int[] FailedTests = [3, 8, 13];

    /// <summary>二つの shard にまたがる共有変異の被覆テスト連番</summary>
    private static readonly int[] SharedCoverageTests = [2, 5, 8, 11, 14];

    /// <summary>全 shard の観測が実行計画と被覆の帰属に反映されることの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Observations_of_every_shard_are_kept_with_their_coverage_attribution_and_outcome()
    {
        using var run = new ScriptedWorkerRun(
            [
                ScriptedWorkerRun.DiscoverAnswer(new WorkerResponse.Discovered(AllIds())),
                .. BaselineScript(Enumerable.Range(0, TestCount)),
            ]
        );

        var execution = await run.BaselineAsync(Concurrency);

        await Assert.That(execution.Outcome.IsFailed(out _, out var profile)).IsFalse();
        var mutant = new Mutant(
            new MutantId(SharedMutant), "op", Path.GetFullPath("subject.cs"),
            new SourceSpan(1, 1, 1, 6), "a + b", "a - b", false);
        var planned = MutantScheduler.Plan([mutant], profile!.Coverage, profile.Tests, excludeStatic: false);
        var (order, budget) = planned.Plans.Single().Execution.ResidentSlice;
        await Assert.That(order.Select(profile.Roster.Id).SequenceEqual(["suite-02", "suite-05", "suite-11", "suite-14"])).IsTrue();
        await Assert.That(budget).IsEqualTo(2256d);
        await Assert.That(HitLimits.For(profile.Roster, order)).IsEqualTo(319600L);

        await Assert.That(CoveringIds(profile, SharedMutant))
            .IsEquivalentTo(SharedCoverageTests.Select(IdOf).Order(StringComparer.Ordinal).ToArray());
        await Assert.That(
                Enumerable.Range(0, TestCount)
                    .Select(test => string.Join(',', CoveringIds(profile, OwnMutant(test))))
                    .SequenceEqual(AllIds())
            )
            .IsTrue();
        await Assert.That(CoveringIds(profile, StaticMutant)).IsEmpty();
        await Assert.That(CoveringIds(profile, AmbientOfSecondTest)).IsEmpty();

        await Assert.That(TriggeringIds(profile, StaticMutant)).IsEquivalentTo([IdOf(FirstTriggerTest), IdOf(SecondTriggerTest)]);
        await Assert.That(TriggeringIds(profile, StaticOnlyMutant)).IsEquivalentTo([IdOf(SecondTriggerTest)]);
        await Assert.That(TriggeringIds(profile, SharedMutant)).IsEmpty();
        await Assert.That(profile.Coverage.AmbientMutants.Select(mutant => mutant.Value).ToArray())
            .IsEquivalentTo([StaticMutant, StaticOnlyMutant, AmbientOfFirstTest, AmbientOfSecondTest]);

        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>一つの shard の収集失敗を他の成功で隠さないことの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_failing_shard_fails_the_baseline_with_the_worker_reason()
    {
        const string workerError = "Shard.Boom.4b8e0a17";
        using var run = new ScriptedWorkerRun(
            [
                ScriptedWorkerRun.DiscoverAnswer(new WorkerResponse.Discovered(AllIds())),
                .. BaselineScript(Enumerable.Range(0, TestCount).Where(test => test != FailingTest)),
                ScriptedWorkerRun.BaselineFailure(IdOf(FailingTest), workerError),
            ]
        );

        var execution = await run.BaselineAsync(Concurrency);

        await Assert.That(WorkerFailureReason(execution)).Contains(workerError);
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>不正な種類の発見応答を入力データ付きの診断で拒否することの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task A_discover_answer_of_another_kind_is_rejected_naming_the_offending_data()
    {
        const string offendingKiller = "Offending.Killer.9d3f1c7a";
        using var run = new ScriptedWorkerRun(
            ScriptedWorkerRun.DiscoverAnswer(new WorkerResponse.RunCompleted(WorkerContract.OutcomeKilled, offendingKiller, 1, 1))
        );

        var execution = await run.BaselineAsync(Concurrency);

        await Assert.That(WorkerFailureReason(execution)).Contains(offendingKiller);
        await Assert.That(execution.BaselineRuns()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>発見がゼロ件の場合に空の名簿で成功扱いしないことの検査</summary>
    /// <returns>worker の終了まで含む非同期検査</returns>
    [Test]
    public async Task Discovering_no_tests_fails_naming_the_searched_test_assembly()
    {
        using var run = new ScriptedWorkerRun(ScriptedWorkerRun.DiscoverAnswer(new WorkerResponse.Discovered([])));

        var execution = await run.BaselineAsync(Concurrency);

        await Assert.That(execution.Outcome.IsFailed(out var failed, out _)).IsTrue();
        var searched = failed.Failure is PipelineFailure.NoTestsFound { TestAssembly: var assembly } ? assembly : null;
        await Assert.That(searched).IsEqualTo(run.TestAssemblyPath);
        await Assert.That(execution.BaselineRuns()).IsEmpty();
        await Assert.That(execution.SurvivingProcesses).IsEmpty();
    }

    /// <summary>連番順と辞書順が一致するテスト識別子の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>収集対象のテスト識別子</returns>
    private static string IdOf(int test) => $"suite-{test:D2}";

    /// <summary>実行時の識別子と区別する表示名の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>テスト実行体が報告する表示名</returns>
    private static string NameOf(int test) => $"Calc.Case{test:D2}";

    /// <summary>発見対象の全テスト識別子の組み立て</summary>
    /// <returns>連番順の発見対象の識別子</returns>
    private static string[] AllIds() => Enumerable.Range(0, TestCount).Select(IdOf).ToArray();

    /// <summary>テストごとに固有の変異連番の算出</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>他のテストと重ならない変異連番</returns>
    private static int OwnMutant(int test) => OwnMutantBase + test;

    /// <summary>テストごとに異なる基準実行時間の算出</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>ミリ秒単位の基準実行時間</returns>
    private static double DurationOf(int test) => BaseDurationMs + (test * DurationStepMs);

    /// <summary>テストごとに異なる probe 回数の算出</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>基準実行時の probe 呼び出し回数</returns>
    private static long ProbeCountOf(int test) => BaseProbeCount + (test * ProbeStep);

    /// <summary>通常実行で被覆する固有・共有変異の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>通常実行で被覆する変異連番の列</returns>
    private static int[] HitsOf(int test) => SharedCoverageTests.Contains(test) ? [OwnMutant(test), SharedMutant] : [OwnMutant(test)];

    /// <summary>static 初期化で観測する変異連番の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>static 初期化で観測する変異連番の列</returns>
    private static int[] StaticHitsOf(int test) =>
        test switch
        {
            FirstTriggerTest => [StaticMutant],
            SecondTriggerTest => [StaticMutant, StaticOnlyMutant],
            _ => [],
        };

    /// <summary>テストの実行境界の外で観測する変異連番の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>実行境界の外で観測する変異連番の列</returns>
    private static int[] AmbientOf(int test) =>
        test switch
        {
            0 => [AmbientOfFirstTest],
            1 => [AmbientOfSecondTest],
            _ => [],
        };

    /// <summary>台本の worker が返すテスト観測の組み立て</summary>
    /// <param name="test">0 起点のテスト連番</param>
    /// <returns>実行状態と被覆を含むテスト一件の観測</returns>
    private static WorkerTestResult Observation(int test) =>
        new(IdOf(test), NameOf(test), DurationOf(test), !FailedTests.Contains(test), HitsOf(test), ProbeCountOf(test), StaticHitsOf(test));

    /// <summary>テストごとの基準観測の台本の組み立て</summary>
    /// <param name="tests">観測を用意する 0 起点のテスト連番</param>
    /// <returns>テスト識別子ごとの観測応答</returns>
    private static IEnumerable<(string Key, WorkerResponse Answer)> BaselineScript(IEnumerable<int> tests) =>
        tests.Select(test => ScriptedWorkerRun.BaselineAnswer(Observation(test), AmbientOf(test)));

    /// <summary>変異を被覆するテスト識別子の昇順の列の取得</summary>
    /// <param name="profile">収集済みの被覆と名簿</param>
    /// <param name="mutant">対象の変異連番</param>
    /// <returns>名簿から復元した被覆テストの識別子</returns>
    private static string[] CoveringIds(BaselineProfile profile, int mutant) =>
        profile.Coverage.CoveringTests(new MutantId(mutant)).Select(profile.Roster.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>static 初期化を引き起こしたテスト識別子の取得</summary>
    /// <param name="profile">収集済みの被覆と名簿</param>
    /// <param name="mutant">対象の変異連番</param>
    /// <returns>昇順のテスト識別子の列</returns>
    private static string[] TriggeringIds(BaselineProfile profile, int mutant) =>
        profile.Coverage.TriggeringTests(new MutantId(mutant)).Select(profile.Roster.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>worker として報告された収集失敗の理由の取得</summary>
    /// <param name="execution">基準観測の収集結果</param>
    /// <returns>worker の失敗理由またはそれ以外の結果での不在</returns>
    private static string? WorkerFailureReason(ScriptedBaseline execution) =>
        execution.Outcome.IsFailed(out var failed, out _) && failed.Failure is PipelineFailure.WorkerFailed { Reason: var reason }
            ? reason
            : null;
}
