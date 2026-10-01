using Mutation.Shared;
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>変わった行に重なる変異の選別と、未検出の数え方の検査</summary>
public sealed class ChangedLineVerdictsFacts
{
    /// <summary>変わった行として /repo/A.cs の 10 行目だけを持つ差分</summary>
    private static readonly ChangedLines LineTenOfA = new(
        new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal)
        {
            [Path.GetFullPath("/repo/A.cs")] = [new LineRange(10, 10)],
        }
    );

    /// <summary>範囲が変わった行と一行でも重なる変異だけが選ばれ、他の行と他のファイルの変異は外れること</summary>
    [Test]
    public async Task Only_mutants_overlapping_changed_lines_are_selected()
    {
        RecordedMutant[] mutants =
        [
            Mutant("on", "/repo/A.cs", 10, 10, new MutantVerdict.Killed("t")),
            Mutant("spanning", "/repo/A.cs", 8, 12, new MutantVerdict.Killed("t")),
            Mutant("below", "/repo/A.cs", 11, 11, new MutantVerdict.Killed("t")),
            Mutant("above", "/repo/A.cs", 9, 9, new MutantVerdict.Killed("t")),
            Mutant("other-file", "/repo/B.cs", 10, 10, new MutantVerdict.Killed("t")),
        ];

        var selected = ChangedLineVerdicts.Select(mutants, LineTenOfA);

        await Assert.That(selected.OnChangedLines.Select(m => m.Id).ToArray()).IsEquivalentTo(["on", "spanning"]);
    }

    /// <summary>未検出が生存と被覆なしだけで数えられ、検出・時間切れ・コンパイル失敗・除外は入らないこと</summary>
    [Test]
    public async Task Undetected_are_survived_and_no_coverage_only()
    {
        RecordedMutant[] mutants =
        [
            Mutant("killed", "/repo/A.cs", 10, 10, new MutantVerdict.Killed("t")),
            Mutant("survived", "/repo/A.cs", 10, 10, new MutantVerdict.Survived()),
            Mutant("timeout", "/repo/A.cs", 10, 10, new MutantVerdict.TimedOut()),
            Mutant("no-coverage", "/repo/A.cs", 10, 10, new MutantVerdict.NoCoverage()),
            Mutant("compile-error", "/repo/A.cs", 10, 10, new MutantVerdict.CompileError()),
            Mutant("ignored", "/repo/A.cs", 10, 10, new MutantVerdict.Ignored()),
        ];

        var selected = ChangedLineVerdicts.Select(mutants, LineTenOfA);

        await Assert.That(selected.OnChangedLines.Count).IsEqualTo(6);
        await Assert.That(selected.Undetected.Select(m => m.Id).ToArray()).IsEquivalentTo(["survived", "no-coverage"]);
    }

    /// <summary>変わった行の外で生存した変異が、未検出に数えられないこと</summary>
    [Test]
    public async Task Survivors_outside_changed_lines_do_not_count()
    {
        RecordedMutant[] mutants =
        [
            Mutant("elsewhere", "/repo/A.cs", 50, 50, new MutantVerdict.Survived()),
            Mutant("covered", "/repo/A.cs", 10, 10, new MutantVerdict.Killed("t")),
        ];

        var selected = ChangedLineVerdicts.Select(mutants, LineTenOfA);

        await Assert.That(selected.Undetected).IsEmpty();
    }

    /// <summary>行の範囲と確定結果だけを指定した記録済みの変異</summary>
    private static RecordedMutant Mutant(string id, string path, int line, int endLine, MutantVerdict verdict) =>
        new(id, Path.GetFullPath(path), new SourceSpan(line, 1, endLine, 2), "BinaryOperatorMutator", "a - b", verdict);
}
