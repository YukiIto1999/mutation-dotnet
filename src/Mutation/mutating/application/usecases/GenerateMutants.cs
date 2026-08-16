using Mutation.Mutating.Domain;
using Mutation.Shared;
using TypeModeling.Domain;

namespace Mutation.Mutating.Application;

/// <summary>選別方針の組み立てと変異 assembly の生成の use-case</summary>
/// <param name="compilation">schemata コンパイルの port</param>
/// <param name="changeSets">差分解決の port</param>
public sealed class GenerateMutants(IMutationCompilation compilation, IChangeSets changeSets)
{
    /// <summary>選別入力から方針を組み、変異 assembly 一式を生成する遂行</summary>
    /// <param name="sut">対象 project の csc 呼び出し</param>
    /// <param name="selection">変異対象の選別の入力</param>
    /// <param name="projectPath">--since の基点解決に使う対象 project の path</param>
    /// <param name="mutatedDirectory">変異 assembly を書き出す directory</param>
    /// <returns>成功なら成果一式、失敗なら診断付きの失敗</returns>
    public Result<MutatedArtifact, PipelineFailure> Execute(
        CscInvocation sut,
        MutationSelection selection,
        string projectPath,
        string mutatedDirectory
    )
    {
        IReadOnlySet<string>? changed = null;
        if (selection.SinceRef is { Length: > 0 } sinceRef)
        {
            var resolved = changeSets.Resolve(Path.GetDirectoryName(projectPath) ?? ".", sinceRef);
            if (resolved is Result<IReadOnlySet<string>, PipelineFailure>.Failed(var sinceFailure))
            {
                return new Result<MutatedArtifact, PipelineFailure>.Failed(sinceFailure);
            }

            changed = ((Result<IReadOnlySet<string>, PipelineFailure>.Succeeded)resolved).Value;
        }

        var policy = new MutationPolicy(
            MutationScope.FromPatterns(selection.MutatePatterns),
            changed,
            selection.IgnoredOperators.ToHashSet(StringComparer.OrdinalIgnoreCase),
            selection.IgnoredMethods.ToHashSet(StringComparer.Ordinal)
        );
        return compilation.Compile(sut, mutatedDirectory, policy);
    }
}
