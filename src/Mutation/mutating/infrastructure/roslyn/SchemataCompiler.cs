using Mutation.Mutating.Domain;
using Mutation.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>全変異を織り込んだ assembly の一括コンパイル</summary>
public static class SchemataCompiler
{
    /// <summary>rollback を試す回数の上限</summary>
    private const int MaxRollbackRounds = 5;

    /// <summary>候補収集から emit までの遂行。失敗した変異は rollback で確定させる形</summary>
    /// <param name="target">変異対象のコンパイル入力一式</param>
    /// <param name="outputDirectory">変異 assembly を書き出す directory</param>
    /// <param name="policy">どこを変異させるかの選別方針</param>
    /// <returns>成功なら成果一式、失敗なら診断付きの失敗</returns>
    public static Result<MutatedArtifact, PipelineFailure> Compile(
        LoadedTarget target,
        string outputDirectory,
        MutationPolicy policy
    )
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var collected = MutationNumbering.Collect(target, policy);
        var mutateMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();
        var switchTree = CSharpSyntaxTree.ParseText(
            MutantSwitchSource.Render(collected.Mutants.Count),
            target.ParseOptions,
            path: "MutantSwitch.g.cs"
        );
        var disabled = new HashSet<int>();
        var disabledStatics = new HashSet<string>();
        Dictionary<SyntaxTree, SyntaxTree> mutatedTrees = [];
        for (var round = 0; round < MaxRollbackRounds; round++)
        {
            mutatedTrees = RewriteAll(target, collected, disabled, disabledStatics);
            var compilation = BuildCompilation(target, mutatedTrees.Values, switchTree);
            Result<string, IReadOnlyList<Diagnostic>> emitted;
            try
            {
                emitted = SchemataEmission.Emit(compilation, target, outputDirectory);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Failed($"emit が例外で失敗した: {exception.Message}", mutatedTrees.Values, outputDirectory);
            }

            if (emitted is Result<string, IReadOnlyList<Diagnostic>>.Succeeded(var outputPath))
            {
                return new Result<MutatedArtifact, PipelineFailure>.Succeeded(
                    new MutatedArtifact(
                        collected.Mutants,
                        disabled.Select(id => new MutantId(id)).ToHashSet(),
                        outputPath,
                        mutateMs,
                        stopwatch.Elapsed.TotalMilliseconds
                    )
                );
            }

            var errors = ((Result<string, IReadOnlyList<Diagnostic>>.Failed)emitted).Failure;
            var culprits = RollbackAnalysis.FindCulprits(errors);
            if (culprits.IsEmpty)
            {
                return Failed(SchemataEmission.Render(errors), mutatedTrees.Values, outputDirectory);
            }

            disabled.UnionWith(culprits.MutantIds);
            disabledStatics.UnionWith(culprits.StaticKeys);
        }

        return Failed($"rollback を {MaxRollbackRounds} 回試しても emit が成功しない", mutatedTrees.Values, outputDirectory);
    }

    /// <summary>書換後ソースを診断用に残した上でのコンパイル失敗</summary>
    private static Result<MutatedArtifact, PipelineFailure>.Failed Failed(
        string message,
        IEnumerable<SyntaxTree> mutatedTrees,
        string outputDirectory
    )
    {
        var dumped = SchemataEmission.DumpSources(mutatedTrees, Path.Combine(outputDirectory, "failed-sources"));
        return new Result<MutatedArtifact, PipelineFailure>.Failed(
            new PipelineFailure.CompileFailed($"{message}\n書換後のソース: {dumped}")
        );
    }

    /// <summary>全構文木への schemata と static 追跡の織り込み</summary>
    private static Dictionary<SyntaxTree, SyntaxTree> RewriteAll(
        LoadedTarget target,
        CollectedMutations collected,
        IReadOnlySet<int> disabled,
        IReadOnlySet<string> disabledStatics
    ) =>
        target
            .SourceTrees.AsParallel()
            .Select(tree =>
            {
                var schemata = SchemataRewriter.Rewrite(tree.GetRoot(), collected.NumberedByTree[tree], disabled);
                var tracked = StaticContextRewriter.Rewrite(schemata, collected.StaticTypesByTree[tree], disabledStatics);
                return (Original: tree, Mutated: tree.WithRootAndOptions(tracked, target.ParseOptions));
            })
            .ToDictionary(pair => pair.Original, pair => pair.Mutated);

    /// <summary>書換後ソースと生成コードと切替クラスからのコンパイルの組み立て</summary>
    private static CSharpCompilation BuildCompilation(
        LoadedTarget target,
        IEnumerable<SyntaxTree> mutatedTrees,
        SyntaxTree switchTree
    )
    {
        var sourceTrees = target.SourceTrees.ToHashSet();
        var generatedTrees = target.OriginalCompilation.SyntaxTrees.Where(t => !sourceTrees.Contains(t));
        return CSharpCompilation.Create(
            target.Arguments.CompilationName,
            mutatedTrees.Concat(generatedTrees).Append(switchTree),
            target.OriginalCompilation.References,
            target.OriginalCompilation.Options
        );
    }
}
