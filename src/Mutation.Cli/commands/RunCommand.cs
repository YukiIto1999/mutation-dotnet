


using Mutation.Shared;
using Mutation.Verifying.Domain;
using Mutation.Composition;
using TypeModeling.Domain;

namespace Mutation.Cli.Commands;

/// <summary>変異検査を実行する command</summary>
public static class RunCommand
{
    /// <summary>対象 project の変異検査の、最初から最後までの実行</summary>
    /// <param name="project">-p, 変異対象 project の csproj の path</param>
    /// <param name="testProject">-t, テスト project の csproj の path</param>
    /// <param name="configuration">build 構成の名前</param>
    /// <param name="concurrency">同時に走らせる worker 数。0 なら論理コア数の半分</param>
    /// <param name="output">報告と中間物を置く directory</param>
    /// <param name="validateSurvivors">生存した変異の新規 process での再検証</param>
    /// <param name="mutate">-m, 変異対象に含めるファイルの glob。project directory 相対。先頭 `!` は除外。複数は `,` 区切り</param>
    /// <param name="since">差分運用の基点になる git の参照。変更ファイルだけが対象</param>
    /// <param name="breakAt">この値を下回る mutation score で終了コード 2 にする。0〜100</param>
    /// <param name="ignoreOperators">除外する変異演算子の名前。複数は `,` 区切り</param>
    /// <param name="ignoreMethods">その呼び出しの中を変異させない method 名。複数は `,` 区切り</param>
    /// <param name="excludeStatic">static 初期化でしか実行されない変異を対象外にする(Ignored として報告)</param>
    /// <param name="withBaseline">前回実行の保存からの、変わっていない変異の判定の継承</param>
    /// <returns>成功なら 0、失敗なら 1、score が break-at 未満なら 2</returns>
    public static async Task<int> Run(
        string project,
        string testProject,
        string configuration = "Debug",
        int concurrency = 0,
        string output = ".mutation-output",
        bool validateSurvivors = false,
        string[]? mutate = null,
        string? since = null,
        double breakAt = -1,
        string[]? ignoreOperators = null,
        string[]? ignoreMethods = null,
        bool excludeStatic = false,
        bool withBaseline = false
    )
    {
        var resolved = concurrency > 0 ? concurrency : Math.Max(1, Environment.ProcessorCount / 2);
        var options = new MutationRunOptions(
            Path.GetFullPath(project),
            Path.GetFullPath(testProject),
            configuration,
            resolved,
            validateSurvivors,
            excludeStatic,
            mutate ?? [],
            since,
            ignoreOperators ?? [],
            ignoreMethods ?? [],
            Path.GetFullPath(output),
            withBaseline
        );
        var outcome = await BuildCore
            .Create()
            .RunAsync(options, p => Console.Error.WriteLine(ProgressLines.Describe(p)))
            .ConfigureAwait(false);
        if (outcome is Result<MutationRunResult, PipelineFailure>.Failed(var failure))
        {
            await Console.Error.WriteLineAsync(FailureLines.Describe(failure)).ConfigureAwait(false);
            return 1;
        }

        var result = ((Result<MutationRunResult, PipelineFailure>.Succeeded)outcome).Value;
        await Console.Out.WriteAsync(ConsoleSummary.Render(result)).ConfigureAwait(false);
        return await ExitCodeAsync(result, breakAt).ConfigureAwait(false);
    }

    /// <summary>--break-at と score の突き合わせによる終了コードの決定</summary>
    private static async Task<int> ExitCodeAsync(MutationRunResult result, double breakAt)
    {
        if (breakAt < 0 || result.Score is not { } score || score * 100 >= breakAt)
        {
            return 0;
        }

        await Console.Error.WriteLineAsync(
            $"mutation score {score * 100:F2}% が --break-at {breakAt:F2} を下回った"
        ).ConfigureAwait(false);
        return 2;
    }
}
