using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>判定を決める入力の閉包の同一性と、変わった行を加えた全体の指紋の検査</summary>
/// <remarks>一時 directory に csc の引数が指す実ファイルを置き、読める入力の変化が同一性へ届くことと、読めない入力が同一性を不在にすることを見る</remarks>
public sealed class CompilationFingerprintFacts
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
    public async Task A_change_to_any_input_that_reaches_the_verdicts_changes_the_closure_identity(string input)
    {
        using var project = new ClosureProject();
        var before = project.Compute()!;

        project.Change(input);
        var after = project.Compute()!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
        await Assert.That(after.Fingerprint).IsNotEqualTo(before.Fingerprint);
    }

    /// <summary>設定の要約と csc の引数の変化が、閉包の同一性を変えること</summary>
    [Test]
    public async Task A_change_of_settings_or_compiler_arguments_changes_the_closure_identity()
    {
        using var project = new ClosureProject();
        var baseline = project.Compute()!;

        var otherSettings = project.Compute(settings: "other settings")!;
        var otherArguments = project.Compute(extraArgument: "/define:EXTRA")!;

        await Assert.That(otherSettings.Closure).IsNotEqualTo(baseline.Closure);
        await Assert.That(otherArguments.Closure).IsNotEqualTo(baseline.Closure);
    }

    /// <summary>テストの実行体が成果物を書く directory の変化は、同一性を変えないこと</summary>
    [Test]
    public async Task Files_under_a_test_results_directory_do_not_change_the_identity()
    {
        using var project = new ClosureProject();
        var before = project.Compute();

        project.WriteTestResult();
        var after = project.Compute();

        await Assert.That(after).IsEqualTo(before);
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
    /// <param name="input">消す入力の名前。test-directory はテスト assembly の directory 自体</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("source")]
    [Arguments("reference")]
    [Arguments("analyzer")]
    [Arguments("additional-file")]
    [Arguments("analyzer-config")]
    [Arguments("resource")]
    [Arguments("test-directory")]
    public async Task An_input_that_cannot_be_read_gives_no_identity(string input)
    {
        using var project = new ClosureProject();
        await Assert.That(project.Compute()).IsNotNull();

        project.Remove(input);

        await Assert.That(project.Compute()).IsNull();
    }

    /// <summary>csc の引数が指す実 file と、テスト assembly の directory を持つ検査環境</summary>
    private sealed class ClosureProject : IDisposable
    {
        /// <summary>この環境の一時 directory</summary>
        private readonly string root = Directory.CreateTempSubdirectory("mutation-closure-facts").FullName;

        /// <summary>入力の名前から実 file の絶対 path への対応</summary>
        private readonly Dictionary<string, string> inputs;

        /// <summary>全入力の file を初期の内容で置いた環境の作成</summary>
        public ClosureProject()
        {
            inputs = new Dictionary<string, string>
            {
                ["source"] = Path.Combine(root, "src", "Target.cs"),
                ["other-source"] = Path.Combine(root, "src", "Other.cs"),
                ["reference"] = Path.Combine(root, "refs", "Lib.dll"),
                ["analyzer"] = Path.Combine(root, "analyzers", "Generator.dll"),
                ["additional-file"] = Path.Combine(root, "src", "data.txt"),
                ["analyzer-config"] = Path.Combine(root, ".editorconfig"),
                ["resource"] = Path.Combine(root, "src", "strings.bin"),
                ["test-assembly"] = Path.Combine(root, "tests", "bin", "Tests.dll"),
                ["test-support-file"] = Path.Combine(root, "tests", "bin", "config.json"),
                ["test-nested-support-file"] = Path.Combine(root, "tests", "bin", "data", "sample.txt"),
            };
            foreach (var (name, path) in inputs)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"initial {name}");
            }
        }

        /// <summary>閉包の同一性と全体の指紋の計算</summary>
        public ReuseIdentity? Compute(
            string settings = "settings",
            string? extraArgument = null,
            ChangedLines? changes = null,
            string selection = ""
        ) => CompilationFingerprint.Compute(Invocation(extraArgument), inputs["test-assembly"], settings, changes, selection);

        /// <summary>入力の file の内容の変更</summary>
        public void Change(string input) => File.AppendAllText(inputs[input], " changed");

        /// <summary>入力の file、または test-directory の消去</summary>
        public void Remove(string input)
        {
            if (input == "test-directory")
            {
                Directory.Delete(Path.GetDirectoryName(inputs["test-assembly"])!, recursive: true);
                return;
            }

            File.Delete(inputs[input]);
        }

        /// <summary>テストの実行体が成果物を書く directory への file の追加</summary>
        public void WriteTestResult()
        {
            var results = Path.Combine(Path.GetDirectoryName(inputs["test-assembly"])!, "TestResults");
            Directory.CreateDirectory(results);
            File.WriteAllText(Path.Combine(results, "run.log"), "log");
        }

        /// <summary>csc の引数にも、テストの directory にも現れない file の追加</summary>
        public void WriteUnrelatedFile() => File.WriteAllText(Path.Combine(root, "src", "README.md"), "unrelated");

        /// <summary>一つの入力の変わった行の範囲</summary>
        public ChangedLines Changes(string input, LineRange range) =>
            new(new Dictionary<string, IReadOnlyList<LineRange>> { [inputs[input]] = [range] });

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(root, recursive: true);

        /// <summary>この環境の csc 呼び出し</summary>
        private CscInvocation Invocation(string? extraArgument)
        {
            var arguments = new List<string>
            {
                $"/reference:{inputs["reference"]}",
                $"/analyzer:{inputs["analyzer"]}",
                $"/additionalfile:{inputs["additional-file"]}",
                $"/analyzerconfig:{inputs["analyzer-config"]}",
                $"/resource:{inputs["resource"]},Target.strings",
                "/out:obj/Target.dll",
            };
            if (extraArgument is not null)
            {
                arguments.Add(extraArgument);
            }

            arguments.Add(inputs["source"]);
            arguments.Add(inputs["other-source"]);
            return new CscInvocation(Path.Combine(root, "src", "Target.csproj"), arguments, Path.Combine(root, "src", "obj", "Target.dll"));
        }
    }
}
