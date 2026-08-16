using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>呼び出し文・代入文・増減文を取り除く変異の生成</summary>
public sealed class StatementRemover : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        if (node is not ExpressionStatementSyntax statement || !IsRemovable(statement.Expression, model))
        {
            yield break;
        }

        if (HasBindingImpact(statement))
        {
            yield break;
        }

        yield return new MutationCandidate.StatementSwap(
            statement,
            _ => SyntaxFactory.Block(),
            nameof(StatementRemover),
            "{}",
            MutationContexts.IsStaticContext(statement)
        );
    }

    /// <summary>取り除いても compile が成立する文の式かの判定</summary>
    private static bool IsRemovable(ExpressionSyntax expression, SemanticModel model) =>
        expression switch
        {
            InvocationExpressionSyntax => true,
            AwaitExpressionSyntax awaited => awaited.Expression is InvocationExpressionSyntax,
            AssignmentExpressionSyntax assignment => TargetStaysAssigned(assignment.Left, model),
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression } prefix
                => TargetStaysAssigned(prefix.Operand, model),
            PostfixUnaryExpressionSyntax postfix => TargetStaysAssigned(postfix.Operand, model),
            _ => false,
        };

    /// <summary>代入先が除去後も確実代入を保つかの判定</summary>
    private static bool TargetStaysAssigned(ExpressionSyntax target, SemanticModel model) =>
        model.GetSymbolInfo(target).Symbol switch
        {
            IFieldSymbol => true,
            IPropertySymbol => true,
            IParameterSymbol parameter => parameter.RefKind != RefKind.Out,
            ILocalSymbol { IsRef: false } local => HasInitializer(local),
            _ => false,
        };

    /// <summary>宣言時に初期化子を持つ local かの判定</summary>
    private static bool HasInitializer(ILocalSymbol local) =>
        local
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .Any(declarator => declarator.Initializer is not null);

    /// <summary>out 変数宣言など、除去が束縛へ波及する文かの判定</summary>
    private static bool HasBindingImpact(ExpressionStatementSyntax statement) =>
        statement
            .DescendantNodes()
            .Any(n =>
                n is DeclarationExpressionSyntax
                || (n is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            );
}
