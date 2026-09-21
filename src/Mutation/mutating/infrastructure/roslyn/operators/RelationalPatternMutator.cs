using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>関係 pattern の演算子を同系統の別演算子へ置き換える変異の生成</summary>
public sealed class RelationalPatternMutator : IMutationOperator
{
    /// <summary>関係 pattern の演算子から入れ替え先の列への対応</summary>
    private static readonly Dictionary<SyntaxKind, SyntaxKind[]> Swaps = new()
    {
        [SyntaxKind.GreaterThanToken] = [SyntaxKind.GreaterThanEqualsToken, SyntaxKind.LessThanToken],
        [SyntaxKind.GreaterThanEqualsToken] = [SyntaxKind.GreaterThanToken, SyntaxKind.LessThanEqualsToken],
        [SyntaxKind.LessThanToken] = [SyntaxKind.LessThanEqualsToken, SyntaxKind.GreaterThanEqualsToken],
        [SyntaxKind.LessThanEqualsToken] = [SyntaxKind.LessThanToken, SyntaxKind.GreaterThanToken],
    };

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        if (node is not RelationalPatternSyntax pattern || !Swaps.TryGetValue(pattern.OperatorToken.Kind(), out var replacements))
        {
            yield break;
        }

        if (PatternContext(pattern) is not { } context)
        {
            yield break;
        }

        var patternIndex = context
            .DescendantNodesAndSelf()
            .OfType<RelationalPatternSyntax>()
            .TakeWhile(current => current != pattern)
            .Count();
        foreach (var replacementKind in replacements)
        {
            if (!RelationalPatternSafety.IsSafeReplacement(context, patternIndex, replacementKind, model))
            {
                continue;
            }

            var replacement = pattern.WithOperatorToken(SyntaxFactory.Token(replacementKind));
            var inStatic = MutationContexts.IsStaticContext(context);
            if (context is ExpressionSyntax expression)
            {
                yield return new MutationCandidate.ExpressionSwap(
                    expression,
                    visited => Replace(visited, patternIndex, replacementKind),
                    nameof(RelationalPatternMutator),
                    replacement.NormalizeWhitespace().ToString(),
                    inStatic
                )
                {
                    ReportTarget = pattern,
                };
            }
            else if (context is StatementSyntax statement)
            {
                yield return new MutationCandidate.StatementSwap(
                    statement,
                    visited => Replace(visited, patternIndex, replacementKind),
                    nameof(RelationalPatternMutator),
                    replacement.NormalizeWhitespace().ToString(),
                    inStatic
                )
                {
                    ReportTarget = pattern,
                };
            }
        }
    }

    /// <summary>関係 pattern を含む schemata の置換単位の取り出し</summary>
    private static SyntaxNode? PatternContext(RelationalPatternSyntax pattern)
    {
        var owner = pattern.Ancestors().FirstOrDefault(node =>
            node is IsPatternExpressionSyntax or SwitchExpressionArmSyntax or CasePatternSwitchLabelSyntax
        );
        return owner switch
        {
            IsPatternExpressionSyntax expression => expression,
            SwitchExpressionArmSyntax arm => arm.Ancestors().OfType<SwitchExpressionSyntax>().FirstOrDefault(),
            CasePatternSwitchLabelSyntax label => label.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault(),
            _ => null,
        };
    }

    /// <summary>訪問済みの文脈から指定位置の関係 pattern の演算子だけを替えた再構築</summary>
    private static T Replace<T>(T context, int patternIndex, SyntaxKind replacementKind)
        where T : SyntaxNode
    {
        var current = context.DescendantNodesAndSelf().OfType<RelationalPatternSyntax>().ElementAt(patternIndex);
        var replacement = current.WithOperatorToken(SyntaxFactory.Token(replacementKind));
        return context.ReplaceNode(current, replacement);
    }
}
