using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>分岐と繰り返しの条件式を定数または否定へ置き換える変異の生成</summary>
public sealed class ConditionMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        var (owner, condition, allowTrue) = node switch
        {
            IfStatementSyntax ifStatement => ((SyntaxNode)ifStatement, ifStatement.Condition, true),
            ConditionalExpressionSyntax ternary => ((SyntaxNode)ternary, ternary.Condition, true),
            WhileStatementSyntax whileStatement => ((SyntaxNode)whileStatement, whileStatement.Condition, false),
            DoStatementSyntax doStatement => ((SyntaxNode)doStatement, doStatement.Condition, false),
            ForStatementSyntax forStatement => ((SyntaxNode)forStatement, forStatement.Condition, false),
            _ => ((SyntaxNode?)null, null, false),
        };
        if (condition is null || !IsMutableCondition(condition, model))
        {
            yield break;
        }

        var inStatic = MutationContexts.IsStaticContext(condition);
        if (DeclaresVariables(condition))
        {
            if (owner is ConditionalExpressionSyntax)
            {
                yield break;
            }

            if (DeclaredVariableIsReadOutsideCondition(owner!, condition))
            {
                yield break;
            }

            yield return NegationCandidate(owner!, condition, inStatic);
            yield break;
        }

        if (allowTrue)
        {
            yield return LiteralCandidate(condition, SyntaxKind.TrueLiteralExpression, inStatic);
        }

        yield return LiteralCandidate(condition, SyntaxKind.FalseLiteralExpression, inStatic);
    }

    /// <summary>条件式を true / false リテラルへ置き換える候補の生成</summary>
    private static MutationCandidate.ExpressionSwap LiteralCandidate(
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

    /// <summary>変数を宣言する条件式を否定へ置き換える候補の生成</summary>
    private static MutationCandidate NegationCandidate(
        SyntaxNode owner,
        ExpressionSyntax condition,
        bool inStatic
    )
    {
        var replacement = Negate(condition);
        if (ContainsOutDeclaration(condition))
        {
            return new MutationCandidate.ExpressionSwap(
                condition,
                visited => Negate(visited),
                nameof(ConditionMutator),
                replacement.NormalizeWhitespace().ToString(),
                inStatic,
                AllowsExpressionHazard: true,
                UsesInlineBooleanToggle: true
            );
        }

        if (owner is StatementSyntax statement)
        {
            return new MutationCandidate.StatementSwap(
                statement,
                visited => NegateStatement(visited),
                nameof(ConditionMutator),
                replacement.NormalizeWhitespace().ToString(),
                inStatic
            )
            {
                ReportTarget = condition,
            };
        }

        return new MutationCandidate.ExpressionSwap(
            condition,
            visited => Negate(visited),
            nameof(ConditionMutator),
            replacement.NormalizeWhitespace().ToString(),
            inStatic,
            AllowsExpressionHazard: true
        );
    }

    /// <summary>条件を否定した statement の再構築</summary>
    private static StatementSyntax NegateStatement(StatementSyntax statement) =>
        statement switch
        {
            IfStatementSyntax ifStatement => ifStatement.WithCondition(Negate(ifStatement.Condition)),
            WhileStatementSyntax whileStatement => whileStatement.WithCondition(Negate(whileStatement.Condition)),
            DoStatementSyntax doStatement => doStatement.WithCondition(Negate(doStatement.Condition)),
            ForStatementSyntax forStatement => forStatement.WithCondition(
                forStatement.Condition is { } condition ? Negate(condition) : null
            ),
            _ => statement,
        };

    /// <summary>条件変数が条件の外で読まれるかの判定</summary>
    private static bool DeclaredVariableIsReadOutsideCondition(SyntaxNode owner, ExpressionSyntax condition)
    {
        var conditionNodes = condition.DescendantNodesAndSelf().ToHashSet();
        var patternVariableNames = condition
            .DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Where(designation => designation.Ancestors().Any(ancestor => ancestor is PatternSyntax))
            .Select(designation => designation.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        if (
            owner.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(identifier => !conditionNodes.Contains(identifier))
                .Any(identifier => patternVariableNames.Contains(identifier.Identifier.ValueText))
        )
        {
            return true;
        }

        if (owner is not StatementSyntax statement || statement.Parent is not BlockSyntax block)
        {
            return false;
        }

        var statementIndex = block.Statements.IndexOf(statement);
        return block
            .Statements
            .Skip(statementIndex + 1)
            .SelectMany(sibling => sibling.DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(identifier => patternVariableNames.Contains(identifier.Identifier.ValueText));
    }

    /// <summary>条件式を括弧付きの論理否定へ再構築</summary>
    private static PrefixUnaryExpressionSyntax Negate(ExpressionSyntax condition) =>
        SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.ParenthesizedExpression(condition)
        );

    /// <summary>置き換えてよい条件式かの判定。リテラルと定数と bool 以外の除外</summary>
    private static bool IsMutableCondition(ExpressionSyntax condition, SemanticModel model)
    {
        if (condition.Kind() is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression)
        {
            return false;
        }

        return model.GetTypeInfo(condition).ConvertedType?.SpecialType == SpecialType.System_Boolean;
    }

    /// <summary>out 引数または pattern 変数を宣言する条件かの判定</summary>
    private static bool DeclaresVariables(ExpressionSyntax condition) =>
        condition.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Any()
        || ContainsOutDeclaration(condition);

    /// <summary>out 引数の宣言を含む条件かの判定</summary>
    private static bool ContainsOutDeclaration(ExpressionSyntax condition) =>
        condition
            .DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(argument => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
}
