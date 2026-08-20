
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using Mutation.Mutating.Infrastructure.Roslyn;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>三項演算子を置けない文脈での候補の形と compile 可否の検査</summary>
public sealed class StatementContextFacts
{
    /// <summary>for の incrementor 変異が for 文全体の置換になり compile できること</summary>
    [Test]
    public async Task For_incrementor_mutations_become_statement_swaps_on_the_loop()
    {
        var source = """
            namespace Sample;
            public static class Loops
            {
                public static int Count(int n) { var c = 0; for (var i = 0; i < n; i++) { c += 1; } return c; }
            }
            """;
        var candidates = Snippet.Collect(source);
        var stepSwap = candidates.Single(c => c.ReplacementText == "i--");
        await Assert.That(stepSwap).IsTypeOf<MutationCandidate.StatementSwap>();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var loops = assembly.GetType("Sample.Loops")!;
        await Assert.That((int)loops.GetMethod("Count")!.Invoke(null, [3])!).IsEqualTo(3);
    }

    /// <summary>式文の変異が文の置換になること</summary>
    [Test]
    public async Task Expression_statement_mutations_become_statement_swaps()
    {
        var candidates = Snippet.Collect("class C { int n; void M() { n += 2; } }");
        var swap = candidates.Single(c => c.OperatorName == "AssignmentMutator");
        await Assert.That(swap).IsTypeOf<MutationCandidate.StatementSwap>();
    }

    /// <summary>void の式本体 member の変異が本体を block に開く候補になり compile できること</summary>
    [Test]
    public async Task Void_arrow_bodies_become_arrow_swaps_and_still_compile()
    {
        var source = """
            namespace Sample;
            public class Counter
            {
                public int Value;
                public void Bump() => Value += 2;
                public int Read() => Value + 0;
            }
            """;
        var candidates = Snippet.Collect(source);
        var bump = candidates.Single(c => c.ReplacementText == "Value -= 2");
        await Assert.That(bump).IsTypeOf<MutationCandidate.VoidArrowSwap>();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var counter = Activator.CreateInstance(assembly.GetType("Sample.Counter")!)!;
        counter.GetType().GetMethod("Bump")!.Invoke(counter, []);
        await Assert.That((int)counter.GetType().GetField("Value")!.GetValue(counter)!).IsEqualTo(2);
    }

    /// <summary>void delegate へ変換される lambda の式本体の変異が block 化され compile できること</summary>
    [Test]
    public async Task Void_lambda_bodies_are_opened_into_blocks()
    {
        var source = """
            namespace Sample;
            public static class Runner
            {
                public static int Run() { var total = 0; System.Action step = () => total += 5; step(); return total; }
            }
            """;
        var candidates = Snippet.Collect(source);
        var lambdaSwap = candidates.Single(c => c.ReplacementText == "total -= 5");
        await Assert.That(lambdaSwap).IsTypeOf<MutationCandidate.VoidArrowSwap>();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        await Assert.That((int)assembly.GetType("Sample.Runner")!.GetMethod("Run")!.Invoke(null, [])!).IsEqualTo(5);
    }
}
