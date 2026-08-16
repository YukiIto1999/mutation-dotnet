using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>構文木上の一箇所に適用できる変異の候補</summary>
/// <param name="Target">変異元となる元の構文木上の node</param>
/// <param name="OperatorName">候補を生成した演算子の名前</param>
/// <param name="ReplacementText">報告に載せる変異後のコード断片</param>
/// <param name="InStaticContext">static 初期化文脈にあるか</param>
[ClosedUnion]
public abstract record MutationCandidate(
    SyntaxNode Target,
    string OperatorName,
    string ReplacementText,
    bool InStaticContext
)
{
    /// <summary>式を別の式へ置き換える候補。三項演算子の schemata への織り込み</summary>
    /// <param name="Expression">変異元の式</param>
    /// <param name="Apply">子孫の schemata 化が済んだ式から変異後の式を作る変換</param>
    /// <param name="OperatorName">候補を生成した演算子の名前</param>
    /// <param name="ReplacementText">報告に載せる変異後のコード断片</param>
    /// <param name="InStaticContext">static 初期化文脈にあるか</param>
    public sealed record ExpressionSwap(
        ExpressionSyntax Expression,
        Func<ExpressionSyntax, ExpressionSyntax> Apply,
        string OperatorName,
        string ReplacementText,
        bool InStaticContext
    ) : MutationCandidate(Expression, OperatorName, ReplacementText, InStaticContext);

    /// <summary>文を別の文へ置き換える候補。if の分岐の schemata への織り込み</summary>
    /// <param name="Statement">変異元の文</param>
    /// <param name="Apply">子孫の schemata 化が済んだ文から変異後の文を作る変換</param>
    /// <param name="OperatorName">候補を生成した演算子の名前</param>
    /// <param name="ReplacementText">報告に載せる変異後のコード断片</param>
    /// <param name="InStaticContext">static 初期化文脈にあるか</param>
    public sealed record StatementSwap(
        StatementSyntax Statement,
        Func<StatementSyntax, StatementSyntax> Apply,
        string OperatorName,
        string ReplacementText,
        bool InStaticContext
    ) : MutationCandidate(Statement, OperatorName, ReplacementText, InStaticContext);

    /// <summary>member 本体の先頭で早期脱出させる候補</summary>
    /// <param name="Body">変異元の本体 block</param>
    /// <param name="Guard">本体先頭へ挿す脱出文</param>
    /// <param name="OperatorName">候補を生成した演算子の名前</param>
    /// <param name="ReplacementText">報告に載せる変異後のコード断片</param>
    /// <param name="InStaticContext">static 初期化文脈にあるか</param>
    public sealed record BodyGuard(
        BlockSyntax Body,
        StatementSyntax Guard,
        string OperatorName,
        string ReplacementText,
        bool InStaticContext
    ) : MutationCandidate(Body, OperatorName, ReplacementText, InStaticContext);

    /// <summary>値を返さない式本体の式を置き換える候補。block に開いた if の分岐への織り込み</summary>
    /// <param name="Owner">式本体を持つ member か lambda</param>
    /// <param name="Apply">子孫の schemata 化が済んだ本体の式から変異後の式を作る変換</param>
    /// <param name="OperatorName">候補を生成した演算子の名前</param>
    /// <param name="ReplacementText">報告に載せる変異後のコード断片</param>
    /// <param name="InStaticContext">static 初期化文脈にあるか</param>
    public sealed record VoidArrowSwap(
        SyntaxNode Owner,
        Func<ExpressionSyntax, ExpressionSyntax> Apply,
        string OperatorName,
        string ReplacementText,
        bool InStaticContext
    ) : MutationCandidate(Owner, OperatorName, ReplacementText, InStaticContext);
}
