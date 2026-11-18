using Mutation.Application;
using Mutation.Composition;
using Mutation.Domain;
using TUnit.Core;

namespace Mutation.Tests;

public sealed partial class ChangedLinesGateFacts
{
    /// <summary>変更行を生成で選ぶ場合もファイル単位の既定と報告の判定を保つこと</summary>
    [Test]
    public async Task Run_generates_only_changed_line_mutants_and_retains_the_gate()
    {
        using var repo = await TempRepository.CreateAsync();
        var baseRef = await CommitFixtureAsync(repo);
        var options = new RunRequest([FixtureTarget(repo)], new MutationSelection([], baseRef, true, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/lines"), true);
        var result = await RunAsync(options);
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Completed.Single().Mutants.Select(m => m.Span.Line).ToArray()).IsEquivalentTo([11]);
        var judged = BuildCore.Create().ChangedLines.Execute(repo.PathOf("out/lines/reports/mutation-report.json"), baseRef);
        await Assert.That(judged.IsFailed(out _, out var verdicts)).IsFalse();
        await Assert.That(verdicts!.Undetected.Count).IsEqualTo(1);

        var fileResult = await RunAsync(options with { Selection = options.Selection with { ChangedLinesOnly = false } });
        await Assert.That(fileResult.Failures).IsEmpty();
        await Assert.That(fileResult.Completed.Single().Mutants.Any(m => m.Span.Line == 9)).IsTrue();

        var missingResult = await RunAsync(
            options with { Selection = options.Selection with { SinceRef = null }, OutputDirectory = repo.PathOf("out/missing-base") });
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
        var options = new RunRequest([FixtureTarget(repo)], new MutationSelection([], "origin/main", true, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/lines"), true);
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
        var whole = new RunRequest([FixtureTarget(repo)], new MutationSelection([], null, false, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/shared"), true);
        var first = await RunAsync(whole);
        await RunAsync(whole with { Selection = whole.Selection with { SinceRef = baseRef } });
        await RunAsync(whole with { Selection = whole.Selection with { SinceRef = baseRef, ChangedLinesOnly = true } });

        var progress = new List<RunProgress>();
        var last = await RunAsync(whole, progress.Add);

        await Assert.That(progress.OfType<RunProgress.SnapshotMatched>().Count()).IsEqualTo(1);
        await Assert.That(last.Completed.Single().Mutants.Count).IsEqualTo(first.Completed.Single().Mutants.Count);
    }

    /// <summary>テストだけの変更が、影響なしにならず対象の全範囲を選び、明示した変更行の部分集合では広げずにテストの変更を未分析として報告に残すこと</summary>
    [Test]
    public async Task A_test_only_change_selects_the_whole_scope_and_the_explicit_subset_reports_it_unanalysed()
    {
        using var repo = await TempRepository.CreateAsync();
        await WriteFixtureAsync(repo);
        var baseRef = await repo.CommitAllAsync();
        await File.AppendAllTextAsync(repo.PathOf("src/Fixture.Target.Tests/CalculatorTests.cs"), "\n// a test changed\n");
        var options = new RunRequest([FixtureTarget(repo)], new MutationSelection([], baseRef, false, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/test-only"), false);

        var whole = await RunAsync(options);
        var subset = await RunAsync(options with { Selection = options.Selection with { ChangedLinesOnly = true }, OutputDirectory = repo.PathOf("out/test-only-lines") });

        var selected = whole.Completed.Single();
        await Assert.That(selected.Selection!.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(RelationNames(selected.Selection.Reasons)).IsEquivalentTo(["test-changed"]);
        await Assert.That(selected.Mutants.Any(m => m.Span.Line == 9)).IsTrue();
        var narrowed = subset.Completed.Single();
        await Assert.That(narrowed.Selection!.Scope is SelectionScope.ExplicitSubset).IsTrue();
        await Assert.That(narrowed.Mutants).IsEmpty();
        await Assert.That(RelationNames(narrowed.Selection.Unanalysed)).IsEquivalentTo(["test-changed"]);
    }

    /// <summary>関係が不明な未追跡ファイル、build 設定、削除がそれぞれ対象の全範囲を選ぶこと</summary>
    [Test]
    public async Task Unknown_inputs_configuration_and_removed_files_select_the_whole_scope()
    {
        using var repo = await TempRepository.CreateAsync();
        await WriteFixtureAsync(repo);
        await repo.WriteAsync("src/Fixture.Target/Spare.cs", "namespace Fixture.Target;\n\ninternal static class Spare\n{\n}\n");
        var baseRef = await repo.CommitAllAsync();
        var options = new RunRequest([FixtureTarget(repo)], new MutationSelection([], baseRef, false, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/unknown"), false);

        await repo.WriteAsync("docs/guide.md", "documentation only\n");
        var unknown = (await RunAsync(options)).Completed.Single();
        File.Delete(repo.PathOf("docs/guide.md"));

        var props = repo.PathOf("src/Directory.Build.props");
        await File.WriteAllTextAsync(
            props,
            (await File.ReadAllTextAsync(props)).Replace("<PropertyGroup>", "<!-- configuration changed -->\n  <PropertyGroup>", StringComparison.Ordinal)
        );
        var configured = (await RunAsync(options with { OutputDirectory = repo.PathOf("out/configured") })).Completed.Single();

        File.Delete(repo.PathOf("src/Fixture.Target/Spare.cs"));
        var removed = (await RunAsync(options with { OutputDirectory = repo.PathOf("out/removed") })).Completed.Single();

        await Assert.That(unknown.Selection!.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(unknown.Mutants.Any(m => m.Span.Line == 9)).IsTrue();
        await Assert.That(RelationNames(unknown.Selection.Reasons)).IsEquivalentTo(["unknown-input"]);
        await Assert.That(configured.Selection!.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(RelationNames(configured.Selection.Reasons)).IsEquivalentTo(["config-changed"]);
        await Assert.That(configured.Mutants.Any(m => m.Span.Line == 9)).IsTrue();
        await Assert.That(removed.Selection!.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(RelationNames(removed.Selection.Reasons)).IsEquivalentTo(["config-changed", "file-removed"]);
    }

    /// <summary>対象が参照する project のソースの変更が、対象のソースが変わらなくても、対象の全範囲を選び、変更行の部分集合では未分析に残ること</summary>
    [Test]
    public async Task A_change_in_a_referenced_project_selects_the_whole_scope_and_is_left_unanalysed_by_the_subset()
    {
        using var repo = await TempRepository.CreateAsync();
        await WriteFixtureAsync(repo);
        await repo.WriteAsync("src/Fixture.Lib/Fixture.Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n");
        await repo.WriteAsync("src/Fixture.Lib/Lib.cs", "namespace Fixture.Lib;\n\npublic static class Lib\n{\n    public const int Offset = 1;\n}\n");
        await repo.WriteAsync(
            "src/Fixture.Target/Fixture.Target.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"../Fixture.Lib/Fixture.Lib.csproj\" />\n  </ItemGroup>\n</Project>\n"
        );
        var baseRef = await repo.CommitAllAsync();
        await File.AppendAllTextAsync(repo.PathOf("src/Fixture.Lib/Lib.cs"), "\n// the library changed\n");
        var options = new RunRequest([FixtureTarget(repo)], new MutationSelection([], baseRef, false, [], []), new ExecutionSettings(2, false, false, false), repo.PathOf("out/dependency"), false);

        var whole = (await RunAsync(options)).Completed.Single();
        var subset = (await RunAsync(options with { Selection = options.Selection with { ChangedLinesOnly = true }, OutputDirectory = repo.PathOf("out/dependency-lines") })).Completed.Single();

        await Assert.That(whole.Selection!.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(RelationNames(whole.Selection.Reasons)).IsEquivalentTo(["dependency-changed"]);
        await Assert.That(whole.Mutants.Any(m => m.Span.Line == 9)).IsTrue();
        await Assert.That(subset.Selection!.Scope is SelectionScope.ExplicitSubset).IsTrue();
        await Assert.That(subset.Mutants).IsEmpty();
        await Assert.That(RelationNames(subset.Selection.Unanalysed)).IsEquivalentTo(["dependency-changed"]);
    }

    /// <summary>検証フィクスチャを書いて commit し、生存する 11 行目だけを作業木で書き換えた、その commit の id</summary>
    private static async Task<string> CommitFixtureAsync(TempRepository repo)
    {
        await WriteFixtureAsync(repo);
        var baseRef = await repo.CommitAllAsync();
        var calculator = repo.PathOf("src/Fixture.Target/Calculator.cs");
        var source = await File.ReadAllLinesAsync(calculator);
        source[10] += " // changed";
        await File.WriteAllLinesAsync(calculator, source);
        return baseRef;
    }

    /// <summary>検証フィクスチャと .gitignore の作業木への書き込み。commit はしない</summary>
    private static async Task WriteFixtureAsync(TempRepository repo)
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
    }

    /// <summary>関係の名前の並び</summary>
    private static string[] RelationNames(IReadOnlyList<ImpactReason> reasons) =>
        [.. reasons.Select(reason => reason.Relation.Name).Order(StringComparer.Ordinal)];

    /// <summary>一時 repository に書いた検証フィクスチャの対象とテストの project</summary>
    private static TargetProjects FixtureTarget(TempRepository repo) =>
        new(
            repo.PathOf("src/Fixture.Target/Fixture.Target.csproj"),
            repo.PathOf("src/Fixture.Target.Tests/Fixture.Target.Tests.csproj"),
            "Debug"
        );

    /// <summary>組み立て済みの経路での一回の実行と、成功したときの確定結果</summary>
    private static async Task<MutationRunResult> RunAsync(RunRequest options, Action<RunProgress>? progress = null)
    {
        var run = await BuildCore.Create().Run.ExecuteAsync(options, progress ?? (_ => { }));
        await Assert.That(run.IsFailed(out _, out var result)).IsFalse();
        return result!;
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
