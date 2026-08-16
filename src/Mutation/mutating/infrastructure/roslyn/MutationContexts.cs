using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>変異の可否を決める構文文脈の判定</summary>
public static class MutationContexts
{
    /// <summary>schemata を注入できない文脈(定数要求文脈と式木)にあるかの判定</summary>
    /// <param name="node">変異候補の対象 node</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <returns>注入できない文脈なら真</returns>
    public static bool IsForbidden(SyntaxNode node, SemanticModel model)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var forbidden = ancestor
                is AttributeSyntax
                    or ParameterSyntax
                    or EnumMemberDeclarationSyntax
                    or CaseSwitchLabelSyntax
                    or GotoStatementSyntax
                    or PatternSyntax
                    or UsingDirectiveSyntax;
            if (forbidden || IsConstDeclaration(ancestor) || IsExpressionTreeLambda(ancestor, model))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>三項演算子の schemata で包むと壊れる式(変数宣言、stackalloc、ref、`?.` の連鎖から切り離される member binding)かの判定</summary>
    /// <param name="expression">変異候補の対象式</param>
    /// <returns>式を包むか複製すると束縛を壊すなら真</returns>
    public static bool HasExpressionHazard(ExpressionSyntax expression) =>
        expression
            .DescendantNodesAndSelf()
            .Any(n =>
                n
                    is DeclarationExpressionSyntax
                        or SingleVariableDesignationSyntax
                        or StackAllocArrayCreationExpressionSyntax
                        or ImplicitStackAllocArrayCreationExpressionSyntax
                        or RefExpressionSyntax
                || IsDetachedConditionalBinding(expression, n)
            );

    /// <summary>`?.` 連鎖の member binding を対象式の外に持つ式かの判定</summary>
    private static bool IsDetachedConditionalBinding(ExpressionSyntax expression, SyntaxNode node)
    {
        if (node is not (MemberBindingExpressionSyntax or ElementBindingExpressionSyntax))
        {
            return false;
        }

        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ConditionalAccessExpressionSyntax)
            {
                return false;
            }

            if (ancestor == expression)
            {
                return true;
            }
        }

        return true;
    }

    /// <summary>process 起動時に一度しか実行されない static 初期化文脈にあるか</summary>
    /// <param name="node">変異候補の対象 node</param>
    /// <returns>static constructor か static member 初期化子の中なら真</returns>
    public static bool IsStaticContext(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax)
            {
                return false;
            }

            if (ancestor is ConstructorDeclarationSyntax ctor)
            {
                return ctor.Modifiers.Any(SyntaxKind.StaticKeyword);
            }

            if (ancestor is EqualsValueClauseSyntax initializer && IsStaticInitializer(initializer))
            {
                return true;
            }

            if (ancestor is MethodDeclarationSyntax method)
            {
                return method.AttributeLists.SelectMany(l => l.Attributes)
                    .Any(a => a.Name.ToString().Contains("ModuleInitializer", StringComparison.Ordinal));
            }
        }

        return false;
    }

    /// <summary>static field / property の初期化子かの判定</summary>
    private static bool IsStaticInitializer(EqualsValueClauseSyntax initializer) =>
        initializer.Parent switch
        {
            VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } =>
                field.Modifiers.Any(SyntaxKind.StaticKeyword),
            PropertyDeclarationSyntax property => property.Modifiers.Any(SyntaxKind.StaticKeyword),
            _ => false,
        };

    /// <summary>const 宣言の中かの判定</summary>
    private static bool IsConstDeclaration(SyntaxNode node) =>
        node switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.ConstKeyword),
            LocalDeclarationStatementSyntax local => local.Modifiers.Any(SyntaxKind.ConstKeyword),
            _ => false,
        };

    /// <summary>式木へ変換される lambda の中かの判定</summary>
    private static bool IsExpressionTreeLambda(SyntaxNode node, SemanticModel model)
    {
        if (node is not AnonymousFunctionExpressionSyntax lambda)
        {
            return false;
        }

        return model.GetTypeInfo(lambda).ConvertedType
            is INamedTypeSymbol
            {
                Name: "Expression",
                ContainingNamespace:
                {
                    Name: "Expressions",
                    ContainingNamespace:
                    {
                        Name: "Linq",
                        ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                    }
                }
            };
    }
}
