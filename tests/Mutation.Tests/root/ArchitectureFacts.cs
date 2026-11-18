using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>単一の core の層の依存方向と配置の検査</summary>
public sealed class ArchitectureFacts
{
    /// <summary>全境界の検査で共有する source と参照 assembly の意味モデル</summary>
    private static readonly Lazy<CSharpCompilation> Compilation = new(CreateCompilation);

    /// <summary>型と namespace の参照が層の境界を逆流しないこと</summary>
    [Test]
    [Arguments("src/Mutation/domain", "Mutation.Application,Mutation.Infrastructure,Mutation.Composition,Mutation.Protocol")]
    [Arguments("src/Mutation/application", "Mutation.Infrastructure,Mutation.Composition,Mutation.Protocol")]
    [Arguments("src/Mutation/infrastructure", "Mutation.Composition")]
    [Arguments("src/Mutation/infrastructure/workers/protocol", "Mutation.Domain,Mutation.Application,Mutation.Infrastructure,Mutation.Composition")]
    [Arguments("src/Mutation.Cli", "Mutation.Infrastructure")]
    public async Task Dependencies_respect_the_core_boundary(string scope, string forbiddenNamespaces)
    {
        var forbidden = forbiddenNamespaces.Split(',');
        var root = RootedPath(scope) + Path.DirectorySeparatorChar;
        var violations = new List<string>();
        foreach (var tree in Compilation.Value.SyntaxTrees.Where(tree => tree.FilePath.StartsWith(root, StringComparison.Ordinal)))
        {
            var model = Compilation.Value.GetSemanticModel(tree);
            var syntaxRoot = await tree.GetRootAsync();
            foreach (var node in syntaxRoot.DescendantNodes().OfType<NameSyntax>())
            {
                var symbol = model.GetSymbolInfo(node).Symbol;
                var ns = (symbol as INamespaceSymbol ?? symbol?.ContainingNamespace)?.ToDisplayString();
                if (ns is not null && forbidden.Any(prefix => ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal)))
                {
                    violations.Add($"{tree.FilePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {ns}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    /// <summary>core が業務モデルを分割した shared kernel を持たず、一つの文脈の層だけを持つこと</summary>
    [Test]
    public async Task Core_contains_one_context()
    {
        string[] allowed = ["domain", "application", "infrastructure", "composition", "bin", "obj"];
        var unexpected = Directory.EnumerateDirectories(RootedPath("src/Mutation"))
            .Where(path => !allowed.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .ToArray();
        await Assert.That(unexpected).IsEmpty();
    }

    /// <summary>domain と application の直下が規範の単位フォルダだけで構成されること</summary>
    [Test]
    public async Task Layers_hold_only_standard_units()
    {
        var units = new Dictionary<string, string[]>
        {
            ["domain"] = ["aggregates", "values", "events", "services"],
            ["application"] = ["usecases", "workflows", "ports"],
        };
        var offending = units.SelectMany(layer =>
        {
            var path = RootedPath(Path.Combine("src", "Mutation", layer.Key));
            var looseFiles = Directory.EnumerateFiles(path, "*.cs", SearchOption.TopDirectoryOnly);
            var strangeFolders = Directory.EnumerateDirectories(path)
                .Where(directory => !layer.Value.Contains(Path.GetFileName(directory), StringComparer.Ordinal));
            return looseFiles.Concat(strangeFolders);
        }).ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>各 project の root 直下のファイルが entry point だけであること</summary>
    [Test]
    public async Task Project_roots_hold_only_entry_points()
    {
        string[] roots = ["src/Mutation", "src/Mutation.Cli", "src/Mutation.Worker", "tests/Mutation.Tests"];
        var offending = roots
            .SelectMany(root => Directory.EnumerateFiles(RootedPath(root), "*.cs", SearchOption.TopDirectoryOnly))
            .Where(file => Path.GetFileName(file) != "Program.cs")
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>実際の型参照を解決するための、製品 source と実行中の参照 assembly による compilation</summary>
    private static CSharpCompilation CreateCompilation()
    {
        var trees = SourcesUnder("src/Mutation").Concat(SourcesUnder("src/Mutation.Cli"))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = trusted.Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create("ArchitectureBoundary", trees, references);
    }

    /// <summary>repo root からの相対 path の絶対化</summary>
    private static string RootedPath(string relative)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Mutation.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repo root が見つからない");
        }

        return Path.Combine(root, relative);
    }

    /// <summary>生成物を除く製品 source の列挙</summary>
    private static IEnumerable<string> SourcesUnder(string relative) =>
        Directory.EnumerateFiles(RootedPath(relative), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
