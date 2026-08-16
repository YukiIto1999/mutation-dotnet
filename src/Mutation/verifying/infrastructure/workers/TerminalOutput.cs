using System.Text.RegularExpressions;

namespace Mutation.Verifying.Infrastructure.Workers;

/// <summary>テスト実行体が端末へ書いた行からの事実の抽出</summary>
public static partial class TerminalOutput
{
    /// <summary>Microsoft.Testing.Platform の端末出力からの、最初に失敗したテストの表示名の取り出し</summary>
    /// <param name="lines">一実行の間に観測した出力行</param>
    /// <returns>失敗したテストの表示名。見つからなければ不在</returns>
    public static string? KillerTest(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var match = FailedLine().Match(line);
            if (match.Success)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\s*failed (?<name>.+?) \(\d+(\.\d+)?\s*(ms|s|m|h)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FailedLine();
}
