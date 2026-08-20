using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure.Roslyn;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;


namespace Mutation.Tests;

/// <summary>テスト用の断片ソースの解析と変異コンパイル</summary>
public static class Snippet
{
    /// <summary>テスト process が読める全 assembly のメタデータ参照</summary>
    public static IReadOnlyList<MetadataReference> References { get; } = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Where(path => path.EndsWith(".dll", StringComparison.Ordinal))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    /// <summary>断片ソースの構文木と意味解析の組み立て</summary>
    /// <param name="source">解析する C# ソース</param>
    /// <returns>構文木とその意味解析</returns>
    public static (SyntaxTree Tree, SemanticModel Model) Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = Compile([tree]);
        return (tree, compilation.GetSemanticModel(tree));
    }

    /// <summary>static 初期化へ目印を付けた断片ソースの構文木と意味解析の組み立て</summary>
    /// <param name="source">解析する C# ソース</param>
    /// <returns>目印付きの構文木とその意味解析</returns>
    public static (SyntaxTree Tree, SemanticModel Model) ParseAnnotated(string source)
    {
        var parsed = CSharpSyntaxTree.ParseText(source);
        var tree = parsed.WithRootAndOptions(StaticContextRewriter.Annotate(parsed.GetRoot()), parsed.Options);
        return (tree, Compile([tree]).GetSemanticModel(tree));
    }

    /// <summary>断片ソースからの変異候補の収集</summary>
    /// <param name="source">解析する C# ソース</param>
    /// <param name="policy">選別方針。無指定なら全て対象</param>
    /// <returns>既定演算子で許容された候補の列</returns>
    public static IReadOnlyList<MutationCandidate> Collect(string source, MutationPolicy? policy = null)
    {
        var (tree, model) = Parse(source);
        return CandidateCollector.Collect(
            tree.GetRoot(),
            model,
            CandidateCollector.DefaultOperators,
            policy ?? MutationPolicy.Everything
        );
    }

    /// <summary>断片ソースを schemata 込みでコンパイルして読み込む</summary>
    /// <param name="source">変異対象の C# ソース</param>
    /// <param name="trackStatic">static 初期化の実行時追跡も計装するか</param>
    /// <returns>読み込んだ assembly と変異の総数</returns>
    public static (System.Reflection.Assembly Assembly, int MutantCount) EmitWithSchemata(
        string source,
        bool trackStatic = false
    )
    {
        var (tree, model) = trackStatic ? ParseAnnotated(source) : Parse(source);
        var candidates = CandidateCollector.Collect(
            tree.GetRoot(),
            model,
            CandidateCollector.DefaultOperators,
            MutationPolicy.Everything
        );
        var numbered = candidates.Select((candidate, id) => (id, candidate)).ToArray();
        var rewritten = SchemataRewriter.Rewrite(tree.GetRoot(), numbered, new HashSet<int>());
        if (trackStatic)
        {
            var types = StaticInitializerIndex.Build(tree.GetRoot(), model);
            rewritten = StaticContextRewriter.Rewrite(rewritten, types, new HashSet<string>());
        }

        var mutatedTree = tree.WithRootAndOptions(rewritten, tree.Options);
        var switchTree = CSharpSyntaxTree.ParseText(MutantSwitchSource.Render(candidates.Count));
        var compilation = Compile([mutatedTree, switchTree]);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success)
        {
            var errors = string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"schemata コンパイルが失敗した:\n{errors}");
        }

        return (System.Reflection.Assembly.Load(stream.ToArray()), candidates.Count);
    }

    private static CSharpCompilation Compile(IReadOnlyList<SyntaxTree> trees) =>
        CSharpCompilation.Create(
            $"Snippet{Guid.NewGuid():N}",
            trees,
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Suppress
            )
        );
}
