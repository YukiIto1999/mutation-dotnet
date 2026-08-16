using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>真偽値と文字列のリテラルを裏返す変異の生成</summary>
public sealed class LiteralMutator : IMutationOperator
{
    /// <summary>空文字列の置き換えに使う目立つ値</summary>
    private const string FilledReplacement = "MUTATED";

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model) =>
        node switch
        {
            LiteralExpressionSyntax literal => LiteralCandidates(literal),
            InterpolatedStringExpressionSyntax interpolated => InterpolatedCandidates(interpolated, model),
            _ => [],
        };

    /// <summary>数値・真偽・文字列リテラルの候補の列挙</summary>
    private static IEnumerable<MutationCandidate> LiteralCandidates(LiteralExpressionSyntax literal)
    {
        if (IsLoopOrBranchCondition(literal))
        {
            yield break;
        }

        var replacement = literal.Kind() switch
        {
            SyntaxKind.TrueLiteralExpression => SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression),
            SyntaxKind.FalseLiteralExpression => SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression),
            SyntaxKind.StringLiteralExpression => StringReplacement(literal),
            _ => null,
        };
        if (replacement is null)
        {
            yield break;
        }

        yield return new MutationCandidate.ExpressionSwap(
            literal,
            _ => replacement,
            nameof(LiteralMutator),
            replacement.ToString(),
            MutationContexts.IsStaticContext(literal)
        );
    }

    /// <summary>補間文字列の候補の列挙</summary>
    private static IEnumerable<MutationCandidate> InterpolatedCandidates(
        InterpolatedStringExpressionSyntax interpolated,
        SemanticModel model
    )
    {
        if (model.GetTypeInfo(interpolated).ConvertedType?.SpecialType != SpecialType.System_String)
        {
            yield break;
        }

        var replacement = EmptyString();
        yield return new MutationCandidate.ExpressionSwap(
            interpolated,
            _ => replacement,
            nameof(LiteralMutator),
            replacement.ToString(),
            MutationContexts.IsStaticContext(interpolated)
        );
    }

    /// <summary>loop や分岐の条件そのものかの判定。無限 loop 化を避ける形</summary>
    private static bool IsLoopOrBranchCondition(LiteralExpressionSyntax literal) =>
        literal.Parent switch
        {
            WhileStatementSyntax loop => loop.Condition == literal,
            DoStatementSyntax loop => loop.Condition == literal,
            ForStatementSyntax loop => loop.Condition == literal,
            IfStatementSyntax branch => branch.Condition == literal,
            _ => false,
        };

    /// <summary>文字列リテラルの置き換え先の生成</summary>
    private static LiteralExpressionSyntax StringReplacement(LiteralExpressionSyntax literal) =>
        literal.Token.ValueText.Length == 0
            ? SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(FilledReplacement)
            )
            : EmptyString();

    /// <summary>空文字列リテラルの生成</summary>
    private static LiteralExpressionSyntax EmptyString() =>
        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(""));
}
