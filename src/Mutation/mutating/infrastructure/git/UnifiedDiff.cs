using Mutation.Shared;
using System.Globalization;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>`git diff -U0` の出力からの、新しい側で変わった行の読み取り</summary>
public static class UnifiedDiff
{
    /// <summary>差分を出させるときに新しい側の path へ付けさせる接頭辞。`--dst-prefix` に渡す値</summary>
    public const string NewSidePrefix = "b/";

    /// <summary>新しい側の file 名を示す見出しの接頭辞</summary>
    private const string NewFileHeader = "+++ ";

    /// <summary>差分の出力から、ファイルごとの変わった行の範囲の読み取り</summary>
    /// <remarks>見出しの path は <see cref="NewSidePrefix"/> 付きで出させた形を前提にする。削除されたファイルは含めない</remarks>
    /// <param name="root">差分の path の基準になる repository の root</param>
    /// <param name="lines">`git diff -U0 --dst-prefix=b/` の出力の行</param>
    /// <returns>ファイルの絶対 path から、変わった行の範囲の列への対応</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<LineRange>> Parse(string root, IEnumerable<string> lines)
    {
        var ranges = new Dictionary<string, List<LineRange>>(StringComparer.Ordinal);
        List<LineRange>? current = null;
        var inHeader = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inHeader = true;
            }
            else if (inHeader && line.StartsWith(NewFileHeader, StringComparison.Ordinal))
            {
                current = NewSide(root, line[NewFileHeader.Length..], ranges);
            }
            else if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                inHeader = false;
                current?.AddRange(AddedLines(line));
            }
        }

        return ranges.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<LineRange>)pair.Value,
            StringComparer.Ordinal
        );
    }

    /// <summary>見出しが指す新しい側のファイルの、空の範囲の列の登録。削除されたファイルなら不在</summary>
    private static List<LineRange>? NewSide(string root, string headerPath, Dictionary<string, List<LineRange>> ranges)
    {
        if (headerPath == "/dev/null")
        {
            return null;
        }

        var list = new List<LineRange>();
        ranges[Path.GetFullPath(Path.Combine(root, GitPathQuoting.Unquote(headerPath)[NewSidePrefix.Length..]))] = list;
        return list;
    }

    /// <summary>hunk 見出し `@@ -a,b +c,d @@` の新しい側の範囲。行数 0 は削除だけの hunk で範囲を持たない</summary>
    private static IEnumerable<LineRange> AddedLines(string hunkHeader)
    {
        var newSide = hunkHeader.Split(' ')[2][1..].Split(',');
        var start = int.Parse(newSide[0], CultureInfo.InvariantCulture);
        var count = newSide.Length > 1 ? int.Parse(newSide[1], CultureInfo.InvariantCulture) : 1;
        return count == 0 ? [] : [new LineRange(start, start + count - 1)];
    }
}
