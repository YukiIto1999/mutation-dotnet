using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>実行時追跡で包む static 初期化子とその型名の事前算出</summary>
public static class StaticInitializerIndex
{
    /// <summary>目印付きの static 初期化子から、包むときに使う型名への対応の算出</summary>
    /// <param name="root">読込時に目印を付けた構文木の根</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <returns>初期化子の鍵から完全修飾の型名への対応。包めない初期化子は含まない</returns>
    public static IReadOnlyDictionary<string, string> Build(SyntaxNode root, SemanticModel model)
    {
        var index = new Dictionary<string, string>();
        foreach (var initializer in root.GetAnnotatedNodes(StaticContextRewriter.AnnotationKind).OfType<EqualsValueClauseSyntax>())
        {
            var key = StaticContextRewriter.KeyOf(initializer);
            if (key is null || !IsWrappableExpression(initializer.Value))
            {
                continue;
            }

            var type = model.GetTypeInfo(initializer.Value).ConvertedType;
            if (type is null || !IsRenderable(type))
            {
                continue;
            }

            index[key] = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return index;
    }

    /// <summary>TrackStatic で包める式かの判定</summary>
    private static bool IsWrappableExpression(ExpressionSyntax expression) =>
        expression is not (InitializerExpressionSyntax or RefExpressionSyntax or ThrowExpressionSyntax)
        && expression.Kind() is not (SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression)
        && !expression
            .DescendantNodesAndSelf()
            .Any(n => n is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax);

    /// <summary>型名を注入コードに書ける型かの判定</summary>
    private static bool IsRenderable(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => IsRenderable(array.ElementType),
            ITypeParameterSymbol => true,
            INamedTypeSymbol named =>
                !named.IsAnonymousType
                && !named.IsRefLikeType
                && named.TypeKind is not (TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer)
                && named.TypeArguments.All(IsRenderable),
            _ => false,
        };
}
