using Mutation.Application;
using Mutation.Domain;
using Mutation.Infrastructure;
using Mutation.Infrastructure.Git;
using Mutation.Infrastructure.Roslyn;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>同じ project の準備を重ねたときの生成入力の隔離</summary>
public sealed class MutationSourceFacts
{
    /// <summary>後の準備で演算子を除外しても、先に準備した対象の算術変異が失われないこと</summary>
    [Test]
    public async Task Preparing_the_same_project_again_does_not_replace_an_earlier_targets_selection()
    {
        using var fixture = new PreparedProjectFixture();
        var source = new MutationSource(
            new PrepareTarget(fixture, fixture),
            new GenerateMutants(new MutationCompilation(), new ChangeSets()),
            new Fingerprints()
        );
        var selection = new MutationSelection([], null, false, [], []);
        var execution = new ExecutionSettings(1, false, false, false);
        var host = new WorkerHost("/missing/dotnet", "/missing/worker.dll");
        var first = await source.PrepareAsync([fixture.Projects], selection, fixture.Root, execution, host);
        var later = await source.PrepareAsync(
            [fixture.Projects], selection with { IgnoredOperators = ["BinaryOperatorMutator"] }, fixture.Root, execution, host
        );
        await Assert.That(later.IsFailed(out _, out _)).IsFalse();
        await Assert.That(first.IsFailed(out _, out var prepared)).IsFalse();
        await Assert.That(prepared!.Single().IsFailed(out _, out var target)).IsFalse();

        var generated = target!.Generate(Path.Combine(fixture.Root, "mutated"));

        await Assert.That(generated.IsFailed(out _, out var artifact)).IsFalse();
        await Assert.That(artifact!.Mutants.Any(mutant => mutant.OperatorName == "BinaryOperatorMutator")).IsTrue();
    }

    /// <summary>build の入出力だけを置き換え、生成は実際の Roslyn で行う一時 project</summary>
    private sealed class PreparedProjectFixture : IBinlogBuilds, ILocateTargets, IDisposable
    {
        /// <summary>所有する一時 directory</summary>
        public string Root { get; } = Directory.CreateTempSubdirectory("mutation-prepared-input").FullName;

        /// <summary>同じ path で準備を重ねる対象</summary>
        public TargetProjects Projects => new(Path.Combine(Root, "Fixture.csproj"), Path.Combine(Root, "Tests.csproj"), "Debug");

        /// <inheritdoc />
        public Task<Result<string, PipelineFailure>> BuildAsync(IReadOnlyList<TargetProjects> projects, string workDirectory) =>
            Task.FromResult<Result<string, PipelineFailure>>(Result.Succeeded("prepared.binlog"));

        /// <inheritdoc />
        public Task<Result<string, PipelineFailure>> RebuildAsync(IReadOnlyList<TargetProjects> projects, string workDirectory) =>
            throw new InvalidOperationException("完全な compile 記録に再 build は不要");

        /// <inheritdoc />
        public Result<LocatedTarget, PipelineFailure> Locate(string binlogPath, TargetProjects projects)
        {
            var file = Path.Combine(Root, "Fixture.cs");
            File.WriteAllText(file, "public static class C { public static int Add(int left, int right) => left + right; }");
            var output = Path.Combine(Root, "Fixture.dll");
            string[] arguments = ["/target:library", $"/out:{output}", $"/reference:{typeof(object).Assembly.Location}", file];
            return Result.Succeeded(new LocatedTarget(
                new CscInvocation(projects.ProjectPath, arguments, output),
                Path.Combine(Root, "Tests.dll"),
                new TargetGraph(new ProjectInputs(projects.ProjectPath, [file], []), new ProjectInputs(projects.TestProjectPath, [], []), [], [])
            ));
        }

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
