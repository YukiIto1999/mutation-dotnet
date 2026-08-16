using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mutation.Mutating.Infrastructure.Roslyn.Operators;

/// <summary>署名が対称な method 対の入れ替え仕様</summary>
/// <param name="Pairs">入れ替え可能な method 名の対応</param>
/// <param name="Belongs">解決した method が対象の型に属するかの判定</param>
/// <param name="ArityAllows">引数の数が対の入れ替えを許すかの判定</param>
/// <param name="OperatorName">候補に載せる演算子の名前</param>
public sealed record MethodPairSpec(
    IReadOnlyDictionary<string, string> Pairs,
    Func<IMethodSymbol, bool> Belongs,
    Func<string, InvocationExpressionSyntax, bool> ArityAllows,
    string OperatorName
);

/// <summary>署名が対称な method 対の呼び出しを入れ替える変異の共通機構</summary>
public static class MethodPairSwaps
{
    /// <summary>呼び出しが対の条件を満たすときの、名前を入れ替える候補の生成</summary>
    /// <param name="node">走査中 node</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <param name="spec">入れ替え仕様</param>
    /// <returns>条件を満たす候補。満たさなければ空</returns>
    public static IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model, MethodPairSpec spec)
    {
        if (
            node is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier }
            } invocation
        )
        {
            yield break;
        }

        var name = identifier.Identifier.ValueText;
        if (!spec.Pairs.TryGetValue(name, out var replacementName))
        {
            yield break;
        }

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method || !spec.Belongs(method))
        {
            yield break;
        }

        if (!spec.ArityAllows(name, invocation))
        {
            yield break;
        }

        yield return new MutationCandidate.ExpressionSwap(
            invocation,
            visited => Rename(visited, replacementName),
            spec.OperatorName,
            Rename(invocation, replacementName).NormalizeWhitespace().ToString(),
            MutationContexts.IsStaticContext(invocation)
        );
    }

    /// <summary>member access の名前だけを替えた呼び出しの再構築</summary>
    private static InvocationExpressionSyntax Rename(ExpressionSyntax visited, string replacementName)
    {
        var invocation = (InvocationExpressionSyntax)visited;
        var access = (MemberAccessExpressionSyntax)invocation.Expression;
        var name = (IdentifierNameSyntax)access.Name;
        return invocation.WithExpression(access.WithName(name.WithIdentifier(SyntaxFactory.Identifier(replacementName))));
    }
}
