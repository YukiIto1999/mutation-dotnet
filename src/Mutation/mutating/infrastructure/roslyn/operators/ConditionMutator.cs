using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>分岐と繰り返しの条件式を定数へ置き換える変異の生成</summary>
public sealed class ConditionMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        var (condition, allowTrue) = node switch
        {
            IfStatementSyntax ifStatement => (ifStatement.Condition, true),
            ConditionalExpressionSyntax ternary => (ternary.Condition, true),
            WhileStatementSyntax whileStatement => (whileStatement.Condition, false),
            DoStatementSyntax doStatement => (doStatement.Condition, false),
            ForStatementSyntax forStatement => (forStatement.Condition, false),
            _ => (null, false),
        };
        if (condition is null || !IsMutableCondition(condition, model))
        {
            yield break;
        }

        var inStatic = MutationContexts.IsStaticContext(condition);
        if (allowTrue)
        {
            yield return Candidate(condition, SyntaxKind.TrueLiteralExpression, inStatic);
        }

        yield return Candidate(condition, SyntaxKind.FalseLiteralExpression, inStatic);
    }

    /// <summary>条件式を true / false リテラルへ置き換える候補の生成</summary>
    private static MutationCandidate.ExpressionSwap Candidate(
        ExpressionSyntax condition,
        SyntaxKind literalKind,
        bool inStatic
    )
    {
        var replacement = SyntaxFactory.LiteralExpression(literalKind);
        return new MutationCandidate.ExpressionSwap(
            condition,
            _ => replacement,
            nameof(ConditionMutator),
            replacement.ToString(),
            inStatic
        );
    }

    /// <summary>置き換えてよい条件式かの判定。リテラルと定数と bool 以外は外す</summary>
    private static bool IsMutableCondition(ExpressionSyntax condition, SemanticModel model)
    {
        if (condition.Kind() is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression)
        {
            return false;
        }

        if (AssignsOutArguments(condition))
        {
            return false;
        }

        return model.GetTypeInfo(condition).ConvertedType?.SpecialType == SpecialType.System_Boolean;
    }

    /// <summary>out 引数への代入を含む条件かの判定</summary>
    private static bool AssignsOutArguments(ExpressionSyntax condition) =>
        condition
            .DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
}
