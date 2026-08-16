using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TypeModeling.Domain;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>変異コンパイルの emit と、失敗時の診断材料の書き出し</summary>
public static class SchemataEmission
{
    /// <summary>コンパイルの emit と assembly の書き出し</summary>
    /// <param name="compilation">織り込み済みのコンパイル</param>
    /// <param name="target">変異対象のコンパイル入力一式</param>
    /// <param name="outputDirectory">変異 assembly を書き出す directory</param>
    /// <returns>成功なら書き出した path、失敗なら error 診断の列</returns>
    public static Result<string, IReadOnlyList<Diagnostic>> Emit(
        CSharpCompilation compilation,
        LoadedTarget target,
        string outputDirectory
    )
    {
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, Path.GetFileName(target.OriginalOutputPath));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, manifestResources: target.Arguments.ManifestResources);
        if (!result.Success)
        {
            var errors = result
                .Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            return new Result<string, IReadOnlyList<Diagnostic>>.Failed(errors);
        }

        File.WriteAllBytes(outputPath, stream.ToArray());
        return new Result<string, IReadOnlyList<Diagnostic>>.Succeeded(outputPath);
    }

    /// <summary>書換後のソース一式の診断用の書き出し</summary>
    /// <param name="trees">書換後の構文木の列</param>
    /// <param name="directory">書き出し先 directory</param>
    /// <returns>書き出した directory</returns>
    public static string DumpSources(IEnumerable<SyntaxTree> trees, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var tree in trees)
        {
            var name = Path.GetFileName(tree.FilePath);
            var stem = Path.GetFileNameWithoutExtension(name);
            var suffix = 0;
            var target = Path.Combine(directory, name);
            while (File.Exists(target))
            {
                suffix++;
                target = Path.Combine(directory, $"{stem}.{suffix}.cs");
            }

            File.WriteAllText(target, tree.GetRoot().NormalizeWhitespace().ToFullString());
        }

        return directory;
    }

    /// <summary>error 診断の先頭の、人が読む形への変換</summary>
    /// <param name="errors">emit が返した error 診断の列</param>
    /// <returns>先頭 20 件を並べた複数行の説明</returns>
    public static string Render(IReadOnlyList<Diagnostic> errors) =>
        string.Join("\n", errors.Take(20).Select(e => e.ToString()));
}
