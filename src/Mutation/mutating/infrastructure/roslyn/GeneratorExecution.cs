using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>対象 project の source generator の再実行</summary>
public static class GeneratorExecution
{
    /// <summary>csc 引数の analyzer 参照から読み込んだ generator による、生成コードのコンパイルへの追加</summary>
    /// <param name="compilation">手書きソースのみのコンパイル</param>
    /// <param name="arguments">analyzer 参照と追加 file を含む csc 引数</param>
    /// <param name="parseOptions">対象と同じ構文解析設定</param>
    /// <returns>生成コードを追加したコンパイル。generator がなければ元のまま</returns>
    public static CSharpCompilation Append(
        CSharpCompilation compilation,
        CSharpCommandLineArguments arguments,
        CSharpParseOptions parseOptions
    )
    {
        var context = new GeneratorLoadContext();
        try
        {
            var loader = new PathAnalyzerAssemblyLoader(context);
            var generators = arguments
                .AnalyzerReferences.Select(reference => LoadGenerators(reference.FilePath, loader))
                .SelectMany(g => g)
                .ToArray();
            if (generators.Length == 0)
            {
                return compilation;
            }

            var additionalTexts = arguments
                .AdditionalFiles.Select(file => (AdditionalText)new FileAdditionalText(file.Path))
                .ToArray();
            var driver = CSharpGeneratorDriver.Create(generators, additionalTexts, parseOptions);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
            return (CSharpCompilation)updated;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>一つの analyzer 参照からの generator の読込。読めなければ空</summary>
    private static System.Collections.Immutable.ImmutableArray<ISourceGenerator> LoadGenerators(
        string path,
        PathAnalyzerAssemblyLoader loader
    )
    {
        try
        {
            var reference = new AnalyzerFileReference(Path.GetFullPath(path), loader);
            return reference.GetGenerators(LanguageNames.CSharp);
        }
        catch (IOException)
        {
            return [];
        }
        catch (BadImageFormatException)
        {
            return [];
        }
    }

    /// <summary>一回の Append に閉じた generator 用の読込文脈</summary>
    private sealed class GeneratorLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        /// <summary>依存 assembly を探す directory の集合</summary>
        private readonly HashSet<string> probeDirectories = [];

        /// <summary>assembly の置き場の、依存解決の探索先への追加</summary>
        public void Remember(string assemblyPath)
        {
            var directory = Path.GetDirectoryName(assemblyPath);
            if (directory is not null)
            {
                lock (probeDirectories)
                {
                    probeDirectories.Add(directory);
                }
            }
        }

        /// <inheritdoc />
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Roslyn と framework はホストと型を共有しないと generator の契約型が一致しない
            if (assemblyName.Name is null
                || assemblyName.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || assemblyName.Name.StartsWith("System", StringComparison.Ordinal)
                || assemblyName.Name == "netstandard")
            {
                return null;
            }

            lock (probeDirectories)
            {
                foreach (var directory in probeDirectories)
                {
                    var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        return LoadFromAssemblyPath(candidate);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>generator 用の読込文脈へ委譲する loader</summary>
    private sealed class PathAnalyzerAssemblyLoader(GeneratorLoadContext context) : IAnalyzerAssemblyLoader
    {
        /// <inheritdoc />
        public void AddDependencyLocation(string fullPath) => context.Remember(fullPath);

        /// <inheritdoc />
        public Assembly LoadFromPath(string fullPath)
        {
            context.Remember(fullPath);
            return context.LoadFromAssemblyPath(fullPath);
        }
    }

    /// <summary>path をそのまま読む追加 file</summary>
    private sealed class FileAdditionalText(string path) : AdditionalText
    {
        /// <inheritdoc />
        public override string Path => path;

        /// <inheritdoc />
        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            using var stream = File.OpenRead(path);
            return SourceText.From(stream);
        }
    }
}
