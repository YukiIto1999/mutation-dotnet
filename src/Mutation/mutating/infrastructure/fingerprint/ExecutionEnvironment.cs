using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Mutation.Mutating.Infrastructure;

/// <summary>worker が継承する実行環境の内容による判定再利用の識別</summary>
internal static class ExecutionEnvironment
{
    /// <summary>環境変数と作業 directory の同一性の計算</summary>
    /// <returns>値そのものを保存しない実行環境の SHA-256</returns>
    public static string Identity()
    {
        var environment = Environment.GetEnvironmentVariables();
        var names = environment.Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray();
        var directory = Environment.CurrentDirectory;
        var capacity = Encoding.UTF8.GetMaxByteCount(directory.Length);
        foreach (var name in names)
        {
            capacity = Math.Max(capacity, Encoding.UTF8.GetMaxByteCount(name.Length));
            capacity = Math.Max(capacity, Encoding.UTF8.GetMaxByteCount(((string)environment[name]!).Length));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(capacity + 1);
        try
        {
            Append(hash, buffer, directory);
            foreach (var name in names)
            {
                Append(hash, buffer, name);
                Append(hash, buffer, (string)environment[name]!);
            }

            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            hash.GetHashAndReset(digest);
            return Convert.ToHexString(digest);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>NUL を含まない実行環境の値の区切り付き hash への追加</summary>
    /// <param name="hash">更新する SHA-256</param>
    /// <param name="buffer">UTF-8 の値と末尾の NUL が収まる作業領域</param>
    /// <param name="value">環境変数の名前か値または作業 directory</param>
    private static void Append(IncrementalHash hash, byte[] buffer, string value)
    {
        var count = Encoding.UTF8.GetBytes(value, buffer);
        buffer[count] = 0;
        hash.AppendData(buffer.AsSpan(0, count + 1));
    }
}
