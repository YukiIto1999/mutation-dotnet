
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>member 本体の早期脱出変異の形の検査</summary>
public sealed class MethodBodyNullifierFacts
{
    /// <summary>void method に対する素の return の生成</summary>
    [Test]
    public async Task Void_bodies_get_a_bare_return_guard()
    {
        var guard = SoleGuard("class C { int n; void M() { n = 1; } }");
        await Assert.That(guard).IsEqualTo("return;");
    }

    /// <summary>値を返す method に対する return default の生成</summary>
    [Test]
    public async Task Value_returning_bodies_get_a_default_return_guard()
    {
        var guard = SoleGuard("class C { int M() { return 1 + 2; } }");
        await Assert.That(guard).IsEqualTo("return default;");
    }

    /// <summary>iterator に対する yield break の生成</summary>
    [Test]
    public async Task Iterator_bodies_get_a_yield_break_guard()
    {
        var source = "class C { System.Collections.Generic.IEnumerable<int> M() { yield return 1; } }";
        var guard = SoleGuard(source);
        await Assert.That(guard).IsEqualTo("yield break;");
    }

    /// <summary>async Task に対する素の return の生成</summary>
    [Test]
    public async Task Async_task_bodies_get_a_bare_return_guard()
    {
        var source = "class C { async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Yield(); } }";
        var guard = SoleGuard(source);
        await Assert.That(guard).IsEqualTo("return;");
    }

    /// <summary>out 引数を持つ method に対する生成の抑止</summary>
    [Test]
    public async Task Bodies_with_out_parameters_are_not_nullified()
    {
        var candidates = Snippet.Collect("class C { void M(out int value) { value = 1; } }");
        await Assert.That(candidates.Count(c => c.OperatorName == "MethodBodyNullifier")).IsEqualTo(0);
    }

    /// <summary>式本体 member に対する生成の抑止</summary>
    [Test]
    public async Task Expression_bodied_members_are_not_nullified()
    {
        var candidates = Snippet.Collect("class C { int M() => 1; }");
        await Assert.That(candidates.Count(c => c.OperatorName == "MethodBodyNullifier")).IsEqualTo(0);
    }

    private static string SoleGuard(string source)
    {
        var guards = Snippet
            .Collect(source)
            .Where(c => c.OperatorName == "MethodBodyNullifier")
            .Select(c => c.ReplacementText)
            .ToArray();
        return guards is [var only] ? only : $"候補が {guards.Length} 件";
    }
}
