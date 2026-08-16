
namespace Mutation.Shared;

/// <summary>ソース上の 1 始まりの行・桁で表す範囲</summary>
/// <param name="Line">開始行</param>
/// <param name="Column">開始桁</param>
/// <param name="EndLine">終了行</param>
/// <param name="EndColumn">終了桁</param>
public sealed record SourceSpan(int Line, int Column, int EndLine, int EndColumn);
