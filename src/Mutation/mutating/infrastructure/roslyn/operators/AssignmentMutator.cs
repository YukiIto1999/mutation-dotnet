using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>複合代入演算子を同系統の別演算子へ置き換える変異の生成</summary>
public sealed class AssignmentMutator : IMutationOperator
{
    /// <summary>複合代入の演算子から入れ替え先への対応</summary>
    private static readonly Dictionary<SyntaxKind, SyntaxKind> Swaps = new()
    {
        [SyntaxKind.AddAssignmentExpression] = SyntaxKind.SubtractAssignmentExpression,
        [SyntaxKind.SubtractAssignmentExpression] = SyntaxKind.AddAssignmentExpression,
        [SyntaxKind.MultiplyAssignmentExpression] = SyntaxKind.DivideAssignmentExpression,
        [SyntaxKind.DivideAssignmentExpression] = SyntaxKind.MultiplyAssignmentExpression,
        [SyntaxKind.ModuloAssignmentExpression] = SyntaxKind.MultiplyAssignmentExpression,
        [SyntaxKind.LeftShiftAssignmentExpression] = SyntaxKind.RightShiftAssignmentExpression,
        [SyntaxKind.RightShiftAssignmentExpression] = SyntaxKind.LeftShiftAssignmentExpression,
        [SyntaxKind.UnsignedRightShiftAssignmentExpression] = SyntaxKind.LeftShiftAssignmentExpression,
        [SyntaxKind.AndAssignmentExpression] = SyntaxKind.OrAssignmentExpression,
        [SyntaxKind.OrAssignmentExpression] = SyntaxKind.AndAssignmentExpression,
        [SyntaxKind.ExclusiveOrAssignmentExpression] = SyntaxKind.AndAssignmentExpression,
    };

    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        if (node is not AssignmentExpressionSyntax assignment || !Swaps.TryGetValue(assignment.Kind(), out var kind))
        {
            yield break;
        }

        if (!IsApplicable(assignment, kind, model))
        {
            yield break;
        }

        var inStatic = MutationContexts.IsStaticContext(assignment);
        var display = SyntaxFactory
            .AssignmentExpression(kind, assignment.Left, assignment.Right)
            .NormalizeWhitespace()
            .ToString();
        yield return new MutationCandidate.ExpressionSwap(
            assignment,
            visited => RebuildExpression(visited, kind),
            nameof(AssignmentMutator),
            display,
            inStatic
        );
    }

    /// <summary>訪問済みの代入式の、演算子だけを替えた再構築</summary>
    private static AssignmentExpressionSyntax RebuildExpression(ExpressionSyntax visited, SyntaxKind kind)
    {
        var current = (AssignmentExpressionSyntax)visited;
        return SyntaxFactory.AssignmentExpression(kind, current.Left, current.Right);
    }

    /// <summary>入れ替え先の演算子が operand の型で成立するかの判定</summary>
    private static bool IsApplicable(AssignmentExpressionSyntax assignment, SyntaxKind kind, SemanticModel model)
    {
        if (model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol)
        {
            return false;
        }

        var leftType = model.GetTypeInfo(assignment.Left).Type;
        if (leftType is null || leftType.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (leftType.TypeKind is TypeKind.Delegate or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.Error)
        {
            return false;
        }

        if (model.GetSymbolInfo(assignment).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator })
        {
            return true;
        }

        var speculative = SyntaxFactory.AssignmentExpression(kind, assignment.Left, assignment.Right);
        return SpeculativeBinding.BindsCleanly(model, assignment, speculative);
    }
}
