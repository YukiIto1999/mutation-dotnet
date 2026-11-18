using Mutation.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定を決める入力の閉包の同一性と、変わった行を加えた全体の指紋の検査</summary>
/// <remarks>
/// 一時 directory に csc の引数が指す実ファイル、test の出力、worker の実体を置き、読める入力の変化が同一性へ届くことと、読めない入力が同一性を不在にすることを見る
/// 同一性は走行のたびに読み直すので、どの検査も、同じ process の中で変更の前後に求めて比べる
/// </remarks>
public sealed partial class CompilationFingerprintFacts
{
    /// <summary>読める入力が不変なら、同一性が不在でなく、何度求めても同じこと</summary>
    [Test]
    public async Task Unchanged_inputs_give_the_same_identity()
    {
        using var project = new ClosureProject();

        var first = project.Compute();
        var second = project.Compute();

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsEqualTo(first);
    }

    /// <summary>判定へ届く入力のどれが変わっても、閉包の同一性と全体の指紋が変わること</summary>
    /// <remarks>
    /// 対象のソース、他のソース、参照する assembly、generator、additional file、analyzer config、resource、
    /// テスト assembly、テストの補助 file、テストの directory の下の補助 file
    /// </remarks>
    /// <param name="input">変える入力の名前</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("source")]
    [Arguments("other-source")]
    [Arguments("reference")]
    [Arguments("analyzer")]
    [Arguments("additional-file")]
    [Arguments("analyzer-config")]
    [Arguments("resource")]
    [Arguments("test-assembly")]
    [Arguments("test-support-file")]
    [Arguments("test-nested-support-file")]
    [Arguments("worker-host")]
    [Arguments("worker-assembly")]
    [Arguments("worker-runtimeconfig")]
    [Arguments("worker-deps")]
    [Arguments("worker-dependency")]
    [Arguments("worker-added-file")]
    public async Task A_change_to_any_input_that_reaches_the_verdicts_changes_the_closure_identity(string input)
    {
        using var project = new ClosureProject();
        var before = project.Compute()!;

        project.Change(input);
        var after = project.Compute()!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
        await Assert.That(after.Fingerprint).IsNotEqualTo(before.Fingerprint);
    }

    /// <summary>worker の directory から file が消えると、同一性が変わること</summary>
    /// <param name="input">消す入力の名前</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("worker-runtimeconfig")]
    [Arguments("worker-deps")]
    [Arguments("worker-dependency")]
    public async Task Removing_a_file_the_worker_is_deployed_with_changes_the_closure_identity(string input)
    {
        using var project = new ClosureProject();
        var before = project.Compute()!;

        project.Remove(input);
        var after = project.Compute()!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
        await Assert.That(after.Fingerprint).IsNotEqualTo(before.Fingerprint);
    }

    /// <summary>csc の引数の変化が、閉包の同一性を変えること</summary>
    [Test]
    public async Task A_change_of_compiler_arguments_changes_the_closure_identity()
    {
        using var project = new ClosureProject();
        var baseline = project.Compute()!;

        var otherArguments = project.Compute(extraArgument: "/define:EXTRA")!;

        await Assert.That(otherArguments.Closure).IsNotEqualTo(baseline.Closure);
    }

    /// <summary>`TestResults` の名の directory に置かれた、test が実行時に読む data が、同一性に入ること</summary>
    /// <remarks>名前だけでは engine や test の実行体が書いた成果物とは言えず、test の判定を決める入力かもしれない</remarks>
    [Test]
    public async Task Runtime_data_under_a_directory_named_TestResults_is_part_of_the_identity()
    {
        using var project = new ClosureProject();
        var before = project.Compute()!;

        project.Change("test-results-data");
        var after = project.Compute()!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
        await Assert.That(after.Fingerprint).IsNotEqualTo(before.Fingerprint);
    }

    /// <summary>engine 自身が成果物を書く directory は、test の directory の中にあっても同一性に入らないこと</summary>
    [Test]
    public async Task Only_the_directory_the_engine_owns_is_left_out_of_the_test_closure()
    {
        using var project = new ClosureProject(outputInsideTests: true);
        var before = project.Compute()!;

        project.WriteOwnedOutput();
        var afterOwnedOutput = project.Compute();

        await Assert.That(afterOwnedOutput).IsEqualTo(before);
    }

    /// <summary>engine 自身の directory と名前が前方一致するだけの別の directory は、同一性に入ること</summary>
    [Test]
    public async Task A_neighbour_whose_name_only_starts_like_the_owned_directory_is_part_of_the_identity()
    {
        using var project = new ClosureProject(outputInsideTests: true);
        var before = project.Compute()!;

        project.WriteNeighbourOfOwnedDirectory();
        var after = project.Compute()!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
    }

