using Mutation.Domain;
using Mutation.Infrastructure;

namespace Mutation.Tests;

public sealed partial class CompilationFingerprintFacts
{
    /// <summary>csc の引数が指す実 file と、テスト assembly の directory と、worker の実体を持つ検査環境</summary>
    private sealed class ClosureProject : IDisposable
    {
        /// <summary>この環境の一時 directory</summary>
        private readonly string root = Directory.CreateTempSubdirectory("mutation-closure-facts").FullName;

        /// <summary>入力の名前から実 file の絶対 path への対応</summary>
        private readonly Dictionary<string, string> inputs;

        /// <summary>engine 自身が成果物を書く directory の絶対 path</summary>
        private readonly string ownedDirectory;

        /// <summary>実行時のテスト assembly を置く directory</summary>
        public string TestOutputDirectory => Path.GetDirectoryName(inputs["test-assembly"])!;

        /// <summary>全入力の file を初期の内容で置いた環境の作成</summary>
        /// <param name="outputInsideTests">engine 自身の directory を、テスト assembly の directory の中に置くか</param>
        public ClosureProject(bool outputInsideTests = false)
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
                ["test-results-data"] = Path.Combine(root, "tests", "bin", "TestResults", "data.json"),
                ["worker-host"] = Path.Combine(root, "host", "dotnet"),
                ["worker-assembly"] = Path.Combine(root, "worker", "Mutation.Worker.dll"),
                ["worker-runtimeconfig"] = Path.Combine(root, "worker", "Mutation.Worker.runtimeconfig.json"),
                ["worker-deps"] = Path.Combine(root, "worker", "Mutation.Worker.deps.json"),
                ["worker-dependency"] = Path.Combine(root, "worker", "Dependency.dll"),
            };
            ownedDirectory = outputInsideTests
                ? Path.Combine(root, "tests", "bin", "engine-output")
                : Path.Combine(root, "out");
            foreach (var (name, path) in inputs)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"initial {name}");
            }
        }

        /// <summary>閉包の同一性と全体の指紋の計算。走行共通の部分の捕捉からやり直す</summary>
        public ReuseIdentity? Compute(
            ReuseSettings? settings = null,
            string? extraArgument = null,
            ChangedLines? changes = null,
            string selection = "",
            string? outputDirectory = null
        )
        {
            var worker = new WorkerHost(inputs["worker-host"], inputs["worker-assembly"]);
            return CompilationFingerprint.Capture(settings ?? Baseline, outputDirectory ?? ownedDirectory, worker) is { } run
                ? CompilationFingerprint.Compute(run, Invocation(extraArgument), inputs["test-assembly"], changes, selection)
                : null;
        }

        /// <summary>入力の file の内容の変更。worker-added-file は worker assembly の directory への file の追加</summary>
        public void Change(string input)
        {
            if (input == "worker-added-file")
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(inputs["worker-assembly"])!, "Added.dll"), "added");
                return;
            }

            File.AppendAllText(inputs[input], " changed");
        }

        /// <summary>入力の file、または test-directory と worker-directory の消去</summary>
        public void Remove(string input)
        {
            switch (input)
            {
                case "test-directory":
                    Directory.Delete(Path.GetDirectoryName(inputs["test-assembly"])!, recursive: true);
                    break;
                case "worker-directory":
                    Directory.Delete(Path.GetDirectoryName(inputs["worker-assembly"])!, recursive: true);
                    break;
                default:
                    File.Delete(inputs[input]);
                    break;
            }
        }

        /// <summary>engine 自身の directory への、実行の成果物の追加</summary>
        public void WriteOwnedOutput()
        {
            var results = Path.Combine(ownedDirectory, "work", "T", "test-results");
            Directory.CreateDirectory(results);
            File.WriteAllText(Path.Combine(results, "run.trx"), "results");
        }

        /// <summary>engine 自身の directory と名前が前方一致するだけの別の directory への、file の追加</summary>
        public void WriteNeighbourOfOwnedDirectory()
        {
            var neighbour = ownedDirectory + "-data";
            Directory.CreateDirectory(neighbour);
            File.WriteAllText(Path.Combine(neighbour, "data.txt"), "neighbour");
        }

        /// <summary>csc の引数にも、テストの出力の directory にも現れない file の追加</summary>
        public void WriteUnrelatedFile() => File.WriteAllText(Path.Combine(root, "src", "README.md"), "unrelated");

        /// <summary>テスト project の directory にあり、テストの出力の directory の外にある source の追加</summary>
        public void WriteTestProjectSource() => File.WriteAllText(Path.Combine(root, "tests", "CalculatorTests.cs"), "class CalculatorTests;");

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
