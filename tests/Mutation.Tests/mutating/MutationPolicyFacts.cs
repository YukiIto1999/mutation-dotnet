

using Mutation.Mutating.Domain;
using Mutation.Shared;
using Mutation.Mutating.Infrastructure.Roslyn.Operators;
using Mutation.Mutating.Infrastructure.Roslyn;
using TUnit.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    /// <summary>変異の報告範囲と変更行の閉区間が重なる候補だけを選ぶ</summary>
    [Test]
    public async Task Changed_line_filter_uses_report_span_and_keeps_since_file_scope()
    {
        var path = Path.GetFullPath("/repo/domain/Money.cs");
        var changes = new ChangedLines(new Dictionary<string, IReadOnlyList<LineRange>>
        {
            [path] = [new LineRange(5, 6)],
            [Path.GetFullPath("/repo/domain/Removed.cs")] = [],
        });
        var policy = new MutationPolicy(
            MutationScope.FromPatterns(["domain/**/*.cs"]),
            changes.Files,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            changes
        );

        await Assert.That(policy.IncludesSpan(path, 4, 5)).IsTrue();
        await Assert.That(policy.IncludesSpan(path, 6, 7)).IsTrue();
        await Assert.That(policy.IncludesSpan(path, 3, 4)).IsFalse();
        await Assert.That(policy.IncludesSpan(path, 7, 8)).IsFalse();
        await Assert.That(policy.IncludesSpan("/repo/domain/Removed.cs", 5, 5)).IsFalse();
        await Assert.That(policy.IncludesSpan("/repo/domain/Other.cs", 5, 5)).IsFalse();
    }

    /// <summary>候補の報告位置を変更行に合わせて隣の行の候補を除外すること</summary>
    [Test]
    public async Task Collector_selects_only_candidates_reported_on_the_changed_line()
    {
        var path = Path.GetFullPath("/repo/domain/Arithmetic.cs");
        var tree = CSharpSyntaxTree.ParseText(
            "class C\n{\n    int Add(int a, int b) => a + b;\n    int Sub(int a, int b) => a - b;\n}\n",
            path: path
        );
        var compilation = CSharpCompilation.Create(
            "Arithmetic",
            [tree],
            Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var changes = new ChangedLines(new Dictionary<string, IReadOnlyList<LineRange>>
        {
            [path] = [new LineRange(3, 3)],
        });
        var policy = new MutationPolicy(
            MutationScope.FromPatterns([]),
            changes.Files,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            changes
        );
        var candidates = CandidateCollector.Collect(
            await tree.GetRootAsync(), compilation.GetSemanticModel(tree), CandidateCollector.DefaultOperators, policy
        );

        await Assert.That(candidates.Any(candidate =>
            candidate.ReportTarget.GetLocation().GetLineSpan().StartLinePosition.Line == 2)).IsTrue();
        await Assert.That(candidates.Any(candidate =>
            candidate.ReportTarget.GetLocation().GetLineSpan().StartLinePosition.Line == 3)).IsFalse();
    }

    /// <summary>変更行より前から始まる報告範囲の変異も選択すること</summary>
    [Test]
    public async Task Collector_keeps_mutants_whose_multiline_report_span_covers_the_changed_line()
    {
        var path = Path.GetFullPath("/repo/domain/Arithmetic.cs");
        var tree = CSharpSyntaxTree.ParseText(
            "class C\n{\n    int Add(int a, int b)\n    {\n        return a + b;\n    }\n}\n",
            path: path
        );
        var compilation = CSharpCompilation.Create(
            "Arithmetic",
            [tree],
            Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var changes = new ChangedLines(new Dictionary<string, IReadOnlyList<LineRange>>
        {
            [path] = [new LineRange(5, 5)],
        });
        var policy = new MutationPolicy(
            MutationScope.FromPatterns([]),
            changes.Files,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            changes
        );

        var candidates = CandidateCollector.Collect(
            await tree.GetRootAsync(), compilation.GetSemanticModel(tree), CandidateCollector.DefaultOperators, policy
        );

        await Assert.That(candidates.Any(candidate =>
        {
            var span = candidate.ReportTarget.GetLocation().GetLineSpan();
            return span.StartLinePosition.Line + 1 < 5 && span.EndLinePosition.Line + 1 >= 5;
        })).IsTrue();
        await Assert.That(candidates.Any(candidate => candidate.ReportTarget.ToString() == "a + b")).IsTrue();
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
