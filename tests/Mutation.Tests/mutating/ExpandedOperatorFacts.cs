
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>拡充した演算子(string / Math / 初期化子)の生成条件の検査</summary>
public sealed class ExpandedOperatorFacts
{
    /// <summary>string method の対の入れ替えと、他型の同名 method の非対象</summary>
    [Test]
    public async Task String_method_pairs_swap_only_on_System_String()
    {
        var source = """
            class C
            {
                bool M(string s) => s.StartsWith("a");

                bool N(CustomPath p) => p.StartsWith("a");
            }

            class CustomPath
            {
                public bool StartsWith(string prefix) => prefix.Length > 0;
            }
            """;
        var swaps = Snippet.Collect(source).Where(c => c.OperatorName == "StringMethodMutator").ToArray();
        await Assert.That(swaps.Length).IsEqualTo(1);
        await Assert.That(swaps[0].ReplacementText.Contains("s.EndsWith")).IsTrue();
    }

    /// <summary>Math.Min と Max の入れ替えが挙動を変えること</summary>
    [Test]
    public async Task Math_min_max_swap_changes_behavior()
    {
        var source = """
            namespace Sample;
            public static class Calc
            {
                public static int Smaller(int a, int b) => System.Math.Min(a, b);
            }
            """;
        var (assembly, mutantCount) = Snippet.EmitWithSchemata(source);
        await Assert.That(mutantCount).IsEqualTo(1);
        var calc = assembly.GetType("Sample.Calc")!;
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        control.GetMethod("Activate")!.Invoke(null, [0, long.MaxValue]);
        await Assert.That((int)calc.GetMethod("Smaller")!.Invoke(null, [2, 5])!).IsEqualTo(5);
        control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        await Assert.That((int)calc.GetMethod("Smaller")!.Invoke(null, [2, 5])!).IsEqualTo(2);
    }

    /// <summary>初期化子の空化の対象と非対象</summary>
    [Test]
    public async Task Initializers_are_emptied_only_when_expressible()
    {
        var source = """
            class C
            {
                int[] Explicit() => new int[] { 1, 2 };

                System.Collections.Generic.List<int> Collection() => new System.Collections.Generic.List<int> { 1, 2 };

                int[] Expression() => [1, 2];

                int[] Implicit() => new[] { 1, 2 };
            }
            """;
        var inits = Snippet.Collect(source).Where(c => c.OperatorName == "InitializerMutator").ToArray();
        await Assert.That(inits.Length).IsEqualTo(3);
        await Assert.That(inits.Any(c => c.ReplacementText.Contains("new[]"))).IsFalse();
    }

    /// <summary>`?.` の連鎖の内側は包まず、連鎖全体を含む式は compile できること</summary>
    [Test]
    public async Task Conditional_access_chains_are_not_wrapped_inside()
    {
        var source = """
            namespace Sample;
            public class Holder
            {
                public string? Name { get; set; }

                public bool Check(Holder? h) => h?.Name.StartsWith("x") ?? false;
            }
            """;
        var candidates = Snippet.Collect(source);
        await Assert.That(candidates.Any(c => c.OperatorName == "StringMethodMutator")).IsFalse();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        await Assert.That(assembly.GetType("Sample.Holder")).IsNotNull();
    }

    /// <summary>空にした collection 初期化子が compile できること</summary>
    [Test]
    public async Task Emptied_collection_initializer_still_compiles()
    {
        var source = """
            namespace Sample;
            public static class Holder
            {
                public static int Count() { var items = new System.Collections.Generic.List<int> { 1, 2, 3 }; return items.Count; }
            }
            """;
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var holder = assembly.GetType("Sample.Holder")!;
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        var initializerId = 0;
        await Assert.That((int)holder.GetMethod("Count")!.Invoke(null, [])!).IsEqualTo(3);
        control.GetMethod("Activate")!.Invoke(null, [initializerId, long.MaxValue]);
        await Assert.That((int)holder.GetMethod("Count")!.Invoke(null, [])!).IsEqualTo(0);
        control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
    }
}
