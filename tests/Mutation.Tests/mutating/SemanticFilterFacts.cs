
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>compile error や束縛破壊を招く文脈の抑止の検査</summary>
public sealed class SemanticFilterFacts
{
    /// <summary>const 宣言の初期化子に対する変異の抑止</summary>
    [Test]
    public async Task Constant_field_initializers_yield_no_candidates()
    {
        var candidates = Snippet.Collect("class C { const int Limit = 10 + 3; const bool Flag = true; }");
        await Assert.That(candidates.Count).IsEqualTo(0);
    }

    /// <summary>attribute 引数に対する変異の抑止</summary>
    [Test]
    public async Task Attribute_arguments_yield_no_candidates()
    {
        var candidates = Snippet.Collect("""[System.Obsolete("old" + "er")] class C { }""");
        await Assert.That(candidates.Count).IsEqualTo(0);
    }

    /// <summary>pattern 内の定数に対する変異の抑止</summary>
    [Test]
    public async Task Patterns_yield_no_candidates()
    {
        var candidates = Snippet.Collect("class C { bool M(int a) => a is > 5 or < -3; }");
        await Assert.That(candidates.Count).IsEqualTo(0);
    }

    /// <summary>式木に変換される lambda に対する変異の抑止</summary>
    [Test]
    public async Task Expression_tree_lambdas_yield_no_candidates()
    {
        var source = """
            using System.Linq.Expressions;
            class C { Expression<System.Func<int, int>> M() => x => x + 1; }
            """;
        var candidates = Snippet.Collect(source);
        await Assert.That(candidates.Count).IsEqualTo(0);
    }

    /// <summary>pattern 変数を含む条件に対する定数化の抑止</summary>
    [Test]
    public async Task Conditions_declaring_pattern_variables_are_not_replaced_by_literals()
    {
        var source = "class C { int M(object o) { if (o is string s) { return s.Length; } return 0; } }";
        var candidates = Snippet.Collect(source);
        await Assert.That(candidates.Count(c => c.OperatorName == "ConditionMutator")).IsEqualTo(0);
    }

    /// <summary>null 許容値型の合体に対する左側落としの抑止</summary>
    [Test]
    public async Task Nullable_value_coalesce_keeps_only_the_right_drop()
    {
        var candidates = Snippet.Collect("class C { int M(int? a) => a ?? 5; }");
        var drops = candidates.Where(c => c.OperatorName == "NullCoalesceMutator").ToArray();
        await Assert.That(drops.Length).IsEqualTo(1);
        await Assert.That(drops[0].ReplacementText).IsEqualTo("5");
    }

    /// <summary>参照型の合体に対する両側落としの生成</summary>
    [Test]
    public async Task Reference_coalesce_yields_both_drops()
    {
        var candidates = Snippet.Collect("class C { string M(string? a, string b) => a ?? b; }");
        await Assert.That(candidates.Count(c => c.OperatorName == "NullCoalesceMutator")).IsEqualTo(2);
    }

    /// <summary>out 引数を持つ呼び出し文に対する除去の抑止</summary>
    [Test]
    public async Task Invocation_statements_with_out_arguments_are_not_removed()
    {
        var source = "class C { int M(string raw) { int.TryParse(raw, out var value); return value; } }";
        var candidates = Snippet.Collect(source);
        await Assert.That(candidates.Count(c => c.OperatorName == "StatementRemover")).IsEqualTo(0);
    }
}
