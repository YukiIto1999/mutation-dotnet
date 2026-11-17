using ProjectReferenceGraph = Microsoft.Build.Logging.StructuredLogger.ProjectReferenceGraph;
using Microsoft.CodeAnalysis.CSharp;
using Mutation.Mutating.Domain;

namespace Mutation.Mutating.Infrastructure.Msbuild;

/// <summary>MSBuild の project 参照と compiler 入力からの対象依存関係の導出</summary>
/// <remarks>参照関係の解釈は StructuredLogger が所有し、assembly 名や出力 directory の推測で代用しない</remarks>
internal static class TargetGraphs
{
    /// <summary>対象とテストと、それらが参照する project の入力の組み立て</summary>
    /// <param name="sut">対象 project の csc 呼び出し</param>
    /// <param name="test">テスト project の csc 呼び出し</param>
    /// <param name="build">build に含まれた全 project の csc 呼び出し</param>
    /// <param name="references">MSBuild の評価から読み取った project 参照</param>
    /// <returns>対象とテストと参照する project の入力</returns>
    public static TargetGraph Build(
        CscInvocation sut,
        CscInvocation test,
        IEnumerable<CscInvocation> build,
        ProjectReferenceGraph references
    )
    {
        var all = build.ToArray();
        var referenced = ReferencedProjects(references, sut.ProjectFile, test.ProjectFile);
        var upstream = all
            .Where(invocation => referenced.Contains(invocation.ProjectFile))
            .Select(Inputs)
            .ToArray();
        referenced.ExceptWith(all.Select(invocation => invocation.ProjectFile));
        return new TargetGraph(
            Inputs(sut),
            Inputs(test),
            upstream,
            [.. referenced.Order(StringComparer.Ordinal)]
        );
    }

    /// <summary>project の変更判定用入力の抽出</summary>
    /// <param name="invocation">binlog に記録された compiler 呼出し</param>
    /// <returns>ソースとそれ以外の生成入力</returns>
    private static ProjectInputs Inputs(CscInvocation invocation)
    {
        var baseDirectory = Path.GetDirectoryName(invocation.ProjectFile) ?? ".";
        var arguments = CSharpCommandLineParser.Default.Parse(invocation.Arguments, baseDirectory, sdkDirectory: null);
        var sources = arguments.SourceFiles.Select(file => Path.GetFullPath(file.Path)).ToHashSet(StringComparer.Ordinal);
        var others = InputClosure
            .CompileInputs(arguments, invocation.Arguments, baseDirectory)
            .Where(path => !sources.Contains(path))
            .ToArray();
        return new ProjectInputs(invocation.ProjectFile, [.. sources.Order(StringComparer.Ordinal)], others);
    }

    /// <summary>対象とテストから辿れる推移的な project 参照の列挙</summary>
    /// <param name="references">MSBuild の project graph</param>
    /// <param name="sut">対象 project の絶対 path</param>
    /// <param name="test">テスト project の絶対 path</param>
    /// <returns>対象とテスト自身を除く参照先 project の絶対 path</returns>
    private static HashSet<string> ReferencedProjects(ProjectReferenceGraph references, string sut, string test)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([sut, test]);
        while (pending.TryPop(out var path))
        {
            if (!visited.Add(path) || references.Graph.TryFindVertex(path) is not { } vertex)
            {
                continue;
            }

            foreach (var reference in vertex.Outgoing)
            {
                pending.Push(Path.GetFullPath(reference.Value));
            }
        }

        visited.Remove(sut);
        visited.Remove(test);
        return visited;
    }
}
