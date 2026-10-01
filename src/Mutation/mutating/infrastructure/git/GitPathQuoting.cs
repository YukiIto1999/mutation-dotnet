using System.Text;

namespace Mutation.Mutating.Infrastructure.Git;

/// <summary>git が特殊な文字を含む path に付ける C 形式の引用の解除</summary>
public static class GitPathQuoting
{
    /// <summary>逆斜線の後の一文字から、表す byte への対応。それ以外は 3 桁の 8 進数</summary>
    private static readonly Dictionary<char, byte> Escapes = new()
    {
        ['a'] = 0x07,
        ['b'] = 0x08,
        ['t'] = 0x09,
        ['n'] = 0x0A,
        ['v'] = 0x0B,
        ['f'] = 0x0C,
        ['r'] = 0x0D,
        ['"'] = (byte)'"',
        ['\\'] = (byte)'\\',
    };

    /// <summary>引用された path の、元の path への復元。引用されていなければそのまま</summary>
    /// <param name="path">git が出力した path</param>
    /// <returns>引用を解いた path</returns>
    public static string Unquote(string path)
    {
        if (path.Length < 2 || path[0] != '"' || path[^1] != '"')
        {
            return path;
        }

        var body = path[1..^1];
        var bytes = new List<byte>(body.Length);
        var index = 0;
        while (index < body.Length)
        {
            var escape = body.IndexOf('\\', index);
            var end = escape < 0 ? body.Length : escape;
            bytes.AddRange(Encoding.UTF8.GetBytes(body[index..end]));
            index = escape < 0 ? body.Length : Unescape(body, escape + 1, bytes);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>逆斜線の直後からの一つの escape の byte への復元</summary>
    /// <returns>escape の次の位置</returns>
    private static int Unescape(string body, int start, List<byte> bytes)
    {
        if (Escapes.TryGetValue(body[start], out var escaped))
        {
            bytes.Add(escaped);
            return start + 1;
        }

        bytes.Add(Convert.ToByte(body.Substring(start, 3), 8));
        return start + 3;
    }
}
