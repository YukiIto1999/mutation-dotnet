using Mutation.Domain;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Mutation.Infrastructure;

/// <summary>mutant の判定を決める入力の閉包の、内容 hash での記述</summary>
/// <remarks>
/// 読めない入力が一つでもあれば、閉包の同一性は示せないので記述を返さない。呼び出し側は、その結果を再利用の根拠にしない
/// test project の外の file、network、時刻は閉包に入れない
/// </remarks>
internal static class InputClosure
{
    /// <summary>csc 呼び出しの入力のうち、内容が生成物の振る舞いへ届く file の、重複のない絶対 path の列</summary>
    /// <remarks>ソース、additional file、embed する file、analyzer config、参照する assembly、analyzer と generator、resource、署名の鍵</remarks>
    /// <param name="arguments">解釈した csc の引数</param>
    /// <param name="rawArguments">csc の引数の文字列。解釈の結果に path が出ない resource の指定を読む</param>
    /// <param name="baseDirectory">相対 path の基準にする project の directory</param>
    /// <returns>並べ替えた絶対 path の列</returns>
    public static IReadOnlyList<string> CompileInputs(
        CSharpCommandLineArguments arguments,
        IReadOnlyList<string> rawArguments,
        string baseDirectory
    )
    {
        var declared = new List<string?>();
        declared.AddRange(arguments.SourceFiles.Select(file => file.Path));
        declared.AddRange(arguments.AdditionalFiles.Select(file => file.Path));
        declared.AddRange(arguments.EmbeddedFiles.Select(file => file.Path));
        declared.AddRange(arguments.AnalyzerConfigPaths);
        declared.AddRange(arguments.MetadataReferences.Select(reference => reference.Reference));
        declared.AddRange(arguments.AnalyzerReferences.Select(reference => reference.FilePath));
        declared.AddRange(ResourcePaths(rawArguments));
        declared.Add(arguments.CompilationOptions.CryptoKeyFile);
        return
        [
            .. declared
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => Path.GetFullPath(path!, baseDirectory))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>test assembly の directory に置かれた全 file の、内容 hash での記述</summary>
    /// <remarks>
    /// test assembly、依存する assembly、deps と runtimeconfig、設定と test data の file を含む。directory を名前では除かない
    /// 除くのは、engine 自身が成果物を書く directory だけである。その内容は test の実行が決めるものでなく、走行のたびに変わる
    /// </remarks>
    /// <param name="testAssemblyPath">test assembly の絶対 path</param>
    /// <param name="ownedDirectory">engine 自身が成果物を書く directory の絶対 path</param>
    /// <returns>path と内容 hash の列の記述。directory か file を読めなければ不在</returns>
    public static string? TestClosure(string testAssemblyPath, string ownedDirectory)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(testAssemblyPath));
        if (directory is null || IsUnder(ownedDirectory, testAssemblyPath) || FilesUnder(directory) is not { } files)
        {
            return null;
        }

        return Describe(files.Where(path => !IsUnder(ownedDirectory, path)).Order(StringComparer.Ordinal));
    }

    /// <summary>engine と compiler の assembly、worker の実体、runtime の版の記述</summary>
    /// <remarks>
    /// worker は host 実行体が worker assembly を開いて起動する。host は worker assembly の directory にある runtimeconfig と deps と依存 assembly から実行環境を決めるので、
    /// host 実行体と、その directory の全 file を worker の実体として含める。deployed output の持ち主である directory を使い、依存の一覧は組み直さない
    /// </remarks>
    /// <param name="worker">worker を起動する host 実行体と worker assembly の所在</param>
    /// <returns>runtime の版と、実体の path と内容 hash の列の記述。一つでも読めなければ不在</returns>
    public static string? DescribeToolchain(WorkerHost worker)
    {
        string[] assemblies =
        [
            typeof(InputClosure).Assembly.Location,
            typeof(Microsoft.CodeAnalysis.SyntaxTree).Assembly.Location,
            typeof(CSharpCommandLineParser).Assembly.Location,
        ];
        var workerDirectory = Path.GetDirectoryName(worker.DllPath);
        if (
            !Path.IsPathRooted(worker.ExecutablePath)
            || !Path.IsPathRooted(worker.DllPath)
            || workerDirectory is null
            || !File.Exists(worker.DllPath)
            || assemblies.Any(string.IsNullOrEmpty)
            || FilesUnder(workerDirectory) is not { } deployed
        )
        {
            return null;
        }

        var files = new[] { worker.ExecutablePath }
            .Concat(assemblies)
            .Concat(deployed)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return Describe(files) is { } described
            ? $"{RuntimeInformation.FrameworkDescription}\n{RuntimeInformation.RuntimeIdentifier}\n{RuntimeInformation.OSDescription}\n{described}"
            : null;
    }

    /// <summary>file の path と内容 hash の一行ずつの記述</summary>
    /// <param name="paths">記述する file の絶対 path</param>
    /// <returns>記述。一つでも読めなければ不在</returns>
    public static string? Describe(IEnumerable<string> paths)
    {
        var builder = new StringBuilder();
        foreach (var path in paths)
        {
            if (HashOf(path) is not { } hash)
            {
                return null;
            }

            builder.Append(path).Append(':').Append(hash).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>文字列の SHA-256 の 16 進表記</summary>
    /// <param name="text">hash する文字列</param>
    /// <returns>16 進の hash</returns>
    public static string Sha256Of(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>directory の下の全 file の絶対 path</summary>
    /// <param name="directory">列挙する directory の絶対 path</param>
    /// <returns>順不同の絶対 path の列。directory が無いか、列挙を読めなければ不在</returns>
    private static string[]? FilesUnder(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>csc の引数に指定された resource の file の path</summary>
    private static IEnumerable<string> ResourcePaths(IReadOnlyList<string> rawArguments)
    {
        foreach (var argument in rawArguments)
        {
            var separator = argument.IndexOf(':', StringComparison.Ordinal);
            if (argument.Length < 2 || argument[0] is not ('/' or '-') || separator < 0)
            {
                continue;
            }

            if (argument[1..separator] is "resource" or "res" or "linkresource" or "linkres")
            {
                yield return argument[(separator + 1)..].Split(',')[0].Trim('"');
            }
        }
    }

    /// <summary>path が directory の下にあるか。directory 自身や、名前の似た別の directory は含めない</summary>
    private static bool IsUnder(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>file 内容の SHA-256。読めなければ不在</summary>
    private static string? HashOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