    /// <summary>除外する出力先がテスト assembly を含むときは、不完全な閉包で前回の判定を再利用しないこと</summary>
    /// <param name="ancestor">テスト directory 自体ではなく、その親を出力先にするか</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task An_output_directory_containing_the_test_assembly_disables_reuse(bool ancestor)
    {
        using var project = new ClosureProject();
        var output = ancestor ? Path.GetDirectoryName(project.TestOutputDirectory)! : project.TestOutputDirectory;

        await Assert.That(project.Compute(outputDirectory: output)).IsNull();
    }

    /// <summary>判定に届かない file の変化は、同一性を変えないこと</summary>
    [Test]
    public async Task A_file_that_is_not_an_input_does_not_change_the_identity()
    {
        using var project = new ClosureProject();
        var before = project.Compute();

        project.WriteUnrelatedFile();
        var after = project.Compute();

        await Assert.That(after).IsEqualTo(before);
    }

    /// <summary>test の出力に届かない test project の source の変化は、同一性を変えないこと</summary>
    /// <remarks>
    /// test は出力された assembly と実行時 data を実行する。source は出力へ届いたときだけ判定に届き、届く変更は test assembly の変化として検出される
    /// 出力が同一のまま判定だけが変わる source の変化は、出力の外の file や network を読む test に限られ、閉包の範囲の外である
    /// </remarks>
    [Test]
    public async Task A_test_project_source_that_does_not_reach_the_test_output_does_not_change_the_identity()
    {
        using var project = new ClosureProject();
        var before = project.Compute();

        project.WriteTestProjectSource();
        var after = project.Compute();

        await Assert.That(after).IsEqualTo(before);
    }

    /// <summary>変わった行の範囲は全体の指紋だけを変え、閉包の同一性を変えないこと</summary>
    /// <remarks>変異ごとの継承は閉包の同一性を鍵にするので、変わった行が違う走行の間でも、入力が同じなら継承できる</remarks>
    [Test]
    public async Task Changed_line_ranges_change_the_fingerprint_but_not_the_closure_identity()
    {
        using var project = new ClosureProject();
        var plain = project.Compute()!;

        var narrowed = project.Compute(changes: project.Changes("source", new LineRange(3, 5)))!;
        var moved = project.Compute(changes: project.Changes("source", new LineRange(3, 6)))!;

        await Assert.That(narrowed.Closure).IsEqualTo(plain.Closure);
        await Assert.That(narrowed.Fingerprint).IsNotEqualTo(plain.Fingerprint);
        await Assert.That(moved.Closure).IsEqualTo(plain.Closure);
        await Assert.That(moved.Fingerprint).IsNotEqualTo(narrowed.Fingerprint);
    }

    /// <summary>対象のソースでない file の変わった行は、全体の指紋も変えないこと</summary>
    [Test]
    public async Task Changed_lines_of_a_file_that_is_not_a_source_do_not_change_the_fingerprint()
    {
        using var project = new ClosureProject();
        var plain = project.Compute()!;

        var changed = project.Compute(changes: project.Changes("test-support-file", LineRange.WholeFile))!;

        await Assert.That(changed).IsEqualTo(plain);
    }

    /// <summary>差分運用で選んだ候補の範囲は全体の指紋だけを変え、閉包の同一性を変えないこと</summary>
    /// <remarks>候補の範囲が違う走行の保存を、全体の再構築に使わないためで、変異ごとの継承は候補の範囲によらない</remarks>
    [Test]
    public async Task The_selected_scope_changes_the_fingerprint_but_not_the_closure_identity()
    {
        using var project = new ClosureProject();
        var none = project.Compute(selection: "no-impact\n")!;
        var full = project.Compute(selection: "full-production\n*")!;

        await Assert.That(full.Closure).IsEqualTo(none.Closure);
        await Assert.That(full.Fingerprint).IsNotEqualTo(none.Fingerprint);
    }

    /// <summary>読めない入力が一つでもあれば、同一性を示せないので不在になること</summary>
    /// <param name="input">消す入力の名前。test-directory はテスト assembly の directory 自体、worker-directory は worker assembly の directory 自体</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("source")]
    [Arguments("reference")]
    [Arguments("analyzer")]
    [Arguments("additional-file")]
    [Arguments("analyzer-config")]
    [Arguments("resource")]
    [Arguments("test-directory")]
    [Arguments("worker-host")]
    [Arguments("worker-assembly")]
    [Arguments("worker-directory")]
    public async Task An_input_that_cannot_be_read_gives_no_identity(string input)
    {
        using var project = new ClosureProject();
        await Assert.That(project.Compute()).IsNotNull();

        project.Remove(input);

        await Assert.That(project.Compute()).IsNull();
    }

}
