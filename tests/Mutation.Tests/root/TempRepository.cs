using Mutation.Mutating.Infrastructure.Git;
using System.Diagnostics;

namespace Mutation.Tests;

public sealed partial class ChangedLinesGateFacts
{
    /// <summary>テストごとに作り、終わりに消す一時 git repository</summary>
    private sealed class TempRepository : IDisposable
    {
        /// <summary>repository の root の絶対 path</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"mutation-tests-{Guid.NewGuid():N}");

        /// <summary>空の repository の作成</summary>
        public static async Task<TempRepository> CreateAsync()
        {
            var repo = new TempRepository();
            Directory.CreateDirectory(repo.Root);
            await repo.GitAsync("init", "-q");
            return repo;
        }

        /// <summary>Killed の変異を検出したことにするテスト名</summary>
        private static readonly string[] KillerTests = ["Suite.Test"];

        /// <summary>root からの相対 path の絶対化</summary>
        public string PathOf(string relative) => Path.GetFullPath(Path.Combine(Root, relative));

        /// <summary>root からの相対 path へのファイルの書き込み</summary>
        public async Task WriteAsync(string relative, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relative))!);
            await File.WriteAllTextAsync(PathOf(relative), content);
        }

        /// <summary>作業木の全体を hook を起こさない commit-tree で commit した、その commit の id</summary>
        public async Task<string> CommitAllAsync()
        {
            await GitAsync("add", "-A");
            var tree = (await GitAsync("write-tree")).Trim();
            return (await GitAsync("commit-tree", "-m", "base", tree)).Trim();
        }

        /// <summary>root を projectRoot にした互換 schema の報告の書き込みと、その path</summary>
        public async Task<string> WriteReportAsync(params (string File, string Id, int Line, string Status)[] mutants)
        {
            var files = mutants
                .GroupBy(mutant => mutant.File)
                .ToDictionary(
                    group => group.Key,
                    group => new
                    {
                        language = "cs",
                        source = "",
                        mutants = group.Select(mutant => new
                        {
                            id = mutant.Id,
                            mutatorName = "LiteralMutator",
                            replacement = "x",
                            location = new
                            {
                                start = new { line = mutant.Line, column = 1 },
                                end = new { line = mutant.Line, column = 2 },
                            },
                            status = mutant.Status,
                            killedBy = mutant.Status == "Killed" ? KillerTests : [],
                        }).ToArray(),
                    }
                );
            var path = PathOf("out/mutation-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new { projectRoot = Root, files }));
            return path;
        }

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Root, recursive: true);

        /// <summary>root での git の実行と標準出力。失敗したら例外</summary>
        private async Task<string> GitAsync(params string[] arguments)
        {
            var startInfo = GitLocator.StartInfo(Root);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["GIT_AUTHOR_NAME"] = "fixture";
            startInfo.Environment["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid";
            startInfo.Environment["GIT_COMMITTER_NAME"] = "fixture";
            startInfo.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid";

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? await output
                : throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {await error}");
        }
    }
}
