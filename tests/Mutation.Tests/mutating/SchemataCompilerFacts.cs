using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mutation.Mutating.Domain;
using Mutation.Mutating.Infrastructure.Roslyn;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>実際のコンパイルによる変異成果物の成功と失敗の検査</summary>
public sealed class SchemataCompilerFacts
{
    /// <summary>成功の成果物が元の挙動を実行できることの検査</summary>
    /// <returns>書き出した assembly の実行結果の検査</returns>
    [Test]
    public async Task Successful_compilation_produces_an_executable_original_path()
    {
        var directory = Directory.CreateTempSubdirectory("schemata-compile");
        try
        {
            var target = Target("public static class Number { public static int Value() => 7; }", directory.FullName);

            var outcome = SchemataCompiler.Compile(target, directory.FullName, MutationPolicy.Everything);

            await Assert.That(outcome.IsFailed(out _, out var artifact)).IsFalse();
            var assembly = System.Reflection.Assembly.Load(await File.ReadAllBytesAsync(artifact!.MutatedAssemblyPath));
            var control = assembly.GetType($"{MutantSwitchSource.Namespace}.{MutantSwitchSource.ClassName}")!;
            control.GetMethod("Activate")!.Invoke(null, [-1, long.MaxValue]);
            var value = assembly.GetType("Number")!.GetMethod("Value")!.Invoke(null, null);
            await Assert.That(value).IsEqualTo(7);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>解決できない元ソースから有効な成果物を装わず診断材料を返すことの検査</summary>
    /// <returns>コンパイラ診断とソースを参照できる失敗の検査</returns>
    [Test]
    public async Task Compiler_errors_publish_diagnostics_and_sources_instead_of_an_assembly()
    {
        var directory = Directory.CreateTempSubdirectory("schemata-failed");
        try
        {
            var target = Target("public static class Broken { public static int Value() => missing; }", directory.FullName);

            var outcome = SchemataCompiler.Compile(target, directory.FullName, MutationPolicy.Everything);

            await Assert.That(outcome.IsFailed(out var failed, out _)).IsTrue();
            await Assert.That(failed.Failure is PipelineFailure.CompileFailed).IsTrue();
            var diagnostics = ((PipelineFailure.CompileFailed)failed.Failure).Diagnostics;
            await Assert.That(diagnostics).Contains("CS0103");
            await Assert.That(diagnostics).Contains(Path.Combine(directory.FullName, "failed-sources"));
            await Assert.That(File.Exists(Path.Combine(directory.FullName, "failed-sources", "Subject.cs"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory.FullName, "Subject.dll"))).IsFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>独立した出力先を持つコンパイル入力の組み立て</summary>
    /// <param name="source">成功またはコンパイラ診断を生じる対象ソース</param>
    /// <param name="directory">検査が所有する出力先</param>
    /// <returns>同じ source と参照を持つ元コンパイル付きの入力</returns>
    private static LoadedTarget Target(string source, string directory)
    {
        var options = CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(source, options, path: Path.Combine(directory, "Subject.cs"));
        var arguments = CSharpCommandLineParser.Default.Parse(
            ["/target:library", "/out:Subject.dll"], directory, sdkDirectory: null);
        var compilation = CSharpCompilation.Create(
            $"CompilerFacts{Guid.NewGuid():N}", [tree], Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new LoadedTarget(arguments, compilation, [tree], options, Path.Combine(directory, "Subject.dll"));
    }
}
