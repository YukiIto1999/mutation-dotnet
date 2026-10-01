using Mutation.Mutating.Infrastructure.Git;
using System.Diagnostics;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>git の起動情報が、git の hook の中で export された呼び出し元の repository を子へ渡さないことの検査</summary>
public sealed class GitLocatorFacts
{
    /// <summary>git 自身が repository の位置を決めるとする全ての環境変数が外れ、それ以外の環境変数は残ること</summary>
    [Test]
    public async Task Repository_location_variables_are_removed_and_others_are_kept()
    {
        var locationVariables = await LocalEnvironmentVariablesAsync();
        var startInfo = new ProcessStartInfo();
        foreach (var name in locationVariables)
        {
            startInfo.Environment[name] = "/elsewhere";
        }

        startInfo.Environment["GIT_AUTHOR_NAME"] = "fixture";

        GitLocator.WithoutRepositoryLocation(startInfo);

        await Assert.That(locationVariables).Contains("GIT_DIR");
        await Assert.That(locationVariables.Where(startInfo.Environment.ContainsKey).ToArray()).IsEmpty();
        await Assert.That(startInfo.Environment["GIT_AUTHOR_NAME"]).IsEqualTo("fixture");
    }

    /// <summary>`git rev-parse --local-env-vars` が並べる、repository の位置を決める環境変数の名前</summary>
    private static async Task<string[]> LocalEnvironmentVariablesAsync()
    {
        var startInfo = GitLocator.StartInfo(Path.GetTempPath());
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--local-env-vars");
        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
