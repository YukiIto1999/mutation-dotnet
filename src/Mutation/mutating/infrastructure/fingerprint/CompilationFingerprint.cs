using Mutation.Mutating.Domain;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Mutation.Mutating.Infrastructure;

/// <summary>変異の生成入力が前回と同一かを見分ける指紋</summary>
public static class CompilationFingerprint
{
    /// <summary>csc 引数と全ソース内容と選別・検査設定からの指紋の計算</summary>
    /// <param name="invocation">対象 project の csc 呼び出し</param>
    /// <param name="settings">変異の集合と判定に影響する設定の要約</param>
    /// <returns>16 進の指紋。ソース file が読めなければ不在</returns>
    public static string? Compute(CscInvocation invocation, string settings)
    {
        var baseDirectory = Path.GetDirectoryName(invocation.ProjectFile) ?? ".";
        var arguments = CSharpCommandLineParser.Default.Parse(invocation.Arguments, baseDirectory, sdkDirectory: null);
        var builder = new StringBuilder();
        builder.AppendJoin('\n', invocation.Arguments);
        foreach (var path in arguments.SourceFiles.Select(f => f.Path).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(path))
            {
                return null;
            }

            builder.Append('\n').Append(path).Append(':').Append(HashOf(path));
        }

        builder.Append('\n').Append(settings);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>file 内容の SHA-256</summary>
    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
