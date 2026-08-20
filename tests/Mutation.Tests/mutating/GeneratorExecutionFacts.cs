using Mutation.Mutating.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using TUnit.Core;

namespace Mutation.Tests;

/// <summary>source generator 再実行の検査</summary>
public sealed class GeneratorExecutionFacts
{
    private const string GeneratorSource = """
        using Microsoft.CodeAnalysis;

        [Generator]
        public sealed class MarkerGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context) =>
                context.RegisterPostInitializationOutput(c =>
                    c.AddSource("marker.g.cs", "public static class GeneratedMarker { }")
                );
        }
        """;

    /// <summary>analyzer 参照の generator が生成コードをコンパイルへ足すこと</summary>
    [Test]
    public async Task Generator_output_is_appended()
    {
        var generatorDll = EmitGenerator();
        var parseOptions = CSharpParseOptions.Default;
        var arguments = CSharpCommandLineParser.Default.Parse(
            [$"/analyzer:{generatorDll}", "dummy.cs"],
            Path.GetDirectoryName(generatorDll),
            sdkDirectory: null
        );
        var tree = CSharpSyntaxTree.ParseText("public class Handwritten { }", parseOptions);
        var compilation = CSharpCompilation.Create(
            "GeneratorTarget",
            [tree],
            Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var updated = GeneratorExecution.Append(compilation, arguments, parseOptions);
        await Assert.That(updated.SyntaxTrees.Length).IsEqualTo(2);
        await Assert.That(updated.GetTypeByMetadataName("GeneratedMarker")).IsNotNull();
    }

    /// <summary>generator のない analyzer 参照ではコンパイルが変わらないこと</summary>
    [Test]
    public async Task Missing_generator_leaves_compilation_unchanged()
    {
        var parseOptions = CSharpParseOptions.Default;
        var arguments = CSharpCommandLineParser.Default.Parse(
            ["/analyzer:does-not-exist.dll", "dummy.cs"],
            Directory.CreateTempSubdirectory("generator-facts").FullName,
            sdkDirectory: null
        );
        var tree = CSharpSyntaxTree.ParseText("public class Handwritten { }", parseOptions);
        var compilation = CSharpCompilation.Create(
            "GeneratorTarget",
            [tree],
            Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var updated = GeneratorExecution.Append(compilation, arguments, parseOptions);
        await Assert.That(ReferenceEquals(updated, compilation)).IsTrue();
    }

    private static string EmitGenerator()
    {
        var compilation = CSharpCompilation.Create(
            "MarkerGenerator",
            [CSharpSyntaxTree.ParseText(GeneratorSource)],
            Snippet.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        var directory = Directory.CreateTempSubdirectory("generator-facts").FullName;
        var path = Path.Combine(directory, "MarkerGenerator.dll");
        var emitted = compilation.Emit(path);
        if (!emitted.Success)
        {
            var errors = string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"generator のコンパイルが失敗した:\n{errors}");
        }

        return path;
    }
}
