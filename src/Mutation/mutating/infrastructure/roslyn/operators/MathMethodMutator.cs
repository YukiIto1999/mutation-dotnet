using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>署名が対称な Math method を対へ置き換える変異の生成</summary>
public sealed class MathMethodMutator : IMutationOperator
{
    /// <summary>Math / MathF の対の入れ替え仕様</summary>
    private static readonly MethodPairSpec Spec = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Min"] = "Max",
            ["Max"] = "Min",
            ["Floor"] = "Ceiling",
            ["Ceiling"] = "Floor",
        },
        method =>
            method.ContainingType is
            {
                Name: "Math" or "MathF",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            },
        (_, _) => true,
        nameof(MathMethodMutator)
    );

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model) =>
        MethodPairSwaps.Candidates(node, model, Spec);
}
