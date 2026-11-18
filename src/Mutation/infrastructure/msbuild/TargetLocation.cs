

using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Infrastructure.Msbuild;

/// <summary>binlog からの変異対象とテスト assembly の特定</summary>
public static class TargetLocation
{
    /// <summary>対象 project の csc 呼び出しとテスト assembly の実行時 path と、build が示す project の入力の特定</summary>
    /// <param name="binlogPath">読み取る binlog の path</param>
    /// <param name="projects">対象とテストの project の特定</param>
    /// <returns>成功なら対象の csc 呼び出しとテスト assembly の path と、対象とテストと参照する project の入力</returns>
    public static Result<LocatedTarget, PipelineFailure> Locate(string binlogPath, TargetProjects projects)
    {
        var (invocations, references) = BinlogCompilations.Read(binlogPath);
        if (!invocations.TryGetValue(Path.GetFullPath(projects.ProjectPath), out var sut))
        {
            return Result.Failed<PipelineFailure>(new PipelineFailure.CompilerArgumentsNotFound(projects.ProjectPath));
        }

        if (!invocations.TryGetValue(Path.GetFullPath(projects.TestProjectPath), out var test))
        {
            return Result.Failed<PipelineFailure>(
                new PipelineFailure.CompilerArgumentsNotFound(projects.TestProjectPath)
            );
        }

        var testAssembly = ToBinPath(test.OutputPath);
        if (!File.Exists(testAssembly))
        {
            return Result.Failed<PipelineFailure>(new PipelineFailure.NoTestsFound(testAssembly));
        }

        return Result.Succeeded(new LocatedTarget(sut, testAssembly, TargetGraphs.Build(sut, test, invocations.Values, references)));
    }

    /// <summary>obj 配下の出力 path から実行時の bin 配下の path への写像</summary>
    private static string ToBinPath(string objOutputPath)
    {
        var marker = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var index = objOutputPath.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return objOutputPath;
        }

        return string.Concat(
            objOutputPath.AsSpan(0, index),
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            objOutputPath.AsSpan(index + marker.Length)
        );
    }
}
