using Mutation.Shared;
using Mutation.Verifying.Application;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>変異検査の workflow を port の代役だけで通す検査環境</summary>
/// <remarks>
/// 資源に触れない代役なので 取り消しや失敗を段階の途中に差し込める
/// 対象ごとの準備と継承の照合と生成と基準実行と変異の実行が 何に何回呼ばれたかの記録
/// 既定の応答は 全変異を名簿の先頭のテストが被覆し 実行した変異を全て検出する
/// </remarks>
internal sealed class FakePipeline : IMutantSource, IWorkerRuns, ISnapshots, IVerificationReports
{
    /// <summary>名簿の先頭のテストの表示名</summary>
    public const string TestName = "Calc.Add";

    /// <summary>対象 project の名前の列。並べた順に検査する</summary>
    private readonly string[] names;

    /// <summary>既定の基準実行の観測。変異の一覧が決まってから組み立てる</summary>
    private BaselineProfile? baseline;

    /// <summary>対象 project の名前を決めた検査環境</summary>
    /// <param name="names">対象 project の名前の列</param>
    public FakePipeline(params string[] names) => this.names = names;

    /// <summary>対象ごとの変異の一覧。連番 0 の一件だけが既定</summary>
    public IReadOnlyList<Mutant> Mutants { get; init; } = [MutantOf(0)];

    /// <summary>rollback で無効化された変異の連番</summary>
    public IReadOnlySet<MutantId> CompileErrors { get; init; } = new HashSet<MutantId>();

    /// <summary>基準実行の観測。既定は全変異を名簿の先頭のテストが被覆する観測</summary>
    public BaselineProfile Baseline
    {
        get => baseline ??= ProfileOf(Mutants);
        init => baseline = value;
    }

    /// <summary>対象の名前から、前回の保存との完全一致で再構築する結果への対応。既定は一致しない</summary>
    public Func<string, TargetResult?> OnRebuild { get; init; } = _ => null;

    /// <summary>対象の名前から、基準実行の結果への対応。既定は成功</summary>
    public Func<string, Result<BaselineProfile, PipelineFailure>>? OnObserve { get; init; }

    /// <summary>計画から、実行の結果への対応。既定は計画の全変異を検出</summary>
    public Func<IReadOnlyList<MutantPlan>, OrchestrationResult>? OnExecute { get; init; }

    /// <summary>準備が受けた指紋の設定。呼び出しごとに一つ</summary>
    public List<string?> Fingerprinted { get; } = [];

    /// <summary>変異を生成した対象の名前</summary>
    public List<string> Generated { get; } = [];

    /// <summary>前回の保存との照合を求められた対象の名前</summary>
    public List<string> Consulted { get; } = [];

    /// <summary>基準実行を求められた対象の名前</summary>
    public List<string> Observed { get; } = [];

    /// <summary>変異の実行を求められた計画の呼び出しごとの列</summary>
    public List<IReadOnlyList<MutantPlan>> Executed { get; } = [];

    /// <summary>判定の継承を求められた回数</summary>
    public int Inherited { get; private set; }

    /// <summary>対象の指定の列</summary>
    /// <returns>対象 project の名前ごとの指定</returns>
    public IReadOnlyList<TargetRequest> TargetRequests() =>
        names.Select(name => new TargetRequest($"/work/{name}/{name}.csproj", $"/work/{name}/{name}.Tests.csproj", "Debug")).ToArray();

    /// <summary>この環境の代役を全ての port に使った workflow</summary>
    public RunMutationTesting Workflow => new(this, new VerifyMutants(this, this), this, this, this);

    /// <summary>名簿の先頭のテストだけを持つ名簿</summary>
    public static TestRoster Roster { get; } = new(["test-0"], [10]);

    /// <summary>連番だけが違う、一つのソースの変異</summary>
    /// <param name="id">変異の連番</param>
    /// <returns>static 文脈にない変異</returns>
    public static Mutant MutantOf(int id) =>
        new(new MutantId(id), "BinaryOperatorMutator", "/work/Target.cs", new SourceSpan(1, 30, 1, 35), "a + b", "a - b", false);

    /// <summary>指定の変異を名簿の先頭のテストが被覆する基準実行の観測</summary>
    /// <param name="covered">被覆される変異</param>
    /// <returns>名簿と被覆を持つ観測</returns>
    public static BaselineProfile ProfileOf(IEnumerable<Mutant> covered) =>
        new(
            [new TestCaseInfo(new TestIndex(0), TestName, 1, true)],
            Roster,
            new CoverageMap(
                covered.ToDictionary(m => m.Id, _ => (IReadOnlyList<TestIndex>)[new TestIndex(0)]),
                new HashSet<MutantId>(),
                null
            )
        );

    /// <summary>前回の保存から再構築された体の、全変異を検出した確定結果</summary>
    /// <param name="name">対象 project の名前</param>
    /// <param name="mutants">対象の変異の一覧</param>
    /// <returns>全変異が検出の確定結果</returns>
    public static TargetResult RebuiltResult(string name, IReadOnlyList<Mutant> mutants) =>
        new(
            name,
            mutants,
            mutants.ToDictionary(m => m.Id, _ => (MutantVerdict)new MutantVerdict.Killed(TestName)),
            [],
            TargetTimings.None,
            [],
            0,
            new Dictionary<MutantId, DetectionRow>()
        );

