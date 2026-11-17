using Mutation.Mutating.Domain;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>基点から変わったファイルが対象の変異候補へ届く関係と、候補の範囲の選択の検査</summary>
/// <remarks>build graph が示す入力は、対象一件、対にしたテスト一件、参照する project 一件で組み立てる</remarks>
public sealed class ChangeImpactFacts
{
    /// <summary>対象の project の directory</summary>
    private static readonly string TargetDirectory = Path.GetFullPath("/repo/src/Target");

    /// <summary>テスト project の directory</summary>
    private static readonly string TestDirectory = Path.GetFullPath("/repo/tests/Target.Tests");

    /// <summary>参照する project の directory</summary>
    private static readonly string LibraryDirectory = Path.GetFullPath("/repo/src/Library");

    /// <summary>対象とテストと参照する project の入力</summary>
    private static readonly TargetGraph Graph = new(
        new ProjectInputs(
            Path.Combine(TargetDirectory, "Target.csproj"),
            [Path.Combine(TargetDirectory, "A.cs"), Path.Combine(TargetDirectory, "B.cs")],
            [Path.Combine(TargetDirectory, "obj", "Target.GeneratedMSBuildEditorConfig.editorconfig"), Path.GetFullPath("/repo/lib/Native.dll")]
        ),
        new ProjectInputs(Path.Combine(TestDirectory, "Target.Tests.csproj"), [Path.Combine(TestDirectory, "ATests.cs")], []),
        [new ProjectInputs(Path.Combine(LibraryDirectory, "Library.csproj"), [Path.Combine(LibraryDirectory, "L.cs")], [])],
        []
    );

