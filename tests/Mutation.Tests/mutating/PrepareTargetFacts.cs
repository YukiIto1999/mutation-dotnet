using Mutation.Application;
using Mutation.Domain;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>増分 build で欠けた対象だけを補う準備の検査</summary>
public sealed class PrepareTargetFacts
{
    /// <summary>最初の binlog にコンパイル記録がある対象</summary>
    private static readonly TargetProjects Ready = new(Path.GetFullPath("ready.csproj"), Path.GetFullPath("ready.Tests.csproj"), "Debug");

    /// <summary>最初の binlog にコンパイル記録がない対象</summary>
    private static readonly TargetProjects Missing = new(Path.GetFullPath("missing.csproj"), Path.GetFullPath("missing.Tests.csproj"), "Debug");

    /// <summary>自身の compile は記録されているが、参照する project の compile 記録が欠けた対象</summary>
    private static readonly TargetProjects Stale = new(Path.GetFullPath("stale.csproj"), Path.GetFullPath("stale.Tests.csproj"), "Debug");

    /// <summary>全対象を特定できた場合の不要な再 build の防止</summary>
    /// <returns>既存の成果物だけで準備が成功することの検査</returns>
    [Test]
    public async Task Complete_build_does_not_require_a_rebuild()
    {
        var preparation = new PrepareTarget(new Builds(), new Locations(false));

        var outcome = await preparation.ExecuteAsync([Ready], "work");

        await Assert.That(outcome.IsFailed(out _, out var prepared)).IsFalse();
        await Assert.That(prepared![0].IsFailed(out _, out var target)).IsFalse();
        await Assert.That(target!.TestAssembly).IsEqualTo("ready.Tests.dll");
    }

    /// <summary>不足した対象の補完後も元の入力順で利用可能な成果物を返すことの検査</summary>
    /// <returns>対象ごとのコンパイル記録が混ざらないことの検査</returns>
    [Test]
    public async Task Rebuild_fills_only_missing_targets_and_preserves_input_order()
    {
        var preparation = new PrepareTarget(new Builds(), new Locations(false));

        var outcome = await preparation.ExecuteAsync([Missing, Ready], "work");

        await Assert.That(outcome.IsFailed(out _, out var prepared)).IsFalse();
        await Assert.That(prepared![0].IsFailed(out _, out var recovered)).IsFalse();
        await Assert.That(prepared[1].IsFailed(out _, out var ready)).IsFalse();
        await Assert.That(recovered!.Projects).IsEqualTo(Missing);
        await Assert.That(recovered.Sut.OutputPath).IsEqualTo(Path.GetFullPath("missing.dll"));
        await Assert.That(ready!.Projects).IsEqualTo(Ready);
        await Assert.That(ready.Sut.OutputPath).IsEqualTo(Path.GetFullPath("ready.dll"));
    }

    /// <summary>再 build 後も特定できない対象の失敗が利用可能な対象へ波及しないことの検査</summary>
    /// <returns>対象ごとの成功と再特定の失敗を区別した結果の検査</returns>
    [Test]
    public async Task Unresolved_target_does_not_discard_other_prepared_targets()
    {
        var preparation = new PrepareTarget(new Builds(), new Locations(true));

        var outcome = await preparation.ExecuteAsync([Missing, Ready], "work");

        await Assert.That(outcome.IsFailed(out _, out var prepared)).IsFalse();
        await Assert.That(prepared![0].IsFailed(out var unavailable, out _)).IsTrue();
        await Assert.That(unavailable.Failure is PipelineFailure.NoTestsFound).IsTrue();
        await Assert.That(prepared[1].IsFailed(out _, out var ready)).IsFalse();
        await Assert.That(ready!.TestAssembly).IsEqualTo("ready.Tests.dll");
    }

    /// <summary>対象自身の compile が記録されていても、参照する project の compile 記録が欠けた対象だけが、再 build で補われること</summary>
    /// <returns>再 build の対象と、補った後の入力の検査</returns>
    [Test]
    public async Task A_target_with_an_unbuilt_reference_is_rebuilt_while_a_complete_target_is_not()
    {
        var builds = new Builds();
        var preparation = new PrepareTarget(builds, new Locations(false));

        var outcome = await preparation.ExecuteAsync([Stale, Ready], "work");

        await Assert.That(outcome.IsFailed(out _, out var prepared)).IsFalse();
        await Assert.That(builds.Rebuilt.Select(project => project.ProjectPath).ToArray()).IsEquivalentTo([Stale.ProjectPath]);
        await Assert.That(prepared![0].IsFailed(out _, out var stale)).IsFalse();
        await Assert.That(stale!.Graph.UnbuiltProjects).IsEmpty();
    }

    /// <summary>不足した一件と参照の記録が欠けた一件以外の再 build を失敗させ、再 build の対象を記録する build の制御</summary>
    private sealed class Builds : IBinlogBuilds
    {
        /// <summary>再 build を求められた対象</summary>
        public List<TargetProjects> Rebuilt { get; } = [];

        /// <inheritdoc />
        public Task<Result<string, PipelineFailure>> BuildAsync(
            IReadOnlyList<TargetProjects> projects,
            string workDirectory
        ) => Task.FromResult<Result<string, PipelineFailure>>(Result.Succeeded("initial.binlog"));

        /// <inheritdoc />
        public Task<Result<string, PipelineFailure>> RebuildAsync(
            IReadOnlyList<TargetProjects> projects,
            string workDirectory
        )
        {
            Rebuilt.AddRange(projects);
            Result<string, PipelineFailure> result = projects.Count == 1 && (projects[0] == Missing || projects[0] == Stale)
                ? Result.Succeeded("rebuilt.binlog")
                : Result.Failed<PipelineFailure>(new PipelineFailure.BuildFailed("利用可能な対象の再 build"));
            return Task.FromResult(result);
        }
    }

    /// <summary>初回と再 build 後のコンパイル記録を区別する特定の制御</summary>
    /// <param name="missingRemainsUnavailable">再 build 後にもテスト assembly が見つからない状態</param>
    private sealed class Locations(bool missingRemainsUnavailable) : ILocateTargets
    {
        /// <inheritdoc />
        public Result<LocatedTarget, PipelineFailure> Locate(
            string binlogPath,
            TargetProjects projects
        )
        {
            if (projects == Ready)
            {
                return Result.Succeeded(Located(Ready, "ready.dll", "ready.Tests.dll", []));
            }

            if (projects == Stale)
            {
                return Result.Succeeded(
                    Located(Stale, "stale.dll", "stale.Tests.dll", binlogPath == "initial.binlog" ? [Path.GetFullPath("Library.csproj")] : [])
                );
            }

            if (binlogPath == "initial.binlog")
            {
                return Result.Failed<PipelineFailure>(new PipelineFailure.CompilerArgumentsNotFound(Missing.ProjectPath));
            }

            return missingRemainsUnavailable
                ? Result.Failed<PipelineFailure>(new PipelineFailure.NoTestsFound("missing.Tests.dll"))
                : Result.Succeeded(Located(Missing, "missing.dll", "missing.Tests.dll", []));
        }

        /// <summary>空の入力を持つ、特定済みの対象</summary>
        private static LocatedTarget Located(TargetProjects projects, string output, string testAssembly, string[] unbuilt) =>
            new(
                new CscInvocation(projects.ProjectPath, [], Path.GetFullPath(output)),
                testAssembly,
                new TargetGraph(
                    new ProjectInputs(projects.ProjectPath, [], []),
                    new ProjectInputs(projects.TestProjectPath, [], []),
                    [],
                    unbuilt
                )
            );
    }
}
