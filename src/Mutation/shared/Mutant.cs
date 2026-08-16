
namespace Mutation.Shared;

/// <summary>変異一件の静的な記述</summary>
/// <param name="Id">schemata の活性判定に使う連番</param>
/// <param name="OperatorName">変異を生成した演算子の名前</param>
/// <param name="FilePath">変異元ファイルの絶対 path</param>
/// <param name="Span">変異元のソース上の範囲</param>
/// <param name="Original">変異前のコード断片</param>
/// <param name="Replacement">変異後のコード断片</param>
/// <param name="InStaticContext">static 初期化文脈にあり process 単位の分離が要るか</param>
public sealed record Mutant(
    MutantId Id,
    string OperatorName,
    string FilePath,
    SourceSpan Span,
    string Original,
    string Replacement,
    bool InStaticContext
);
