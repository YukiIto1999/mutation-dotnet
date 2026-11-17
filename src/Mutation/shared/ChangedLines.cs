namespace Mutation.Shared;

/// <summary>差分の基点から内容が変わったファイルと、その中で変わった行の範囲</summary>
public sealed class ChangedLines
{
    /// <summary>ファイルの絶対 path から、変わった行の範囲の列への対応。行の削除だけのファイルや消えたファイルは空の列を持つ</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyList<LineRange>> ranges;

    /// <summary>ファイルごとの変わった行の範囲からの組み立て</summary>
    /// <param name="ranges">ファイルの絶対 path から、変わった行の範囲の列への対応</param>
    /// <param name="deleted">基点にあって作業木から消えたファイルの絶対 path の集合。<paramref name="ranges"/> にも空の列で含める。なければ空</param>
    public ChangedLines(IReadOnlyDictionary<string, IReadOnlyList<LineRange>> ranges, IReadOnlySet<string>? deleted = null)
    {
        this.ranges = ranges;
        Files = ranges.Keys.ToHashSet(StringComparer.Ordinal);
        Deleted = deleted ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>内容が変わったファイルの絶対 path の集合。消えたファイルを含む</summary>
    public IReadOnlySet<string> Files { get; }

    /// <summary>基点にあって作業木から消えたファイルの絶対 path の集合。移動と改名は、移動元の消失と移動先の追加になる</summary>
    public IReadOnlySet<string> Deleted { get; }

    /// <summary>ファイル上の行の閉区間が、変わった行と一行でも重なるか</summary>
    /// <param name="absolutePath">ファイルの絶対 path</param>
    /// <param name="first">範囲の最初の行</param>
    /// <param name="last">範囲の最後の行</param>
    /// <returns>重なるなら真</returns>
    public bool Overlaps(string absolutePath, int first, int last) =>
        RangesOf(absolutePath) is { } changed && changed.Any(range => range.Overlaps(first, last));

    /// <summary>ファイルの変わった行の範囲の列。内容が変わっていないファイルなら不在</summary>
    /// <param name="absolutePath">ファイルの絶対 path</param>
    /// <returns>変わった行の範囲の列。行の削除だけのファイルは空の列</returns>
    public IReadOnlyList<LineRange>? RangesOf(string absolutePath) =>
        ranges.TryGetValue(Path.GetFullPath(absolutePath), out var changed) ? changed : null;
}
