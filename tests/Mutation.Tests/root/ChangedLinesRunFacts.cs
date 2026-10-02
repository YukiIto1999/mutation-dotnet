using Mutation.Composition;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

public sealed partial class ChangedLinesGateFacts
{
    /// <summary>変更行を生成で選ぶ場合もファイル単位の既定と報告の判定を保つこと</summary>
    [Test]
    public async Task Run_generates_only_changed_line_mutants_and_retains_the_gate()
    {
        using var repo = await TempRepository.CreateAsync();
        var baseRef = await CommitFixtureAsync(repo);
        var options = new MutationRunOptions(
            [FixtureTarget(repo)], "Debug", 2, false, false, [], baseRef, true, [], [],
            repo.PathOf("out/lines"), true
        );
        var result = await RunAsync(options);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Completed.Single().Mutants.Select(m => m.Span.Line).ToArray()).IsEquivalentTo([11]);
        var judged = BuildCore.Create().OnChangedLines(
            repo.PathOf("out/lines/reports/mutation-report.json"), baseRef);
        await Assert.That(judged is Result<ChangedLineVerdicts, PipelineFailure>.Succeeded).IsTrue();
        await Assert.That(((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)judged).Value.Undetected.Count)
            .IsEqualTo(1);

        var fileResult = await RunAsync(options with { ChangedLinesOnly = false });
        await Assert.That(fileResult.Failures).IsEmpty();
        await Assert.That(fileResult.Completed.Single().Mutants.Any(m => m.Span.Line == 9)).IsTrue();

        var missingResult = await RunAsync(
            options with { SinceRef = null, OutputDirectory = repo.PathOf("out/missing-base") });
        await Assert.That(missingResult.Failures.Single().Failure is PipelineFailure.SinceUnavailable).IsTrue();
        await Assert.That(((PipelineFailure.SinceUnavailable)missingResult.Failures.Single().Failure).Reason)
            .Contains("--since");
    }

    /// <summary>`--with-baseline` の差分運用が、同じ名前の基点の移動と消失を解き直し、前回の結果を流用しないこと</summary>
    [Test]
    public async Task Baseline_run_resolves_a_moved_or_deleted_base_again()
    {
        using var repo = await TempRepository.CreateAsync();
        const string upstream = "refs/remotes/origin/main";
        await repo.SetRefAsync(upstream, await CommitFixtureAsync(repo));
        var options = new MutationRunOptions(
            [FixtureTarget(repo)], "Debug", 2, false, false, [], "origin/main", true, [], [],
            repo.PathOf("out/lines"), true
        );
        var first = await RunAsync(options);
        await Assert.That(first.Completed.Single().Mutants.Select(m => m.Span.Line).ToArray()).IsEquivalentTo([11]);

        await repo.SetRefAsync(upstream, await repo.CommitAllAsync());
        var moved = await RunAsync(options);
        await Assert.That(moved.Completed.Single().Mutants).IsEmpty();

        await repo.DeleteRefAsync(upstream);
        var deleted = await RunAsync(options);
        await Assert.That(deleted.Completed).IsEmpty();
        await Assert.That(deleted.Failures.Single().Failure is PipelineFailure.SinceUnavailable).IsTrue();
    }

    /// <summary>同じ出力先での差分運用の実行が全量の実行の保存を上書きせず、次の全量の実行が前回の結果を再利用すること</summary>
    [Test]
    public async Task Since_runs_keep_the_whole_run_snapshot_reusable()
    {
        using var repo = await TempRepository.CreateAsync();
        var baseRef = await CommitFixtureAsync(repo);
        var whole = new MutationRunOptions(
            [FixtureTarget(repo)], "Debug", 2, false, false, [], null, false, [], [],
            repo.PathOf("out/shared"), true
        );
        var first = await RunAsync(whole);
        await RunAsync(whole with { SinceRef = baseRef });
        await RunAsync(whole with { SinceRef = baseRef, ChangedLinesOnly = true });

        var progress = new List<RunProgress>();
        var last = await RunAsync(whole, progress.Add);

        await Assert.That(progress.OfType<RunProgress.SnapshotMatched>().Count()).IsEqualTo(1);
        await Assert.That(last.Completed.Single().Mutants.Count).IsEqualTo(first.Completed.Single().Mutants.Count);
    }

    /// <summary>検証フィクスチャを書いて commit し、生存する 11 行目だけを作業木で書き換えた、その commit の id</summary>
    private static async Task<string> CommitFixtureAsync(TempRepository repo)
    {
        var fixture = FixturePath();
        foreach (var relative in new[]
        {
            "Directory.Build.props",
            "Fixture.Target/Fixture.Target.csproj",
            "Fixture.Target/Calculator.cs",
            "Fixture.Target/Configured.cs",
            "Fixture.Target.Tests/Fixture.Target.Tests.csproj",
            "Fixture.Target.Tests/CalculatorTests.cs",
        })
        {
            await repo.WriteAsync(Path.Combine("src", relative), await File.ReadAllTextAsync(Path.Combine(fixture, relative)));
        }

        await repo.WriteAsync(".gitignore", "bin/\nobj/\nout/\n");
        var baseRef = await repo.CommitAllAsync();
        var calculator = repo.PathOf("src/Fixture.Target/Calculator.cs");
        var source = await File.ReadAllLinesAsync(calculator);
        source[10] += " // changed";
        await File.WriteAllLinesAsync(calculator, source);
        return baseRef;
    }

    /// <summary>一時 repository に書いた検証フィクスチャの対象とテストの project</summary>
    private static TargetSpec FixtureTarget(TempRepository repo) =>
        new(
            repo.PathOf("src/Fixture.Target/Fixture.Target.csproj"),
            repo.PathOf("src/Fixture.Target.Tests/Fixture.Target.Tests.csproj")
        );

    /// <summary>組み立て済みの経路での一回の実行と、成功したときの確定結果</summary>
    private static async Task<MutationRunResult> RunAsync(MutationRunOptions options, Action<RunProgress>? progress = null)
    {
        var run = await BuildCore.Create().RunAsync(options, progress ?? (_ => { }));
        await Assert.That(run is Result<MutationRunResult, PipelineFailure>.Succeeded).IsTrue();
        return ((Result<MutationRunResult, PipelineFailure>.Succeeded)run).Value;
    }

    /// <summary>変異検査の source fixture の directory</summary>
    private static string FixturePath()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Mutation.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repo root が見つからない");
        }

        return Path.Combine(root, "tests/e2e/medium/verification");
    }
}
