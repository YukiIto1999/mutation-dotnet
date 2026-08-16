using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>null 合体演算子の片側を落とす変異の生成</summary>
public sealed class NullCoalesceMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        if (node is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce)
        {
            yield break;
        }

        var inStatic = MutationContexts.IsStaticContext(coalesce);
        if (coalesce.Right is not ThrowExpressionSyntax)
        {
            yield return new MutationCandidate.ExpressionSwap(
                coalesce,
                visited => ((BinaryExpressionSyntax)visited).Right,
                nameof(NullCoalesceMutator),
                coalesce.Right.NormalizeWhitespace().ToString(),
                inStatic
            );
        }

        if (LeftDroppable(coalesce, model))
        {
            yield return new MutationCandidate.ExpressionSwap(
                coalesce,
                visited => ((BinaryExpressionSyntax)visited).Left,
                nameof(NullCoalesceMutator),
                coalesce.Left.NormalizeWhitespace().ToString(),
                inStatic
            );
        }
    }

    /// <summary>左辺を落としても型が成立するかの判定</summary>
    private static bool LeftDroppable(BinaryExpressionSyntax coalesce, SemanticModel model) =>
        model.GetTypeInfo(coalesce.Left).Type
            is ITypeSymbol left
            && left.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
}
