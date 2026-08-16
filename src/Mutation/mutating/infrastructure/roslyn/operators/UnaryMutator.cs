using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>単項演算子の除去と増減演算子の反転による変異の生成</summary>
public sealed class UnaryMutator : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model) =>
        node switch
        {
            PrefixUnaryExpressionSyntax prefix => PrefixCandidates(prefix, model),
            PostfixUnaryExpressionSyntax postfix => StepCandidates(
                postfix,
                postfix.Kind() == SyntaxKind.PostIncrementExpression
                    ? SyntaxKind.PostDecrementExpression
                    : SyntaxKind.PostIncrementExpression,
                model
            ),
            _ => [],
        };

    /// <summary>前置演算子の候補の列挙</summary>
    private static IEnumerable<MutationCandidate> PrefixCandidates(
        PrefixUnaryExpressionSyntax prefix,
        SemanticModel model
    ) =>
        prefix.Kind() switch
        {
            SyntaxKind.LogicalNotExpression or SyntaxKind.UnaryMinusExpression => DropCandidates(prefix, model),
            SyntaxKind.PreIncrementExpression => StepCandidates(prefix, SyntaxKind.PreDecrementExpression, model),
            SyntaxKind.PreDecrementExpression => StepCandidates(prefix, SyntaxKind.PreIncrementExpression, model),
            _ => [],
        };

    /// <summary>否定や符号を落とす候補の列挙</summary>
    private static IEnumerable<MutationCandidate> DropCandidates(PrefixUnaryExpressionSyntax prefix, SemanticModel model)
    {
        var operandType = model.GetTypeInfo(prefix.Operand).Type;
        var contextType = model.GetTypeInfo(prefix).ConvertedType;
        if (operandType is null || contextType is null)
        {
            yield break;
        }

        var conversion = model.Compilation.ClassifyCommonConversion(operandType, contextType);
        if (!conversion.IsImplicit)
        {
            yield break;
        }

        yield return new MutationCandidate.ExpressionSwap(
            prefix,
            visited => ((PrefixUnaryExpressionSyntax)visited).Operand,
            nameof(UnaryMutator),
            prefix.Operand.NormalizeWhitespace().ToString(),
            MutationContexts.IsStaticContext(prefix)
        );
    }

    /// <summary>増減演算子を入れ替える候補の列挙</summary>
    private static IEnumerable<MutationCandidate> StepCandidates(
        ExpressionSyntax step,
        SyntaxKind replacementKind,
        SemanticModel model
    )
    {
        if (!StepApplicable(step, replacementKind, model))
        {
            yield break;
        }

        var display = Rebuild(step, replacementKind).NormalizeWhitespace().ToString();
        var inStatic = MutationContexts.IsStaticContext(step);
        yield return new MutationCandidate.ExpressionSwap(
            step,
            visited => Rebuild(visited, replacementKind),
            nameof(UnaryMutator),
            display,
            inStatic
        );
    }

    /// <summary>増減の入れ替えが operand の型で成立するかの判定</summary>
    private static bool StepApplicable(ExpressionSyntax step, SyntaxKind replacementKind, SemanticModel model)
    {
        var operand = Operand(step);
        var type = model.GetTypeInfo(operand).Type;
        if (type is null || type.TypeKind is TypeKind.Pointer or TypeKind.Dynamic or TypeKind.Error)
        {
            return false;
        }

        if (model.GetSymbolInfo(step).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator })
        {
            return true;
        }

        return SpeculativeBinding.BindsCleanly(model, step, Rebuild(step, replacementKind));
    }

    /// <summary>増減式の operand の取り出し</summary>
    private static ExpressionSyntax Operand(ExpressionSyntax step) =>
        step switch
        {
            PrefixUnaryExpressionSyntax prefix => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix => postfix.Operand,
            _ => step,
        };

    /// <summary>訪問済みの増減式の、演算子だけを替えた再構築</summary>
    private static ExpressionSyntax Rebuild(ExpressionSyntax visited, SyntaxKind replacementKind) =>
        visited switch
        {
            PrefixUnaryExpressionSyntax prefix => SyntaxFactory.PrefixUnaryExpression(replacementKind, prefix.Operand),
            PostfixUnaryExpressionSyntax postfix => SyntaxFactory.PostfixUnaryExpression(
                replacementKind,
                postfix.Operand
            ),
            _ => visited,
        };
}
