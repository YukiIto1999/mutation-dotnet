using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>署名が対称な LINQ メソッドを対へ置き換える変異の生成</summary>
public sealed class LinqMethodMutator : IMutationOperator
{
    /// <summary>Enumerable / Queryable の対の入れ替え仕様</summary>
    private static readonly MethodPairSpec Spec = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["First"] = "Last",
            ["Last"] = "First",
            ["FirstOrDefault"] = "LastOrDefault",
            ["LastOrDefault"] = "FirstOrDefault",
            ["Min"] = "Max",
            ["Max"] = "Min",
            ["OrderBy"] = "OrderByDescending",
            ["OrderByDescending"] = "OrderBy",
            ["ThenBy"] = "ThenByDescending",
            ["ThenByDescending"] = "ThenBy",
            ["Any"] = "All",
            ["All"] = "Any",
            ["Skip"] = "Take",
            ["Take"] = "Skip",
            ["SkipWhile"] = "TakeWhile",
            ["TakeWhile"] = "SkipWhile",
            ["SkipLast"] = "TakeLast",
            ["TakeLast"] = "SkipLast",
            ["Union"] = "Intersect",
            ["Intersect"] = "Union",
            ["Concat"] = "Except",
            ["Except"] = "Concat",
        },
        method =>
            method.ContainingType is
            {
                Name: "Enumerable" or "Queryable",
                ContainingNamespace:
                {
                    Name: "Linq",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                }
            },
        (name, invocation) => name is not ("Any" or "All") || invocation.ArgumentList.Arguments.Count == 1,
        nameof(LinqMethodMutator)
    );

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model) =>
        MethodPairSwaps.Candidates(node, model, Spec);
}
