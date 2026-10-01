namespace Mutation.Shared;

/// <summary>ソース上の 1 始まりの行の閉区間</summary>
/// <param name="First">最初の行</param>
/// <param name="Last">最後の行</param>
public sealed record LineRange(int First, int Last)
{
    /// <summary>ファイルの全行を覆う範囲。未追跡のファイルのように全体が新しいときに使う</summary>
    public static LineRange WholeFile { get; } = new(1, int.MaxValue);

    /// <summary>別の閉区間と一行でも重なるか</summary>
    /// <param name="first">比べる範囲の最初の行</param>
    /// <param name="last">比べる範囲の最後の行</param>
    /// <returns>重なるなら真</returns>
    public bool Overlaps(int first, int last) => First <= last && first <= Last;
}
