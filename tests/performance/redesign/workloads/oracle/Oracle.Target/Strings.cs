using System.Text.RegularExpressions;

namespace Oracle.Target;

/// 文字列操作と正規表現
public static partial class Strings
{
    public static string Normalize(string input) => input.Trim().ToLowerInvariant();

    public static bool IsCommand(string input) => input.StartsWith("/", StringComparison.Ordinal) && input.Length > 1;

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0])));

    public static bool IsProductCode(string code) => ProductCode().IsMatch(code);

    public static string Pad(string value, int width) => value.PadLeft(width, '0');

    [GeneratedRegex("^[A-Z]{3}-[0-9]{4}$")]
    private static partial Regex ProductCode();
}
