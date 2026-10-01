using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis.CSharp;

using TUnit.Core;

namespace Mutation.Tests;

/// <summary>schemata 織り込み後の assembly の挙動の検査</summary>
public sealed class SchemataRoundtripFacts
{
    private const string Source = """
        namespace Sample;

        public static class Math2
        {
            public static int Add(int a, int b) => a + b;

            public static bool Below(int a, int limit) => a < limit;
        }
        """;

    /// <summary>不活性状態での元の挙動の維持</summary>
    [Test]
    public async Task Inactive_schemata_preserve_original_behavior()
    {
        var (assembly, _) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        await Assert.That(Invoke<int>(math, "Add", 2, 3)).IsEqualTo(5);
        await Assert.That(Invoke<bool>(math, "Below", 1, 2)).IsTrue();
    }

    /// <summary>変異ごとの排他的な活性化と挙動の変化</summary>
    [Test]
    public async Task Each_mutant_changes_behavior_exclusively()
    {
        var (assembly, mutantCount) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        var control = new SwitchControl(assembly);
        var changed = 0;
        for (var id = 0; id < mutantCount; id++)
        {
            control.Activate(id);
            var addChanged = Invoke<int>(math, "Add", 2, 3) != 5;
            var belowChanged = !Invoke<bool>(math, "Below", 1, 2);
            var boundaryChanged = Invoke<bool>(math, "Below", 2, 2);
            if (addChanged || belowChanged || boundaryChanged)
            {
                changed++;
            }
        }

        control.Activate(-1);
        await Assert.That(mutantCount).IsEqualTo(3);
        await Assert.That(changed).IsEqualTo(3);
        await Assert.That(Invoke<int>(math, "Add", 2, 3)).IsEqualTo(5);
    }

    /// <summary>被覆収集モードでの probe の記録</summary>
    [Test]
    public async Task Coverage_mode_records_executed_probes()
    {
        var (assembly, _) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        var control = new SwitchControl(assembly);
        control.StartCoverage();
        Invoke<int>(math, "Add", 2, 3);
        var hits = control.DrainHits();
        control.Activate(-1);
        await Assert.That(hits.Length).IsEqualTo(1);
        await Assert.That(control.DrainHits().Length).IsEqualTo(0);
    }

    /// <summary>入れ子の論理式で子孫の変異を重複させない規約</summary>
    [Test]
    public async Task Nested_boolean_mutants_do_not_duplicate_descendant_schemata()
    {
        var source = "public static class Predicate { public static bool Match(int value) => "
            + string.Join(" || ", Enumerable.Range(0, 12).Select(index => $"value == {index}"))
            + "; }";
        var (tree, model) = Snippet.Parse(source);
        var root = await tree.GetRootAsync();
        var candidates = CandidateCollector.Collect(
            root, model, CandidateCollector.DefaultOperators, MutationPolicy.Everything);
        var numbered = candidates.Select((candidate, id) => (id, candidate)).ToArray();

        var rewritten = SchemataRewriter.Rewrite(root, numbered, new HashSet<int>());

        var annotatedIds = rewritten.DescendantNodesAndSelf()
            .SelectMany(node => node.GetAnnotations(SchemataRewriter.AnnotationKind))
            .Select(annotation => annotation.Data!)
            .ToArray();
        await Assert.That(annotatedIds).IsEquivalentTo(
            numbered.Select(pair => pair.id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var (assembly, count) = Snippet.EmitWithSchemata(source);
        await Assert.That(count).IsEqualTo(candidates.Count);
        var match = assembly.GetType("Predicate")!.GetMethod("Match")!;
        await Assert.That((bool)match.Invoke(null, [11])!).IsTrue();
    }

    /// <summary>診断用ソースの元の trivia を含む正確な保存</summary>
    [Test]
    public async Task Failed_source_dump_preserves_rewritten_text()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "mutation-failed-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string source = "class C {\n    int Value = 1; // diagnostic location\n}\n";
            var tree = CSharpSyntaxTree.ParseText(source, path: "C.cs");

            SchemataEmission.DumpSources([tree], directory);

            await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "C.cs")))
                .IsEqualTo(source);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static T Invoke<T>(Type type, string method, params object[] arguments) =>
        (T)type.GetMethod(method)!.Invoke(null, arguments)!;

    private sealed class SwitchControl
    {
        private readonly Type type;

        public SwitchControl(System.Reflection.Assembly assembly)
        {
            type = assembly.GetType("MutationInjected.MutantSwitch")!;
        }

        public void Activate(int id) => type.GetMethod("Activate")!.Invoke(null, [id, long.MaxValue]);

        public void StartCoverage() => type.GetMethod("StartCoverage")!.Invoke(null, []);

        public int[] DrainHits() => (int[])type.GetMethod("DrainHits")!.Invoke(null, [])!;
    }
}
