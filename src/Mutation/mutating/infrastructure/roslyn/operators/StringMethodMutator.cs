using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>署名が対称な string method を対へ置き換える変異の生成</summary>
public sealed class StringMethodMutator : IMutationOperator
{
    /// <summary>string method の対の入れ替え仕様</summary>
    private static readonly MethodPairSpec Spec = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["StartsWith"] = "EndsWith",
            ["EndsWith"] = "StartsWith",
            ["ToUpper"] = "ToLower",
            ["ToLower"] = "ToUpper",
            ["ToUpperInvariant"] = "ToLowerInvariant",
            ["ToLowerInvariant"] = "ToUpperInvariant",
            ["TrimStart"] = "TrimEnd",
            ["TrimEnd"] = "TrimStart",
            ["PadLeft"] = "PadRight",
            ["PadRight"] = "PadLeft",
            ["IndexOf"] = "LastIndexOf",
            ["LastIndexOf"] = "IndexOf",
        },
        method => method.ContainingType.SpecialType == SpecialType.System_String,
        (_, _) => true,
        nameof(StringMethodMutator)
    );

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model) =>
        MethodPairSwaps.Candidates(node, model, Spec);
}
