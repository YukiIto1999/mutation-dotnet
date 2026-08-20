using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using TUnit.Core;

namespace Mutation.Tests;

/// <summary>static 初期化の実行時追跡の計装の検査</summary>
public sealed class StaticContextTrackingFacts
{
    /// <summary>static field 初期化子と static constructor への目印付け</summary>
    [Test]
    public async Task Static_initializers_and_constructors_are_annotated_on_load()
    {
        var (tree, _) = Snippet.Parse("class C { static int A = 1; int b = 2; static C() { } const int K = 3; }");
        var annotated = StaticContextRewriter.Annotate(await tree.GetRootAsync());
        var keys = annotated.GetAnnotatedNodes(StaticContextRewriter.AnnotationKind).ToArray();
        await Assert.That(keys.Length).IsEqualTo(2);
        await Assert.That(keys.OfType<EqualsValueClauseSyntax>().Count()).IsEqualTo(1);
        await Assert.That(keys.OfType<ConstructorDeclarationSyntax>().Count()).IsEqualTo(1);
    }

    /// <summary>包める初期化子だけへの型名の割り当て</summary>
    [Test]
    public async Task Only_wrappable_initializers_get_a_type()
    {
        var source = """
            class C
            {
                static int A = Compute();
                static int[] B = { 1, 2 };
                static string? N = null;
                static System.Collections.Generic.List<int> L = new();
                static int Compute() => 1;
            }
            """;
        var (tree, model) = Snippet.ParseAnnotated(source);
        var index = StaticInitializerIndex.Build(await tree.GetRootAsync(), model);
        var types = index.Values.Order().ToArray();
        await Assert.That(types.SequenceEqual(["global::System.Collections.Generic.List<int>", "int"])).IsTrue();
    }

    /// <summary>static 初期化中の probe は static hit にだけ記録され、通常の hit から分かれること</summary>
    [Test]
    public async Task Probes_hit_during_static_initialization_are_reported_as_static()
    {
        var source = """
            namespace Sample;

            public static class Seeded
            {
                public static readonly int Seed = Compute(4);

                public static int Plain(int a) => a + 1;

                private static int Compute(int basis) => basis * 2;
            }
            """;
        var (assembly, mutantCount) = Snippet.EmitWithSchemata(source, trackStatic: true);
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        control.GetMethod("StartCoverage")!.Invoke(null, []);
        var seeded = assembly.GetType("Sample.Seeded")!;
        _ = seeded.GetField("Seed")!.GetValue(null);
        seeded.GetMethod("Plain")!.Invoke(null, [1]);
        var hits = (int[])control.GetMethod("DrainHits")!.Invoke(null, [])!;
        var staticHits = (int[])control.GetMethod("DrainStaticHits")!.Invoke(null, [])!;
        control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        await Assert.That(mutantCount).IsEqualTo(2);
        await Assert.That(hits.Length).IsEqualTo(1);
        await Assert.That(staticHits.Length).IsEqualTo(1);
    }
}
