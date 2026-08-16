using System.Text;
using System.Text.RegularExpressions;

namespace Mutation.Mutating.Domain;

/// <summary>変異対象に含めるファイルを glob で決める範囲</summary>
public sealed class MutationScope
{
    /// <summary>宣言順に評価する包含・除外の規則の列</summary>
    private readonly IReadOnlyList<(Regex Pattern, bool Exclude)> rules;

    private MutationScope(IReadOnlyList<(Regex, bool)> rules)
    {
        this.rules = rules;
    }

    /// <summary>Stryker と同じ流儀の glob 列からの範囲の構築。先頭 `!` は除外、後勝ち</summary>
    /// <param name="patterns">project directory からの相対 glob の列。空なら全ファイル</param>
    /// <returns>組み立てた範囲</returns>
    public static MutationScope FromPatterns(IEnumerable<string> patterns)
    {
        var rules = new List<(Regex, bool)>();
        var any = false;
        foreach (var raw in patterns)
        {
            var exclude = raw.StartsWith('!');
            var glob = exclude ? raw[1..] : raw;
            rules.Add((ToRegex(glob), exclude));
            any |= !exclude;
        }

        if (!any)
        {
            rules.Insert(0, (ToRegex("**/*.cs"), false));
        }

        return new MutationScope(rules);
    }

    /// <summary>ファイルが変異対象に含まれるか</summary>
    /// <param name="relativePath">project directory からの相対 path</param>
    /// <returns>含めるなら真</returns>
    public bool Includes(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("/obj/", StringComparison.Ordinal) || normalized.StartsWith("obj/", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.StartsWith("bin/", StringComparison.Ordinal))
        {
            return false;
        }

        var included = false;
        foreach (var (pattern, exclude) in rules)
        {
            if (pattern.IsMatch(normalized))
            {
                included = !exclude;
            }
        }

        return included;
    }

    /// <summary>glob から `/` 区切りの相対 path に対する正規表現への変換</summary>
    private static Regex ToRegex(string glob)
    {
        var normalized = glob.Replace('\\', '/').TrimStart('/');
        var builder = new StringBuilder("^");
        var i = 0;
        while (i < normalized.Length)
        {
            var c = normalized[i];
            if (c == '*' && i + 1 < normalized.Length && normalized[i + 1] == '*')
            {
                var slashFollows = i + 2 < normalized.Length && normalized[i + 2] == '/';
                builder.Append(slashFollows ? "(?:.*/)?" : ".*");
                i += slashFollows ? 3 : 2;
                continue;
            }

            builder.Append(
                c switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(c.ToString()),
                }
            );
            i++;
        }

        builder.Append('$');
        return new Regex(
            builder.ToString(),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );
    }
}
