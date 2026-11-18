using Mutation.Domain;
using TypeModeling.Domain;

namespace Mutation.Application;

/// <summary>build から報告書き出しまでの一回の変異検査を所有する use-case</summary>
/// <param name="mutantSource">変異一式の提供の port</param>
/// <param name="verifyMutants">対象の変異の判定と保存</param>
/// <param name="workerRuns">worker 実行の port</param>
/// <param name="snapshots">保存と継承の port</param>
/// <param name="reports">報告書き出しの port</param>
public sealed class RunMutationTesting(
    IMutantSource mutantSource,
    VerifyMutants verifyMutants,
    IWorkerRuns workerRuns,
    ISnapshots snapshots,
    IVerificationReports reports
)
{
    /// <summary>対象一件の遂行</summary>
    private readonly TargetRun targetRun = new(verifyMutants, workerRuns, snapshots);

    /// <summary>全対象の全段階の実行による確定結果の算出。成功も失敗も報告を書き出す</summary>
    /// <remarks>
    /// 取り消しの要求を受けて止まった走行も、判定を確定した変異を報告に残し、止まったことを報告の run に書く
    /// 全対象に及ぶ準備の失敗は、前の走行の報告を失敗した走行の状態で置き換えてから、その失敗のまま返す
    /// 準備の後に取り消しの要求があれば、その失敗より取り消しを優先し、全対象を中断した確定結果を返す
    /// </remarks>
    /// <param name="request">一回の変異検査の公開入力</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <param name="cancellation">取り消しの要求</param>
    /// <returns>成功なら確定結果、失敗なら中断理由</returns>
    public async Task<Result<MutationRunResult, PipelineFailure>> ExecuteAsync(
        RunRequest request,
        Action<RunProgress> progress,
        CancellationToken cancellation = default
    )
    {
        var clock = new PhaseClock();
        var layout = new RunLayout(request.OutputDirectory, request.Selection);
        var host = workerRuns.Host;
        var located = await mutantSource
            .PrepareAsync(request.Targets, request.Selection, layout.WorkDirectory, request.Execution, host)
            .ConfigureAwait(false);
        var buildMs = clock.EndPhase();
        progress(new RunProgress.BuildCompleted(buildMs));
        if (located.IsFailed(out var preparationFailure, out var targets))
        {
            var cancelled = cancellation.IsCancellationRequested;
            PipelineFailure cause = cancelled ? new PipelineFailure.Interrupted() : preparationFailure.Failure;
            var abandoned = Record(request, layout, AbandonAll(request.Targets, cause), buildMs, clock);
            if (cancelled)
            {
                return Result.Succeeded(abandoned);
            }

            return preparationFailure;
        }

        var session = new PipelineSession(request, layout, host, progress, clock, cancellation);
        var outcomes = await RunEachAsync(session, targets, TargetNames(request.Targets)).ConfigureAwait(false);
        return Result.Succeeded(Record(request, layout, outcomes, buildMs, clock));
    }

    /// <summary>対象ごとの帰結の確定結果への束ねと、報告への書き出し。前の走行の報告は、この走行の報告で置き換わる</summary>
    private MutationRunResult Record(
        RunRequest request,
        RunLayout layout,
        IReadOnlyList<TargetOutcome> outcomes,
        double buildMs,
        PhaseClock clock
    )
    {
        var result = new MutationRunResult(outcomes, MutationRunResult.Aggregate(outcomes, buildMs, clock.TotalMs));
        reports.Write(result, ReportRoot(request.Targets), layout.ReportsDirectory);
        return result;
    }

    /// <summary>全対象に及ぶ準備の失敗の、全対象が判定に至らなかった帰結の列への展開。理由は全対象で同じ</summary>
    private static IReadOnlyList<TargetOutcome> AbandonAll(IReadOnlyList<TargetProjects> targets, PipelineFailure cause) =>
        [.. TargetNames(targets).Select(name => new TargetOutcome.Failed(name, cause, 0))];

    /// <summary>対象を順に検査しての帰結の収集。一件の中断は残りの検査を妨げない形</summary>
    private async Task<IReadOnlyList<TargetOutcome>> RunEachAsync(
        PipelineSession session,
        IReadOnlyList<Result<PreparedTarget, PipelineFailure>> targets,
        string[] names
    )
    {
        var outcomes = new List<TargetOutcome>(targets.Count);
        for (var index = 0; index < targets.Count; index++)
        {
            session.Progress(new RunProgress.TargetStarted(names[index], index + 1, targets.Count));
            outcomes.Add(await OutcomeAsync(session, targets[index], names[index]).ConfigureAwait(false));
        }

        return outcomes;
    }

    /// <summary>対象一件の帰結の算出。準備の失敗はそのまま中断として記録</summary>
    private async Task<TargetOutcome> OutcomeAsync(
        PipelineSession session,
        Result<PreparedTarget, PipelineFailure> target,
        string name
    )
    {
        if (target.IsFailed(out var prepareFailure, out var prepared))
        {
            return Abandon(session, name, prepareFailure.Failure, 0);
        }

        if (session.Cancellation.IsCancellationRequested)
        {
            return Abandon(session, name, new PipelineFailure.Interrupted(), 0);
        }

        var outcome = await targetRun.ExecuteAsync(session, prepared, name).ConfigureAwait(false);
        if (outcome.IsFailed(out var runFailure, out var completed))
        {
            return Abandon(session, name, runFailure.Failure, session.Clock.EndPhase());
        }

        return new TargetOutcome.Completed(completed);
    }

    /// <summary>置き場と報告で対象を指す名前の決定。重複は連番の付加による一意化</summary>
    private static string[] TargetNames(IReadOnlyList<TargetProjects> targets)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new string[targets.Count];
        for (var index = 0; index < targets.Count; index++)
        {
            var stem = Path.GetFileNameWithoutExtension(targets[index].ProjectPath);
            var name = stem;
            var suffix = 1;
            while (!used.Add(name))
            {
                suffix++;
                name = $"{stem}-{suffix}";
            }

            names[index] = name;
        }

        return names;
    }

    /// <summary>対象一件の中断の記録。残る対象の検査への影響なし</summary>
    private static TargetOutcome.Failed Abandon(
        PipelineSession session,
        string name,
        PipelineFailure failure,
        double elapsedMs
    )
    {
        session.Progress(new RunProgress.TargetAbandoned(name, failure));
        return new TargetOutcome.Failed(name, failure, elapsedMs);
    }

    /// <summary>報告内の相対 path の基準になる、全対象 project を含む directory</summary>
    private static string ReportRoot(IReadOnlyList<TargetProjects> targets)
    {
        var common = Segments(targets[0]);
        foreach (var target in targets.Skip(1))
        {
            var other = Segments(target);
            var shared = 0;
            while (shared < common.Length && shared < other.Length && common[shared] == other[shared])
            {
                shared++;
            }

            common = common[..shared];
        }

        var root = string.Join(Path.DirectorySeparatorChar, common);
        return root.Length == 0 ? Path.DirectorySeparatorChar.ToString() : root;
    }

    /// <summary>対象 project が居る directory の、区切りで割った要素列</summary>
    private static string[] Segments(TargetProjects target) =>
        (Path.GetDirectoryName(target.ProjectPath) ?? ".").Split(Path.DirectorySeparatorChar);
}