    /// <summary>対象のソースの変更が、対象の全範囲を候補にし、変えたソースの未変更の呼び出し元を関係として残すこと</summary>
    [Test]
    public async Task A_production_change_selects_the_whole_production_scope_because_callers_cannot_be_selected()
    {
        var assessment = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "A.cs")), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(assessment.Files).IsNull();
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["callers-of-changed-production", "production-changed"]);
        await Assert.That(assessment.Selection.Unanalysed).IsEmpty();
    }

    /// <summary>テストだけの変更が、影響なしにならず、対象の全範囲を候補にすること</summary>
    [Test]
    public async Task A_test_only_change_selects_the_whole_production_scope()
    {
        var assessment = ChangeImpact.Select(Changed(Path.Combine(TestDirectory, "ATests.cs")), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(assessment.Files).IsNull();
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["test-changed"]);
    }

    /// <summary>build の設定と依存の版の変更が、対象とテストの project の directory の祖先にあっても、対象の全範囲を候補にすること</summary>
    /// <param name="path">変えたファイルの絶対 path</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("/repo/Directory.Build.props")]
    [Arguments("/repo/Directory.Packages.props")]
    [Arguments("/repo/global.json")]
    [Arguments("/repo/NuGet.Config")]
    [Arguments("/repo/src/Target/packages.lock.json")]
    [Arguments("/repo/.editorconfig")]
    [Arguments("/repo/src/Target/Target.csproj")]
    [Arguments("/repo/tests/Target.Tests/Target.Tests.csproj")]
    [Arguments("/repo/lib/Native.dll")]
    [Arguments("/repo/src/Target/obj/Target.GeneratedMSBuildEditorConfig.editorconfig")]
    public async Task A_build_configuration_change_selects_the_whole_production_scope(string path)
    {
        var assessment = ChangeImpact.Select(Changed(Path.GetFullPath(path)), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["config-changed"]);
    }

    /// <summary>参照する project のソースの変更が、対象の全範囲を候補にすること</summary>
    [Test]
    public async Task A_change_in_a_referenced_project_selects_the_whole_production_scope()
    {
        var assessment = ChangeImpact.Select(Changed(Path.Combine(LibraryDirectory, "L.cs")), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["dependency-changed"]);
    }

    /// <summary>消えたファイルは、project の外であっても入力との関係を否定できないため全範囲を候補にすること</summary>
    [Test]
    public async Task A_removed_file_selects_the_whole_production_scope()
    {
        var inProject = ChangeImpact.Select(ChangedWithDeleted([], Path.Combine(TargetDirectory, "Gone.cs")), Graph, changedLinesOnly: false);
        var sourceOutside = ChangeImpact.Select(ChangedWithDeleted([], Path.GetFullPath("/repo/src/Shared/Linked.cs")), Graph, changedLinesOnly: false);
        var documentOutside = ChangeImpact.Select(ChangedWithDeleted([], Path.GetFullPath("/repo/docs/old.md")), Graph, changedLinesOnly: false);

        await Assert.That(Relations(inProject.Selection.Reasons)).IsEquivalentTo(["file-removed"]);
        await Assert.That(Relations(sourceOutside.Selection.Reasons)).IsEquivalentTo(["file-removed"]);
        await Assert.That(Relations(documentOutside.Selection.Reasons)).IsEquivalentTo(["file-removed"]);
    }

    /// <summary>compiler 入力との関係が不明な変更を、project の内外や拡張子だけで影響なしにしないこと</summary>
    /// <param name="path">関係を判定できない変更ファイル</param>
    /// <returns>対象全体を選ぶことの検査</returns>
    [Test]
    [Arguments("/repo/tests/Target.Tests/data/sample.json")]
    [Arguments("/repo/build/shared.props")]
    [Arguments("/repo/docs/guide.md")]
    [Arguments("/repo/src/Other/Unrelated.cs")]
    [Arguments("/repo/src/Other/Other.csproj")]
    [Arguments("/repo/src/Target/Excluded.cs")]
    public async Task A_change_outside_the_known_compiler_inputs_selects_the_whole_production_scope(string path)
    {
        var assessment = ChangeImpact.Select(Changed(Path.GetFullPath(path)), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.FullProduction).IsTrue();
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["unknown-input"]);
    }

    /// <summary>対象の入力でない変更だけなら、影響なしで候補が空になり、変更した file を対象の入力でないものとして残すこと</summary>
    /// <param name="path">変えたファイルの絶対 path</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("/repo/src/Target/obj/Debug/Target.AssemblyInfo.cs")]
    [Arguments("/repo/src/Target/bin/Debug/Target.dll")]
    public async Task A_change_that_is_not_an_input_of_the_target_selects_nothing_and_is_recorded(string path)
    {
        var assessment = ChangeImpact.Select(Changed(Path.GetFullPath(path)), Graph, changedLinesOnly: false);

        await Assert.That(assessment.Selection.Scope is SelectionScope.NoImpact).IsTrue();
        await Assert.That(assessment.Files).IsEmpty();
        await Assert.That(assessment.Selection.Reasons).IsEmpty();
        await Assert.That(assessment.Selection.NotInputs.ToArray()).IsEquivalentTo([Path.GetFullPath(path)]);
    }

    /// <summary>明示した変更行の部分集合が、変えたソースだけを候補にし、広げずに、テストなどの届く関係を未分析として残すこと</summary>
    [Test]
    public async Task The_explicit_changed_line_subset_stays_narrow_and_records_what_it_leaves_unanalysed()
    {
        var changes = Changed(Path.Combine(TargetDirectory, "A.cs"), Path.Combine(TestDirectory, "ATests.cs"), Path.GetFullPath("/repo/Directory.Build.props"));

        var assessment = ChangeImpact.Select(changes, Graph, changedLinesOnly: true);

        await Assert.That(assessment.Selection.Scope is SelectionScope.ExplicitSubset).IsTrue();
        await Assert.That(assessment.Files!.ToArray()).IsEquivalentTo([Path.Combine(TargetDirectory, "A.cs")]);
        await Assert.That(Relations(assessment.Selection.Reasons)).IsEquivalentTo(["production-changed"]);
        await Assert.That(Relations(assessment.Selection.Unanalysed))
            .IsEquivalentTo(["callers-of-changed-production", "config-changed", "test-changed"]);
    }

    /// <summary>テストだけの変更に明示した変更行の部分集合を求めても、候補は空でも影響なしとは記録せず、テストの変更を未分析として残すこと</summary>
    [Test]
    public async Task A_test_only_change_under_the_explicit_subset_is_not_recorded_as_no_impact()
    {
        var assessment = ChangeImpact.Select(Changed(Path.Combine(TestDirectory, "ATests.cs")), Graph, changedLinesOnly: true);

        await Assert.That(assessment.Selection.Scope is SelectionScope.ExplicitSubset).IsTrue();
        await Assert.That(assessment.Files).IsEmpty();
        await Assert.That(assessment.Selection.Reasons).IsEmpty();
        await Assert.That(Relations(assessment.Selection.Unanalysed)).IsEquivalentTo(["test-changed"]);
    }

    /// <summary>対象の入力に届く変更がなければ、明示した変更行の部分集合も影響なしになること</summary>
    [Test]
    public async Task Nothing_reaching_the_target_is_no_impact_under_the_explicit_subset_too()
    {
        var assessment = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "obj", "unused.bin")), Graph, changedLinesOnly: true);

        await Assert.That(assessment.Selection.Scope is SelectionScope.NoImpact).IsTrue();
        await Assert.That(assessment.Selection.Unanalysed).IsEmpty();
    }

    /// <summary>候補の範囲を見分ける鍵が、範囲と変異させるファイルで変わること</summary>
    [Test]
    public async Task The_selection_key_tells_the_scope_and_the_files_apart()
    {
        var full = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "A.cs")), Graph, changedLinesOnly: false);
        var subsetA = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "A.cs")), Graph, changedLinesOnly: true);
        var subsetB = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "B.cs")), Graph, changedLinesOnly: true);
        var none = ChangeImpact.Select(Changed(Path.Combine(TargetDirectory, "obj", "unused.bin")), Graph, changedLinesOnly: false);

        await Assert.That(new[] { full.Key, subsetA.Key, subsetB.Key, none.Key }.Distinct().Count()).IsEqualTo(4);
    }

    /// <summary>関係の名前の並び</summary>
    private static string[] Relations(IReadOnlyList<ImpactReason> reasons) =>
        [.. reasons.Select(reason => reason.Relation.Name).Order(StringComparer.Ordinal)];

    /// <summary>内容が変わったファイルの変更</summary>
    private static ChangedLines Changed(params string[] present) => ChangedWithDeleted(present);

    /// <summary>内容が変わったファイルと、消えたファイルの変更</summary>
    private static ChangedLines ChangedWithDeleted(string[] present, params string[] deleted)
    {
        var ranges = new Dictionary<string, IReadOnlyList<LineRange>>(StringComparer.Ordinal);
        foreach (var path in present)
        {
            ranges[path] = [new LineRange(1, 1)];
        }

        foreach (var path in deleted)
        {
            ranges[path] = [];
        }

        return new ChangedLines(ranges, deleted.ToHashSet(StringComparer.Ordinal));
    }
}
