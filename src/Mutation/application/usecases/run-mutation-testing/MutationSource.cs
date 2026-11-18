using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>一回の変異検査で使う対象の準備、同一性の算出、変異の生成</summary>
/// <param name="prepareTarget">対象の準備</param>
/// <param name="generateMutants">変異の生成</param>
/// <param name="fingerprints">再利用の同一性の計算</param>
public sealed class MutationSource(
    PrepareTarget prepareTarget,
    GenerateMutants generateMutants,
    IFingerprints fingerprints
) : IMutantSource
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>> PrepareAsync(
        IReadOnlyList<TargetProjects> targets,
        MutationSelection selection,
        string workDirectory,
        ExecutionSettings execution,
        WorkerHost host
    )
    {
        var located = await prepareTarget.ExecuteAsync(targets, workDirectory).ConfigureAwait(false);
        if (located.IsFailed(out var failure, out var projectResults))
        {
            return failure;
        }

        var run = fingerprints.Capture(
            new ReuseSettings(selection, execution.ValidateSurvivors, execution.ExcludeStatic),
            workDirectory,
            host
        );
        var results = new List<Result<PreparedTarget, PipelineFailure>>(targets.Count);
        foreach (var (target, project) in targets.Zip(projectResults))
        {
            results.Add(Describe(target, project, selection, run));
        }

        return Result.Succeeded<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>>(results);
    }

    /// <summary>対象一件の差分と候補の範囲の解決、同一性の算出、生成入力を保持した準備結果の作成</summary>
    private Result<PreparedTarget, PipelineFailure> Describe(
        TargetProjects target,
        Result<PreparedProject, PipelineFailure> project,
        MutationSelection selection,
        RunClosure? run
    )
    {
        if (project.IsFailed(out var projectFailure, out var value))
        {
            return projectFailure;
        }

        var resolved = generateMutants.ResolveChanges(selection, target.ProjectPath);
        if (resolved.IsFailed(out var changeFailure, out var changes))
        {
            return changeFailure;
        }

        var impact = GenerateMutants.Assess(changes, value.Graph, selection);
        var identity = run is null
            ? null
            : fingerprints.Compute(run, value.Sut, value.TestAssembly, changes, impact?.Key ?? "");
        return Result.Succeeded<PreparedTarget>(
            new PreparedMutation(
                target,
                value.TestAssembly,
                Path.GetFileNameWithoutExtension(value.Sut.OutputPath),
                identity,
                impact?.Selection,
                generateMutants,
                value.Sut,
                selection,
                changes,
                impact
            )
        );
    }

    /// <summary>準備した一件だけの入力を保持する変異生成</summary>
    /// <param name="Request">元の対象指定</param>
    /// <param name="TestAssemblyPath">テスト assembly</param>
    /// <param name="TargetAssemblyName">差し替え対象の名前</param>
    /// <param name="Identity">再利用の同一性</param>
    /// <param name="Selection">候補の範囲の記録</param>
    /// <param name="Generator">変異生成の処理</param>
    /// <param name="Sut">対象の csc 呼び出し</param>
    /// <param name="MutationSelection">変異対象の選別</param>
    /// <param name="Changes">解決済みの差分</param>
    /// <param name="Impact">変更の影響から選んだ候補の範囲</param>
    private sealed record PreparedMutation(
        TargetProjects Request,
        string TestAssemblyPath,
        string TargetAssemblyName,
        ReuseIdentity? Identity,
        CandidateSelection? Selection,
        GenerateMutants Generator,
        CscInvocation Sut,
        MutationSelection MutationSelection,
        ChangedLines? Changes,
        ImpactAssessment? Impact
    ) : PreparedTarget(Request, TestAssemblyPath, TargetAssemblyName, Identity, Selection)
    {
        /// <inheritdoc />
        public override Result<MutatedArtifact, PipelineFailure> Generate(string mutatedDirectory) =>
            Generator.Execute(Sut, MutationSelection, Changes, Impact, mutatedDirectory);
    }
}
