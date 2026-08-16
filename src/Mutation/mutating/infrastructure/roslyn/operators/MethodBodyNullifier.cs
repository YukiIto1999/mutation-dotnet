using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>member 本体を先頭で早期脱出させて空振りにする変異の生成</summary>
public sealed class MethodBodyNullifier : IMutationOperator
{
    /// <inheritdoc />
    public IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model)
    {
        var body = node switch
        {
            MethodDeclarationSyntax method => method.Body,
            LocalFunctionStatementSyntax local => local.Body,
            ConstructorDeclarationSyntax ctor => ctor.Body,
            AccessorDeclarationSyntax { RawKind: (int)SyntaxKind.GetAccessorDeclaration or (int)SyntaxKind.SetAccessorDeclaration } accessor
                => accessor.Body,
            _ => null,
        };
        if (body is null || IsTrivialBody(body) || model.GetDeclaredSymbol(node) is not IMethodSymbol symbol)
        {
            yield break;
        }

        var guard = GuardFor(symbol, body);
        if (guard is null)
        {
            yield break;
        }

        yield return new MutationCandidate.BodyGuard(
            body,
            guard,
            nameof(MethodBodyNullifier),
            guard.NormalizeWhitespace().ToString(),
            MutationContexts.IsStaticContext(body)
        );
    }

    /// <summary>戻り値の型に応じた先頭 return の生成。作れなければ不在</summary>
    private static StatementSyntax? GuardFor(IMethodSymbol symbol, BlockSyntax body)
    {
        var blocked =
            symbol.ReturnsByRef
            || symbol.ReturnsByRefReadonly
            || symbol.ReturnType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer
            || symbol.Parameters.Any(p => p.RefKind == RefKind.Out);
        if (blocked)
        {
            return null;
        }

        if (IsIterator(body))
        {
            return SyntaxFactory.YieldStatement(SyntaxKind.YieldBreakStatement);
        }

        if (symbol.ReturnsVoid || IsAsyncBareTask(symbol))
        {
            return SyntaxFactory.ReturnStatement();
        }

        return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression));
    }

    /// <summary>async の Task / ValueTask かの判定</summary>
    private static bool IsAsyncBareTask(IMethodSymbol symbol) =>
        symbol.IsAsync && symbol.ReturnType is INamedTypeSymbol { Arity: 0 };

    /// <summary>yield を含む iterator かの判定</summary>
    private static bool IsIterator(BlockSyntax body) =>
        body.DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<YieldStatementSyntax>()
            .Any();

    /// <summary>空化しても意味のない本体かの判定</summary>
    private static bool IsTrivialBody(BlockSyntax body) =>
        body.Statements.Count == 0 || body.Statements is [ReturnStatementSyntax { Expression: null }];
}
