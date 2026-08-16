using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>変異後の式が型付けできるかの投機的な検査</summary>
public static class SpeculativeBinding
{
    /// <summary>置換候補の式が元の位置で誤りなく型付けされるか</summary>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <param name="original">変異元の式。束縛位置の基準になる</param>
    /// <param name="replacement">検査する変異後の式</param>
    /// <returns>型付けできるなら真</returns>
    public static bool BindsCleanly(SemanticModel model, ExpressionSyntax original, ExpressionSyntax replacement)
    {
        var info = model.GetSpeculativeTypeInfo(
            original.SpanStart,
            replacement,
            SpeculativeBindingOption.BindAsExpression
        );
        return info.Type is { TypeKind: not TypeKind.Error };
    }
}
