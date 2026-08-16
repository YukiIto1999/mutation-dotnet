using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>配列と collection の初期化子を空にする変異の生成</summary>
public sealed class InitializerMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        Func<ExpressionSyntax, ExpressionSyntax>? apply = node switch
        {
            ArrayCreationExpressionSyntax { Initializer.Expressions.Count: > 0 } => visited =>
                Emptied((ArrayCreationExpressionSyntax)visited),
            ObjectCreationExpressionSyntax
            {
                Initializer: { RawKind: (int)SyntaxKind.CollectionInitializerExpression, Expressions.Count: > 0 }
            } => visited => Emptied((ObjectCreationExpressionSyntax)visited),
            CollectionExpressionSyntax { Elements.Count: > 0 } => _ => SyntaxFactory.CollectionExpression(),
            _ => null,
        };
        if (apply is null)
        {
            yield break;
        }

        var expression = (ExpressionSyntax)node;
        yield return new MutationCandidate.ExpressionSwap(
            expression,
            apply,
            nameof(InitializerMutator),
            apply(expression).NormalizeWhitespace().ToString(),
            MutationContexts.IsStaticContext(expression)
        );
    }

    /// <summary>配列生成の空初期化子への置き換え</summary>
    private static ArrayCreationExpressionSyntax Emptied(ArrayCreationExpressionSyntax creation) =>
        creation.WithInitializer(SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression));

    /// <summary>collection 初期化子の空への置き換え</summary>
    private static ObjectCreationExpressionSyntax Emptied(ObjectCreationExpressionSyntax creation)
    {
        var withoutInitializer = creation.WithInitializer(null);
        return withoutInitializer.ArgumentList is null
            ? withoutInitializer.WithArgumentList(SyntaxFactory.ArgumentList())
            : withoutInitializer;
    }
}
