using Mutation.Mutating.Domain;
using Mutation.Shared;
using Microsoft.CodeAnalysis;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>変異候補の収集と、schemata の連番の割り当て</summary>
public static class MutationNumbering
{
    /// <summary>選別方針に沿った全構文木からの候補の収集と採番</summary>
    /// <param name="target">変異対象のコンパイル入力一式</param>
    /// <param name="policy">どこを変異させるかの選別方針</param>
    /// <returns>採番済みの候補と変異の素性の一式</returns>
    public static CollectedMutations Collect(LoadedTarget target, MutationPolicy policy)
    {
        var baseDirectory = target.Arguments.BaseDirectory ?? Path.GetDirectoryName(target.OriginalOutputPath) ?? ".";
        var collected = target
            .SourceTrees.AsParallel()
            .AsOrdered()
            .Select(tree =>
            {
                var root = tree.GetRoot();
                var model = target.OriginalCompilation.GetSemanticModel(tree);
                var relative = Path.GetRelativePath(baseDirectory, tree.FilePath);
                return (
                    Tree: tree,
                    Candidates: policy.IncludesFile(tree.FilePath, relative)
                        ? CandidateCollector.Collect(root, model, CandidateCollector.DefaultOperators, policy)
                        : [],
                    StaticTypes: StaticInitializerIndex.Build(root, model)
                );
            })
            .ToList();
        var mutants = new List<Mutant>();
        var numberedByTree = new Dictionary<SyntaxTree, IReadOnlyList<(int, MutationCandidate)>>();
        var nextId = 0;
        foreach (var (tree, candidates, _) in collected)
        {
            var numbered = new List<(int, MutationCandidate)>();
            foreach (var candidate in candidates)
            {
                var id = nextId++;
                numbered.Add((id, candidate));
                mutants.Add(ToMutant(id, candidate, tree));
            }

            numberedByTree[tree] = numbered;
        }

        return new CollectedMutations(numberedByTree, mutants, collected.ToDictionary(c => c.Tree, c => c.StaticTypes));
    }

    /// <summary>候補から報告用の変異の素性への写像。断片は 200 字で切る</summary>
    private static Mutant ToMutant(int id, MutationCandidate candidate, SyntaxTree tree)
    {
        var span = candidate.ReportTarget.GetLocation().GetLineSpan();
        var original = candidate.ReportTarget.ToString();
        return new Mutant(
            new MutantId(id),
            candidate.OperatorName,
            tree.FilePath,
            new SourceSpan(
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1,
                span.EndLinePosition.Character + 1
            ),
            original.Length <= 200 ? original : original[..200],
            candidate.ReplacementText.Length <= 200 ? candidate.ReplacementText : candidate.ReplacementText[..200],
            candidate.InStaticContext
        );
    }
}

/// <summary>候補収集と採番の成果一式</summary>
/// <param name="NumberedByTree">構文木ごとの採番済み候補</param>
/// <param name="Mutants">採番順の全変異の素性</param>
/// <param name="StaticTypesByTree">構文木ごとの static 初期化文脈の対応</param>
public sealed record CollectedMutations(
    IReadOnlyDictionary<SyntaxTree, IReadOnlyList<(int Id, MutationCandidate Candidate)>> NumberedByTree,
    IReadOnlyList<Mutant> Mutants,
    IReadOnlyDictionary<SyntaxTree, IReadOnlyDictionary<string, string>> StaticTypesByTree
);
