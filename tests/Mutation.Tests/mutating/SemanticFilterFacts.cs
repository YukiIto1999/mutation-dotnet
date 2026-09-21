
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

    /// <summary>pattern 内の定数に対する既存変異の抑止</summary>
    [Test]
    public async Task Patterns_yield_only_relational_pattern_candidates()
    {
        var candidates = Snippet.Collect("class C { bool M(int a) => a is > 5 or < -3; }");

        await Assert.That(candidates.All(candidate => candidate.OperatorName == "RelationalPatternMutator")).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ReplacementText == ">= 5")).IsTrue();
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

    /// <summary>変数を宣言する条件に対する否定変異の生成</summary>
    [Test]
    public async Task Conditions_declaring_pattern_variables_are_negated()
    {
        var source = "class C { int M(object o) { if (o is string s) { return 1; } return 0; } }";
        var candidates = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "ConditionMutator")
            .ToArray();

        await Assert.That(candidates.Length).IsEqualTo(1);
        await Assert.That(candidates[0].ReplacementText.StartsWith("!(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(candidates[0].ReportTarget.ToString()).IsEqualTo("o is string s");
        await Assert.That(candidates.Any(candidate => candidate.ReplacementText is "true" or "false")).IsFalse();
    }

    /// <summary>否定で pattern 変数の確定代入を壊す条件の抑止</summary>
    [Test]
    public async Task Pattern_variable_negation_is_dropped_when_body_uses_the_variable()
    {
        var source = "class C { int M(object o) { if (o is string s) { return s.Length; } return 0; } }";
        var candidates = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "ConditionMutator")
            .ToArray();

        await Assert.That(candidates).IsEmpty();
    }

    /// <summary>三項の pattern 変数を否定で未確定にする変異の抑止</summary>
    [Test]
    public async Task Ternary_pattern_variable_negation_is_dropped()
    {
        var source = "class C { int M(object o) => o is string s ? s.Length : 0; }";
        var candidates = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "ConditionMutator")
            .ToArray();

        await Assert.That(candidates).IsEmpty();
    }

    /// <summary>out 変数を宣言する条件に対する否定変異の生成</summary>
    [Test]
    public async Task Conditions_declaring_out_variables_are_negated()
    {
        var source = "class C { int M(string raw) { if (int.TryParse(raw, out var value)) Console.WriteLine(\"ok\"); return value; } }";
        var candidates = Snippet.Collect(source)
            .Where(candidate => candidate.OperatorName == "ConditionMutator")
            .ToArray();

        await Assert.That(candidates.Length).IsEqualTo(1);
        await Assert.That(candidates[0].ReplacementText).IsEqualTo("!(int.TryParse(raw, out var value))");
        await Assert.That(candidates.Any(candidate => candidate.ReplacementText is "true" or "false")).IsFalse();
    }
    /// <summary>out 変数を含む否定 schemata の compile と実行</summary>
    [Test]
    public async Task Negated_out_condition_compiles_and_changes_branch()
    {
        var source = "public static class C { public static int M(string raw) { if (int.TryParse(raw, out var value)) return value; return 0; } }";
        var indexed = Snippet.Collect(source)
            .Select((candidate, id) => (candidate, id))
            .First(pair => pair.candidate.OperatorName == "ConditionMutator");
        var (assembly, _) = Snippet.EmitWithSchemata(source);
        var control = assembly.GetType("MutationInjected.MutantSwitch")!;
        var method = assembly.GetType("C")!.GetMethod("M")!;

        control.GetMethod("Activate")!.Invoke(null, [indexed.id, long.MaxValue]);
        await Assert.That((int)method.Invoke(null, ["42"])!).IsEqualTo(0);
        control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
        await Assert.That((int)method.Invoke(null, ["42"])!).IsEqualTo(42);
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
