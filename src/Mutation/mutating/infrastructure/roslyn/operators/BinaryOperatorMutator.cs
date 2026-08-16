using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>二項演算子を同系統の別演算子へ置き換える変異の生成</summary>
public sealed class BinaryOperatorMutator : IMutationOperator
{
    /// <summary>二項演算子から入れ替え先の列への対応</summary>
    private static readonly Dictionary<SyntaxKind, SyntaxKind[]> Swaps = new()
    {
        [SyntaxKind.AddExpression] = [SyntaxKind.SubtractExpression],
        [SyntaxKind.SubtractExpression] = [SyntaxKind.AddExpression],
        [SyntaxKind.MultiplyExpression] = [SyntaxKind.DivideExpression],
        [SyntaxKind.DivideExpression] = [SyntaxKind.MultiplyExpression],
        [SyntaxKind.ModuloExpression] = [SyntaxKind.MultiplyExpression],
        [SyntaxKind.LessThanExpression] =
        [
            SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanOrEqualExpression,
        ],
        [SyntaxKind.LessThanOrEqualExpression] = [SyntaxKind.LessThanExpression, SyntaxKind.GreaterThanExpression],
        [SyntaxKind.GreaterThanExpression] =
        [
            SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.LessThanOrEqualExpression,
        ],
        [SyntaxKind.GreaterThanOrEqualExpression] = [SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression],
        [SyntaxKind.EqualsExpression] = [SyntaxKind.NotEqualsExpression],
        [SyntaxKind.NotEqualsExpression] = [SyntaxKind.EqualsExpression],
        [SyntaxKind.LogicalAndExpression] = [SyntaxKind.LogicalOrExpression],
        [SyntaxKind.LogicalOrExpression] = [SyntaxKind.LogicalAndExpression],
        [SyntaxKind.BitwiseAndExpression] = [SyntaxKind.BitwiseOrExpression],
        [SyntaxKind.BitwiseOrExpression] = [SyntaxKind.BitwiseAndExpression],
        [SyntaxKind.ExclusiveOrExpression] = [SyntaxKind.BitwiseAndExpression],
        [SyntaxKind.LeftShiftExpression] = [SyntaxKind.RightShiftExpression],
        [SyntaxKind.RightShiftExpression] = [SyntaxKind.LeftShiftExpression],
        [SyntaxKind.UnsignedRightShiftExpression] = [SyntaxKind.LeftShiftExpression],
    };

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        if (node is not BinaryExpressionSyntax binary || !Swaps.TryGetValue(binary.Kind(), out var replacements))
        {
            yield break;
        }

        var inStatic = MutationContexts.IsStaticContext(binary);
        foreach (var replacementKind in replacements)
        {
            if (!IsApplicable(binary, replacementKind, model))
            {
                continue;
            }

            var display = SyntaxFactory
                .BinaryExpression(replacementKind, binary.Left, binary.Right)
                .NormalizeWhitespace()
                .ToString();
            yield return new MutationCandidate.ExpressionSwap(
                binary,
                visited => Rebuild(visited, replacementKind),
                nameof(BinaryOperatorMutator),
                display,
                inStatic
            );
        }
    }

    /// <summary>訪問済みの二項式の、演算子だけを替えた再構築</summary>
    private static BinaryExpressionSyntax Rebuild(ExpressionSyntax visited, SyntaxKind replacementKind)
    {
        var current = (BinaryExpressionSyntax)visited;
        return SyntaxFactory.BinaryExpression(replacementKind, current.Left, current.Right);
    }

    /// <summary>入れ替え先の演算子が operand の型で成立するかの判定</summary>
    private static bool IsApplicable(BinaryExpressionSyntax binary, SyntaxKind replacementKind, SemanticModel model)
    {
        var left = Unwrap(model.GetTypeInfo(binary.Left).Type);
        var right = Unwrap(model.GetTypeInfo(binary.Right).Type);
        if (left is null || right is null || IsExcludedOperand(left) || IsExcludedOperand(right))
        {
            return false;
        }

        if (binary.Kind() == SyntaxKind.AddExpression && (IsStringOrDelegate(left) || IsStringOrDelegate(right)))
        {
            return false;
        }

        if (binary.Kind() == SyntaxKind.SubtractExpression && (IsDelegate(left) || IsDelegate(right)))
        {
            return false;
        }

        if (model.GetSymbolInfo(binary).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator })
        {
            return true;
        }

        var speculative = SyntaxFactory.BinaryExpression(replacementKind, binary.Left, binary.Right);
        return SpeculativeBinding.BindsCleanly(model, binary, speculative);
    }

    /// <summary>Nullable を剥がした型の取り出し</summary>
    private static ITypeSymbol? Unwrap(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : type;

    /// <summary>算術の入れ替えを許さない operand 型かの判定</summary>
    private static bool IsExcludedOperand(ITypeSymbol type) =>
        type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic or TypeKind.Error;

    /// <summary>string か delegate かの判定</summary>
    private static bool IsStringOrDelegate(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String || IsDelegate(type);

    /// <summary>delegate 型かの判定</summary>
    private static bool IsDelegate(ITypeSymbol type) => type.TypeKind == TypeKind.Delegate;
}
