using TUnit.Core;

namespace Mutation.Tests;

/// <summary>関係 pattern 変異の生成条件の検査</summary>
public sealed class RelationalPatternMutatorFacts
{
    /// <summary>switch 式の関係 pattern に対する境界交換と反転交換の生成</summary>
    [Test]
    public async Task Switch_expression_relational_patterns_yield_swaps()
    {
        var source = "class C { bool M(int value) => value switch { 0 => false, > 10 => true, _ => false }; }";
        var texts = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "RelationalPatternMutator")
            .Select(candidate => candidate.ReplacementText)
            .Order()
            .ToArray();

        await Assert.That(texts.SequenceEqual(["< 10", ">= 10"])).IsTrue();
    }
    /// <summary>先行定数 arm と後続 discard arm を持つ関係 pattern の交換生成</summary>
    [Test]
    public async Task Constant_then_relational_then_discard_switch_yields_swaps()
    {
        var source =
            "public static class C { private const int Ceiling = 10; public static bool M(byte[] raw) => raw.Length switch { 0 => false, > Ceiling => true, _ => false }; }";
        var texts = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "RelationalPatternMutator")
            .Select(candidate => candidate.ReplacementText)
            .Order()
            .ToArray();

        await Assert.That(texts.SequenceEqual(["< Ceiling", ">= Ceiling"])).IsTrue();
    }
    /// <summary>関係 pattern 交換を含む schemata の実行</summary>
    [Test]
    public async Task Relational_pattern_swaps_compile_and_change_matching()
    {
        var source = "public static class C { public static bool M(int value) => value switch { > 10 => true, _ => false }; }";
        var indexed = Snippet.Collect(source)
            .Select((candidate, id) => (candidate, id))
            .Where(pair => pair.candidate.OperatorName == "RelationalPatternMutator")
            .ToArray();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        var method = assembly.GetType("C")!.GetMethod("M")!;

        foreach (var (candidate, id) in indexed)
        {
            control.GetMethod("Activate")!.Invoke(null, [id, long.MaxValue]);
            var input = candidate.ReplacementText == ">= 10" ? 10 : 11;
            var result = (bool)method.Invoke(null, [input])!;
            await Assert.That(result).IsEqualTo(candidate.ReplacementText == ">= 10");
            control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        }
    }
    /// <summary>switch 文の case pattern に対する交換の実行</summary>
    [Test]
    public async Task Switch_statement_case_patterns_compile_and_change_matching()
    {
        var source = "public static class C { public static bool M(int value) { switch (value) { case > 10: return true; default: return false; } } }";
        var indexed = Snippet.Collect(source)
            .Select((candidate, id) => (candidate, id))
            .Where(pair => pair.candidate.OperatorName == "RelationalPatternMutator")
            .ToArray();
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        var method = assembly.GetType("C")!.GetMethod("M")!;

        foreach (var (candidate, id) in indexed)
        {
            control.GetMethod("Activate")!.Invoke(null, [id, long.MaxValue]);
            var input = candidate.ReplacementText == ">= 10" ? 10 : 11;
            var result = (bool)method.Invoke(null, [input])!;
            await Assert.That(result).IsEqualTo(candidate.ReplacementText == ">= 10");
            control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        }
    }

    /// <summary>網羅性を保つ関係 pattern の交換生成と破綻交換の抑止</summary>
    [Test]
    public async Task Exhaustiveness_preserving_swaps_are_generated()
    {
        var source = "class C { int M(int value) => value switch { < 0 => -1, >= 0 => 1 }; }";
        var texts = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "RelationalPatternMutator")
            .Select(candidate => candidate.ReplacementText)
            .ToArray();

        await Assert.That(texts.Contains("> 0")).IsFalse();
        await Assert.That(texts.Contains(">= 0")).IsFalse();
        await Assert.That(texts.Contains("<= 0")).IsTrue();
    }

    /// <summary>後続 arm を覆う関係 pattern の交換抑止</summary>
    [Test]
    public async Task Subsuming_switch_arm_swaps_are_not_generated()
    {
        var source = "class C { int M(int value) => value switch { > 0 => 1, >= 0 => 2, _ => 3 }; }";
        var texts = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "RelationalPatternMutator")
            .Select(candidate => candidate.ReplacementText)
            .ToArray();

        await Assert.That(texts.Contains(">= 0")).IsFalse();
        await Assert.That(texts.Contains("> 0")).IsFalse();
    }

    /// <summary>property pattern の関係 pattern に対する交換の生成</summary>
    [Test]
    public async Task Property_patterns_yield_relational_swaps()
    {
        var source = "public static class C { public static bool M(int[] values) => values is { Length: > 10 }; }";
        var indexed = Snippet.Collect(source)
            .Select((candidate, id) => (candidate, id))
            .Where(pair => pair.candidate.OperatorName == "RelationalPatternMutator")
            .ToArray();

        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        var method = assembly.GetType("C")!.GetMethod("M")!;
        foreach (var (candidate, id) in indexed)
        {
            control.GetMethod("Activate")!.Invoke(null, [id, long.MaxValue]);
            var input = candidate.ReplacementText == ">= 10" ? new int[10] : new int[11];
            var result = (bool)method.Invoke(null, [input])!;
            await Assert.That(result).IsEqualTo(candidate.ReplacementText == ">= 10");
            control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        }
    }
}