    /// <summary>この環境の対象を検査する要求。指紋は継承を使うときだけ添える</summary>
    /// <param name="withBaseline">前回実行の保存から判定を継承するか</param>
    /// <param name="execution">実行と判定の制御。既定は一本の lane で再検証なし</param>
    /// <returns>変異対象を絞らない一回の検査の入力</returns>
    public RunRequest Request(bool withBaseline = false, ExecutionSettings? execution = null) =>
        new(
            TargetRequests(),
            new SelectionRequest([], null, false, [], []),
            execution ?? new ExecutionSettings(1, false, false, false),
            withBaseline ? "settings" : null,
            "/out",
            withBaseline
        );

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>> PrepareAsync(
        IReadOnlyList<TargetRequest> targets,
        SelectionRequest selection,
        string workDirectory,
        string? fingerprintSettings
    )
    {
        Fingerprinted.Add(fingerprintSettings);
        var prepared = targets
            .Zip(names)
            .Select(pair => PreparedOf(pair.First, pair.Second, fingerprintSettings))
            .ToList();
        Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure> succeeded =
            Result.Succeeded<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>>(prepared);
        return Task.FromResult(succeeded);
    }

    /// <inheritdoc />
    public Result<GeneratedMutants, PipelineFailure> Generate(PreparedTarget target, string mutatedDirectory)
    {
        Generated.Add(target.TargetAssemblyName);
        return Result.Succeeded(
            new GeneratedMutants(Mutants, CompileErrors, Path.Combine(mutatedDirectory, target.TargetAssemblyName + ".dll"), 0, 0)
        );
    }

    /// <inheritdoc />
    public Task<Result<BaselineProfile, PipelineFailure>> ObserveAsync(
        WorkerLaunchPlan launch,
        int concurrency,
        CancellationToken cancellation
    )
    {
        Observed.Add(launch.TargetAssemblyName);
        Result<BaselineProfile, PipelineFailure> observed = OnObserve is { } behavior
            ? behavior(launch.TargetAssemblyName)
            : Result.Succeeded(Baseline);
        return Task.FromResult(observed);
    }

    /// <inheritdoc />
    public Task<OrchestrationResult> ExecuteAsync(
        WorkerLaunchPlan launch,
        TestRoster roster,
        IReadOnlyList<MutantPlan> plans,
        int concurrency,
        bool detectionMatrix,
        Action<RunProgress> progress,
        ExecutionControl control
    )
    {
        Executed.Add(plans);
        return Task.FromResult(OnExecute is { } behavior ? behavior(plans) : Detecting(plans));
    }

    /// <inheritdoc />
    public TargetResult? TryRebuild(
        string snapshotPath,
        string? fingerprint,
        string testAssemblyPath,
        string mutatedAssemblyPath,
        string targetName
    )
    {
        Consulted.Add(targetName);
        return OnRebuild(targetName);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(
        string snapshotPath,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    )
    {
        Inherited++;
        return new Dictionary<MutantId, MutantVerdict>();
    }

    /// <inheritdoc />
    public IVerdictJournal Begin(SnapshotSaveRequest checkpoint) => new SilentJournal();

    /// <inheritdoc />
    public void Write(MutationRunResult result, string projectRoot, string reportsDirectory)
    {
    }

    /// <summary>計画した全変異を検出した実行の結果</summary>
    /// <param name="plans">実行する変異計画の列</param>
    /// <returns>計画の全変異の判定を持つ結果</returns>
    public static OrchestrationResult Detecting(IReadOnlyList<MutantPlan> plans) =>
        Verdicts(plans, _ => new MutantVerdict.Killed(TestName));

    /// <summary>計画ごとの判定を決めた実行の結果</summary>
    /// <param name="plans">実行する変異計画の列</param>
    /// <param name="verdict">計画から判定への対応</param>
    /// <returns>計画の全変異の判定を持つ結果</returns>
    public static OrchestrationResult Verdicts(IReadOnlyList<MutantPlan> plans, Func<MutantPlan, MutantVerdict> verdict) =>
        new(plans.ToDictionary(plan => plan.MutantId, verdict), [], 0, new Dictionary<MutantId, DetectionRow>());

    /// <summary>準備に成功した対象一件</summary>
    private static Result<PreparedTarget, PipelineFailure> PreparedOf(TargetRequest request, string name, string? fingerprint) =>
        Result.Succeeded(new PreparedTarget(request, $"/work/{name}/{name}.Tests.dll", name, fingerprint));

    /// <summary>追記を受け流し 保存もしない判定の記録</summary>
    private sealed class SilentJournal : IVerdictJournal
    {
        /// <inheritdoc />
        public void Append(MutantId id, MutantVerdict verdict)
        {
        }

        /// <inheritdoc />
        public void Commit(SnapshotSaveRequest final)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
