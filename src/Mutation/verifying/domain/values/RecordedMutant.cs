using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>報告に記録された変異一件と、その確定結果</summary>
/// <param name="Id">報告での変異の識別子</param>
/// <param name="FilePath">変異を置いたファイルの絶対 path</param>
/// <param name="Span">変異を置いた範囲</param>
/// <param name="OperatorName">変異演算子の名前</param>
/// <param name="Replacement">置き換えた後の式または文</param>
/// <param name="Verdict">確定結果</param>
public sealed record RecordedMutant(
    string Id,
    string FilePath,
    SourceSpan Span,
    string OperatorName,
    string Replacement,
    MutantVerdict Verdict
);
