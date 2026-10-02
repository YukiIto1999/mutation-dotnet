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

        var target = new TargetSpec(
            repo.PathOf("src/Fixture.Target/Fixture.Target.csproj"),
            repo.PathOf("src/Fixture.Target.Tests/Fixture.Target.Tests.csproj")
        );
        var options = new MutationRunOptions(
            [target], "Debug", 2, false, false, [], baseRef, true, [], [],
            repo.PathOf("out/lines"), true
        );
        var run = await BuildCore.Create().RunAsync(options, _ => { });
        await Assert.That(run is Result<MutationRunResult, PipelineFailure>.Succeeded).IsTrue();
        var result = ((Result<MutationRunResult, PipelineFailure>.Succeeded)run).Value;
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.Completed.Single().Mutants.Select(m => m.Span.Line).ToArray()).IsEquivalentTo([11]);
        var judged = BuildCore.Create().OnChangedLines(
            repo.PathOf("out/lines/reports/mutation-report.json"), baseRef);
        await Assert.That(judged is Result<ChangedLineVerdicts, PipelineFailure>.Succeeded).IsTrue();
        await Assert.That(((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)judged).Value.Undetected.Count)
            .IsEqualTo(1);

        var fileRun = await BuildCore.Create().RunAsync(
            options with { ChangedLinesOnly = false }, _ => { });
        await Assert.That(fileRun is Result<MutationRunResult, PipelineFailure>.Succeeded).IsTrue();
        var fileResult = ((Result<MutationRunResult, PipelineFailure>.Succeeded)fileRun).Value;
        await Assert.That(fileResult.Failures).IsEmpty();
        await Assert.That(fileResult.Completed.Single().Mutants.Any(m => m.Span.Line == 9)).IsTrue();

        var missingBase = await BuildCore.Create().RunAsync(
            options with { SinceRef = null, OutputDirectory = repo.PathOf("out/missing-base") }, _ => { });
        await Assert.That(missingBase is Result<MutationRunResult, PipelineFailure>.Succeeded).IsTrue();
        var missingResult = ((Result<MutationRunResult, PipelineFailure>.Succeeded)missingBase).Value;
        await Assert.That(missingResult.Failures.Single().Failure is PipelineFailure.SinceUnavailable).IsTrue();
        await Assert.That(((PipelineFailure.SinceUnavailable)missingResult.Failures.Single().Failure).Reason)
            .Contains("--since");
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
