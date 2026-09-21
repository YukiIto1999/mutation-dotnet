using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>三項演算子を置けない「文としての式」の文脈にある候補の、文の形への正規化</summary>
public static class StatementContexts
{
    /// <summary>式の置換候補の、置かれた文脈で compile できる形への正規化</summary>
    /// <param name="candidate">演算子が生成した候補</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <returns>そのまま置ける候補は元のまま。文脈上置けなければ文か本体の候補、表せなければ不在</returns>
    public static MutationCandidate? Normalize(MutationCandidate candidate, SemanticModel model)
    {
        if (candidate is not MutationCandidate.ExpressionSwap swap)
        {
            return candidate;
        }

        return swap.Expression.Parent switch
        {
            ExpressionStatementSyntax statement => new MutationCandidate.StatementSwap(
                statement,
                visited => ((ExpressionStatementSyntax)visited).WithExpression(
                    swap.Apply(((ExpressionStatementSyntax)visited).Expression)
                ),
                swap.OperatorName,
                swap.ReplacementText,
                swap.InStaticContext
            )
            {
                ReportTarget = swap.ReportTarget,
            },
            ForStatementSyntax loop when loop.Incrementors.Contains(swap.Expression) => ForSwap(
                swap,
                loop,
                loop.Incrementors.IndexOf(swap.Expression),
                incrementor: true
            ),
            ForStatementSyntax loop when loop.Initializers.Contains(swap.Expression) => ForSwap(
                swap,
                loop,
                loop.Initializers.IndexOf(swap.Expression),
                incrementor: false
            ),
            ArrowExpressionClauseSyntax arrow when arrow.Parent is { } owner && IsVoidLike(owner, model) =>
                new MutationCandidate.VoidArrowSwap(
                    owner,
                    swap.Apply,
                    swap.OperatorName,
                    swap.ReplacementText,
                    swap.InStaticContext
                )
                {
                    ReportTarget = swap.ReportTarget,
                },
            LambdaExpressionSyntax lambda when IsVoidLambda(lambda, model) => new MutationCandidate.VoidArrowSwap(
                lambda,
                swap.Apply,
                swap.OperatorName,
                swap.ReplacementText,
                swap.InStaticContext
            )
            {
                ReportTarget = swap.ReportTarget,
            },
            _ => swap,
        };
    }

    /// <summary>for の incrementor に置ける文候補への写像</summary>
    private static MutationCandidate.StatementSwap ForSwap(
        MutationCandidate.ExpressionSwap swap,
        ForStatementSyntax loop,
        int index,
        bool incrementor
    ) =>
        new MutationCandidate.StatementSwap(
            loop,
            visited =>
            {
                var current = (ForStatementSyntax)visited;
                if (incrementor)
                {
                    var target = current.Incrementors[index];
                    return current.WithIncrementors(current.Incrementors.Replace(target, swap.Apply(target)));
                }

                var initializer = current.Initializers[index];
                return current.WithInitializers(current.Initializers.Replace(initializer, swap.Apply(initializer)));
            },
            swap.OperatorName,
            swap.ReplacementText,
            swap.InStaticContext
        )
        {
            ReportTarget = swap.ReportTarget,
        };

    /// <summary>本体が値を返さない宣言かの判定</summary>
    private static bool IsVoidLike(SyntaxNode owner, SemanticModel model) =>
        owner switch
        {
            ConstructorDeclarationSyntax or DestructorDeclarationSyntax => true,
            AccessorDeclarationSyntax accessor => !accessor.IsKind(SyntaxKind.GetAccessorDeclaration),
            MethodDeclarationSyntax or LocalFunctionStatementSyntax => model.GetDeclaredSymbol(owner)
                is IMethodSymbol symbol
                && ReturnsNothing(symbol),
            _ => false,
        };

    /// <summary>値を返さない lambda かの判定</summary>
    private static bool IsVoidLambda(LambdaExpressionSyntax lambda, SemanticModel model) =>
        model.GetSymbolInfo(lambda).Symbol is IMethodSymbol symbol && ReturnsNothing(symbol);

    /// <summary>戻り値が void / Task / ValueTask かの判定</summary>
    private static bool ReturnsNothing(IMethodSymbol symbol) =>
        symbol.ReturnsVoid || (symbol.IsAsync && symbol.ReturnType is INamedTypeSymbol { Arity: 0 });
}
