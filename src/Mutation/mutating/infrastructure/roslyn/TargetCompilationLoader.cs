using Mutation.Mutating.Domain;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>csc 引数からの変異対象コンパイルの再構築</summary>
public static class TargetCompilationLoader
{
    /// <summary>csc 呼び出しの解析と、生成コードも織り込んだコンパイルの組み立て</summary>
    /// <param name="invocation">binlog から回収した対象 project の csc 呼び出し</param>
    /// <returns>変異対象のコンパイル入力一式</returns>
    public static LoadedTarget Load(CscInvocation invocation)
    {
        var baseDirectory = Path.GetDirectoryName(invocation.ProjectFile) ?? ".";
        var arguments = CSharpCommandLineParser.Default.Parse(
            invocation.Arguments,
            baseDirectory,
            sdkDirectory: null
        );
        var parseOptions = arguments.ParseOptions.WithDocumentationMode(DocumentationMode.None);
        var sourceTrees = arguments
            .SourceFiles.AsParallel()
            .AsOrdered()
            .Select(file => ParseFile(file.Path, parseOptions))
            .ToList();
        var references = arguments
            .MetadataReferences.Select(reference =>
                (MetadataReference)MetadataReference.CreateFromFile(reference.Reference, reference.Properties)
            )
            .ToList();
        var options = arguments
            .CompilationOptions.WithGeneralDiagnosticOption(ReportDiagnostic.Suppress)
            .WithSpecificDiagnosticOptions(ImmutableDictionary<string, ReportDiagnostic>.Empty)
            .WithConcurrentBuild(true)
            .WithStrongNameProvider(new DesktopStrongNameProvider());
        var compilation = CSharpCompilation.Create(arguments.CompilationName, sourceTrees, references, options);
        compilation = GeneratorExecution.Append(compilation, arguments, parseOptions);
        return new LoadedTarget(arguments, compilation, sourceTrees, parseOptions, invocation.OutputPath);
    }

    /// <summary>一 file の読み込みと構文解析</summary>
    private static SyntaxTree ParseFile(string path, CSharpParseOptions parseOptions)
    {
        using var stream = File.OpenRead(path);
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(stream), parseOptions, path);
        var annotated = StaticContextRewriter.Annotate(tree.GetRoot());
        return ReferenceEquals(annotated, tree.GetRoot()) ? tree : tree.WithRootAndOptions(annotated, parseOptions);
    }
}
