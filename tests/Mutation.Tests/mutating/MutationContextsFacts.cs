using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using TUnit.Core;

namespace Mutation.Tests;

/// <summary>static 初期化文脈の判定の検査</summary>
public sealed class MutationContextsFacts
{
    /// <summary>static field 初期化子の static 判定</summary>
    [Test]
    public async Task Static_field_initializers_are_static_context()
    {
        await Assert.That(IsStatic("class C { static int N = 10 - 3; }", "10 - 3")).IsTrue();
    }

    /// <summary>static constructor 本体の static 判定</summary>
    [Test]
    public async Task Static_constructor_bodies_are_static_context()
    {
        await Assert.That(IsStatic("class C { static int n; static C() { n = 1 + 2; } }", "1 + 2")).IsTrue();
    }

    /// <summary>instance method 本体の非 static 判定</summary>
    [Test]
    public async Task Instance_method_bodies_are_not_static_context()
    {
        await Assert.That(IsStatic("class C { int M() => 1 + 2; }", "1 + 2")).IsFalse();
    }

    /// <summary>static 初期化子内の lambda 本体の非 static 判定</summary>
    [Test]
    public async Task Lambda_bodies_inside_static_initializers_are_not_static_context()
    {
        var source = "class C { static System.Func<int, int> F = x => x + 1; }";
        await Assert.That(IsStatic(source, "x + 1")).IsFalse();
    }

    private static bool IsStatic(string source, string expressionText)
    {
        var (tree, _) = Snippet.Parse(source);
        var node = tree.GetRoot()
            .DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .First(n => n.ToString() == expressionText);
        return MutationContexts.IsStaticContext(node);
    }
}
