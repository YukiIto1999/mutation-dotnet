using Microsoft.CodeAnalysis;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>構文木の一 node から変異候補を導く演算子</summary>
public interface IMutationOperator
{
    /// <summary>--ignore-operators の値と突き合わせる演算子の名前</summary>
    string Name => GetType().Name;

    /// <summary>node に適用できる変異候補の列挙</summary>
    /// <param name="node">元の構文木上の走査中 node</param>
    /// <param name="model">対象ファイルの意味解析</param>
    /// <returns>適用できる候補。なければ空</returns>
    IEnumerable<MutationCandidate> Candidates(SyntaxNode node, SemanticModel model);
}
