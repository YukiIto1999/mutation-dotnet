using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>選別方針の組み立てと変異 assembly の生成の use-case</summary>
/// <param name="compilation">schemata コンパイルの port</param>
/// <param name="changeSets">差分解決の port</param>
public sealed class GenerateMutants(IMutationCompilation compilation, IChangeSets changeSets)
{
    /// <summary>差分運用の基点から変わった行の解決。前回結果との照合と生成が同じ差分を使うよう、生成より先に解く</summary>
    /// <param name="selection">変異対象の選別の入力</param>
    /// <param name="projectPath">--since の基点解決に使う対象 project の path</param>
    /// <returns>成功なら変わった行で、差分運用でなければ不在。基点か差分を解けなければ失敗</returns>
    public Result<ChangedLines?, PipelineFailure> ResolveChanges(MutationSelection selection, string projectPath)
    {
        ChangedLines? changes = null;
        if (selection.SinceRef is { Length: > 0 } sinceRef)
        {
            var resolved = changeSets.Resolve(Path.GetDirectoryName(projectPath) ?? ".", sinceRef);
            if (resolved.IsFailed(out var sinceFailure, out var resolvedChanges))
            {
                return sinceFailure;
            }

            changes = resolvedChanges;
        }

        if (selection.ChangedLinesOnly && changes is null)
        {
            return Result.Failed<PipelineFailure>(
                new PipelineFailure.SinceUnavailable("--changed-lines には --since が必要")
            );
        }

        return Result.Succeeded<ChangedLines?>(changes);
    }

    /// <summary>変わったファイルが対象の変異候補へ届く関係の判定と、候補の範囲の選択</summary>
    /// <remarks>
    /// 差分運用でなければ選別しない。差分運用では、対象のソース、テスト、参照する project、build の入力、消えたファイルの変更を、build graph が示す入力から判定する
    /// 変更行の部分集合を求めない限り、届く先を file より細かく選べない関係は、対象の全範囲を候補にする
    /// </remarks>
    /// <param name="changes"><see cref="ResolveChanges"/> が返した変わった行。差分運用でなければ不在</param>
    /// <param name="graph">対象とテストと、それらが参照する project の入力</param>
    /// <param name="selection">変異対象の選別の入力</param>
    /// <returns>候補の範囲の選択。差分運用でなければ不在</returns>
    public static ImpactAssessment? Assess(ChangedLines? changes, TargetGraph graph, MutationSelection selection) =>
        changes is null ? null : ChangeImpact.Select(changes, graph, selection.ChangedLinesOnly);

    /// <summary>選別入力と解決済みの差分と候補の範囲から方針を組み、変異 assembly 一式を生成する遂行</summary>
    /// <param name="sut">対象 project の csc 呼び出し</param>
    /// <param name="selection">変異対象の選別の入力</param>
    /// <param name="changes"><see cref="ResolveChanges"/> が返した変わった行。差分運用でなければ不在</param>
    /// <param name="impact"><see cref="Assess"/> が返した候補の範囲。差分運用でなければ不在</param>
    /// <param name="mutatedDirectory">変異 assembly を書き出す directory</param>
    /// <returns>成功なら成果一式、失敗なら診断付きの失敗</returns>
    public Result<MutatedArtifact, PipelineFailure> Execute(
        CscInvocation sut,
        MutationSelection selection,
        ChangedLines? changes,
        ImpactAssessment? impact,
        string mutatedDirectory
    )
    {
        var policy = new MutationPolicy(
            MutationScope.FromPatterns(selection.MutatePatterns),
            impact?.Files,
            selection.IgnoredOperators.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selection.IgnoredMethods.ToHashSet(StringComparer.Ordinal),
            selection.ChangedLinesOnly ? changes : null
        );
        return compilation.Compile(sut, mutatedDirectory, policy);
    }
}
