
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>二項演算子変異の生成条件の検査</summary>
public sealed class BinaryOperatorMutatorFacts
{
    /// <summary>整数の加算に対する減算への交換の生成</summary>
    [Test]
    public async Task Integer_addition_yields_a_subtraction_swap()
    {
        var candidates = Snippet.Collect("class C { int M(int a, int b) => a + b; }");
        var swaps = candidates.Where(c => c.OperatorName == "BinaryOperatorMutator").ToArray();
        await Assert.That(swaps.Length).IsEqualTo(1);
        await Assert.That(swaps[0].ReplacementText).IsEqualTo("a - b");
    }

    /// <summary>文字列連結に対する算術交換の抑止</summary>
    [Test]
    public async Task String_concatenation_yields_no_arithmetic_swap()
    {
        var candidates = Snippet.Collect("class C { string M(string a, string b) => a + b; }");
        await Assert.That(candidates.Count(c => c.OperatorName == "BinaryOperatorMutator")).IsEqualTo(0);
    }

    /// <summary>比較演算子に対する境界交換と反転交換の生成</summary>
    [Test]
    public async Task Relational_operator_yields_boundary_and_negation_swaps()
    {
        var candidates = Snippet.Collect("class C { bool M(int a, int b) => a < b; }");
        var texts = candidates
            .Where(c => c.OperatorName == "BinaryOperatorMutator")
            .Select(c => c.ReplacementText)
            .Order()
            .ToArray();
        await Assert.That(texts.SequenceEqual(["a <= b", "a >= b"])).IsTrue();
    }

    /// <summary>相方の演算子が定義されない型に対する交換の抑止</summary>
    [Test]
    public async Task User_defined_subtraction_without_addition_yields_no_swap()
    {
        var source = """
            using System;
            class C { TimeSpan M(DateTime a, DateTime b) => a - b; }
            """;
        var candidates = Snippet.Collect(source);
        await Assert.That(candidates.Count(c => c.OperatorName == "BinaryOperatorMutator")).IsEqualTo(0);
    }

    /// <summary>null 許容整数の比較に対する交換の生成</summary>
    [Test]
    public async Task Lifted_nullable_comparison_still_yields_swaps()
    {
        var candidates = Snippet.Collect("class C { bool M(int? a, int? b) => a == b; }");
        await Assert.That(candidates.Count(c => c.OperatorName == "BinaryOperatorMutator")).IsEqualTo(1);
    }
}
