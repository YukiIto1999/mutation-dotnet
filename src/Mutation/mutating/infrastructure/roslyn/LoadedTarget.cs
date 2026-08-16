using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mutation.Mutating.Infrastructure.Roslyn;

/// <summary>変異対象 project のコンパイル入力一式</summary>
/// <param name="Arguments">binlog から回収した csc 引数の解析結果</param>
/// <param name="OriginalCompilation">生成コード込みで元ソースを束ねたコンパイル</param>
/// <param name="SourceTrees">変異対象となる手書きソースの構文木</param>
/// <param name="ParseOptions">対象と同じ言語設定の構文解析設定</param>
/// <param name="OriginalOutputPath">dotnet build が出力した元 assembly の path</param>
public sealed record LoadedTarget(
    CSharpCommandLineArguments Arguments,
    CSharpCompilation OriginalCompilation,
    IReadOnlyList<SyntaxTree> SourceTrees,
    CSharpParseOptions ParseOptions,
    string OriginalOutputPath
);
