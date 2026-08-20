

using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using Mutation.Mutating.Infrastructure.Roslyn;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>選別方針によるファイル・演算子・呼出名の絞り込みの検査</summary>
public sealed class MutationPolicyFacts
{
    /// <summary>差分絞りが glob 範囲との積になること</summary>
    [Test]
    public async Task Changed_file_filter_intersects_with_glob_scope()
    {
        var policy = new MutationPolicy(
            MutationScope.FromPatterns(["domain/**/*.cs"]),
            new HashSet<string>(StringComparer.Ordinal) { Path.GetFullPath("/repo/domain/Money.cs") },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal)
        );
        await Assert.That(policy.IncludesFile("/repo/domain/Money.cs", "domain/Money.cs")).IsTrue();
        await Assert.That(policy.IncludesFile("/repo/domain/Other.cs", "domain/Other.cs")).IsFalse();
        await Assert.That(policy.IncludesFile("/repo/app/Money.cs", "app/Money.cs")).IsFalse();
    }

    /// <summary>演算子除外が大文字小文字を区別しないこと</summary>
    [Test]
    public async Task Ignored_operators_drop_their_candidates()
    {
        var policy = new MutationPolicy(
            MutationScope.FromPatterns([]),
            changedFiles: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "literalmutator" },
            new HashSet<string>(StringComparer.Ordinal)
        );
        var candidates = Snippet.Collect("""class C { string M(bool f) => f ? "a" : "b"; }""", policy);
        await Assert.That(candidates.Any(c => c.OperatorName == "LiteralMutator")).IsFalse();
        await Assert.That(candidates.Any(c => c.OperatorName == "ConditionMutator")).IsTrue();
    }

    /// <summary>除外した呼出の中の変異だけが消えること</summary>
    [Test]
    public async Task Ignored_methods_drop_only_mutations_inside_their_calls()
    {
        var policy = new MutationPolicy(
            MutationScope.FromPatterns([]),
            changedFiles: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal) { "ConfigureAwait" }
        );
        var source = """
            class C
            {
                async System.Threading.Tasks.Task<int> M(int a, int b)
                {
                    await System.Threading.Tasks.Task.Delay(a + b).ConfigureAwait(false);
                    return a + b;
                }
            }
            """;
        var candidates = Snippet.Collect(source, policy);
        var texts = candidates.Select(c => c.ReplacementText).ToArray();
        await Assert.That(texts.Contains("true")).IsFalse();
        await Assert.That(texts.Contains("a - b")).IsTrue();
    }

    /// <summary>除外した呼出文の除去も生成されないこと</summary>
    [Test]
    public async Task Statement_removal_of_ignored_calls_is_not_generated()
    {
        var policy = new MutationPolicy(
            MutationScope.FromPatterns([]),
            changedFiles: null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal) { "Touch" }
        );
        var source = "class C { static int n; static void Touch(int v) { n = v; } void M() { Touch(1); } }";
        var candidates = Snippet.Collect(source, policy);
        var removals = candidates
            .OfType<MutationCandidate.StatementSwap>()
            .Where(c => c.OperatorName == "StatementRemover")
            .ToArray();
        await Assert.That(removals.Any(c => c.Statement.ToString().Contains("Touch(1)"))).IsFalse();
    }
}
