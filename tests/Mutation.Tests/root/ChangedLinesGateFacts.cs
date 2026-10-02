using Mutation.Composition;
using Mutation.Mutating.Infrastructure.Git;
using Mutation.Shared;
using Mutation.Verifying.Domain;
using System.Diagnostics;
using TUnit.Core;
using TypeModeling.Domain;

namespace Mutation.Tests;

/// <summary>一時 git repository の作業木と報告からの、変わった行の判定の組み立て済み経路の検査</summary>
public sealed partial class ChangedLinesGateFacts
{
    /// <summary>書き換えた行と未追跡ファイルの変異だけが選ばれ、そのうち生存と未被覆が未検出になること</summary>
    [Test]
    public async Task Rewritten_and_untracked_lines_select_their_undetected_mutants()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/A.cs", "one\ntwo\nthree\n");
        var baseRef = await repo.CommitAllAsync();
        await repo.WriteAsync("src/A.cs", "one\nchanged\nthree\n");
        await repo.WriteAsync("src/sub/New.cs", "fresh\n");
        var report = await repo.WriteReportAsync(
            ("src/A.cs", "A:1", 1, "Survived"),
            ("src/A.cs", "A:2", 2, "Survived"),
            ("src/A.cs", "A:2k", 2, "Killed"),
            ("src/A.cs", "A:3", 3, "NoCoverage"),
            ("src/sub/New.cs", "New:1", 1, "NoCoverage")
        );

        var outcome = BuildCore.Create().OnChangedLines(report, baseRef);

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Succeeded).IsTrue();
        var verdicts = ((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)outcome).Value;
        await Assert.That(verdicts.OnChangedLines.Select(m => m.Id).ToArray()).IsEquivalentTo(["A:2", "A:2k", "New:1"]);
        await Assert.That(verdicts.Undetected.Select(m => m.Id).ToArray()).IsEquivalentTo(["A:2", "New:1"]);
    }

    /// <summary>`--since` の選別が、subdirectory から解いても内容の変わったファイルと未追跡ファイルだけを返すこと</summary>
    [Test]
    public async Task Since_selects_changed_and_untracked_files_from_a_subdirectory()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/A.cs", "one\n");
        await repo.WriteAsync("src/sub/Same.cs", "same\n");
        var baseRef = await repo.CommitAllAsync();
        await repo.WriteAsync("src/A.cs", "two\n");
        await repo.WriteAsync("src/sub/New.cs", "fresh\n");

        var resolved = new ChangeSets().Resolve(Path.Combine(repo.Root, "src", "sub"), baseRef);

        await Assert.That(resolved is Result<ChangedLines, PipelineFailure>.Succeeded).IsTrue();
        var files = ((Result<ChangedLines, PipelineFailure>.Succeeded)resolved).Value.Files;
        await Assert.That(files.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo([repo.PathOf("src/A.cs"), repo.PathOf("src/sub/New.cs")]);
    }

    /// <summary>先行する挿入と削除で行番号がずれても新しい側の変更行を選ぶこと</summary>
    [Test]
    public async Task Shifted_hunks_select_new_side_lines_not_old_coordinates()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/A.cs", "first\nremoved\nthird\nfourth\nfifth\n");
        var baseRef = await repo.CommitAllAsync();
        await repo.WriteAsync("src/A.cs", "inserted\nfirst\nthird\nfourth\nchanged\nfifth\n");
        var report = await repo.WriteReportAsync(
            ("src/A.cs", "inserted", 1, "Survived"),
            ("src/A.cs", "old-coordinate", 4, "Survived"),
            ("src/A.cs", "changed", 5, "NoCoverage"),
            ("src/A.cs", "unchanged", 6, "Survived")
        );

        var outcome = BuildCore.Create().OnChangedLines(report, baseRef);

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Succeeded).IsTrue();
        var verdicts = ((Result<ChangedLineVerdicts, PipelineFailure>.Succeeded)outcome).Value;
        await Assert.That(verdicts.Undetected.Select(mutant => mutant.Id).ToArray())
            .IsEquivalentTo(["inserted", "changed"]);
    }

    /// <summary>解けない基点が、変わった行の無い合格でなく、git の失敗を伝える差分の失敗になること</summary>
    [Test]
    public async Task Unknown_base_ref_fails_instead_of_passing()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/A.cs", "one\n");
        await repo.CommitAllAsync();
        var report = await repo.WriteReportAsync(("src/A.cs", "A:1", 1, "Survived"));

        var outcome = BuildCore.Create().OnChangedLines(report, "no-such-ref");

        var reason = outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(
            PipelineFailure.SinceUnavailable { Reason: var text }
        )
            ? text
            : null;
        await Assert.That(reason).Contains("no-such-ref");
    }

    /// <summary>git の option に見える基点を commit とみなさず失敗にすること</summary>
    [Test]
    public async Task Git_option_is_not_accepted_as_a_base_ref()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/A.cs", "one\n");
        await repo.CommitAllAsync();
        var report = await repo.WriteReportAsync(("src/A.cs", "A:1", 1, "Survived"));

        var outcome = BuildCore.Create().OnChangedLines(report, "--cached");

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(
            PipelineFailure.SinceUnavailable)).IsTrue();
    }

    /// <summary>無い報告が、読めない報告の失敗になること</summary>
    [Test]
    public async Task Missing_report_is_unreadable()
    {
        using var repo = await TempRepository.CreateAsync();

        var outcome = BuildCore.Create().OnChangedLines(repo.PathOf("missing.json"), "HEAD");

        await Assert.That(outcome is Result<ChangedLineVerdicts, PipelineFailure>.Failed(PipelineFailure.ReportUnreadable))
            .IsTrue();
    }

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
