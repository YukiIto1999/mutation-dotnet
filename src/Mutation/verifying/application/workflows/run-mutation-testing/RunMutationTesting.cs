using Mutation.Shared;
using Mutation.Verifying.Domain;
using TypeModeling.Domain;

namespace Mutation.Verifying.Application;

/// <summary>build から報告書き出しまでの一連の変異検査を貫く workflow</summary>
/// <param name="mutantSource">変異一式の提供の port</param>
/// <param name="verifyMutants">判定確定の workflow</param>
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
    private readonly TargetRun targetRun = new(mutantSource, verifyMutants, workerRuns, snapshots);

    /// <summary>全対象の全段階の実行による確定結果の算出。成功時は報告も書き出す</summary>
    /// <param name="request">一回の変異検査の入力</param>
    /// <param name="progress">進行を伝える通知先</param>
    /// <returns>成功なら確定結果、失敗なら中断理由</returns>
    public async Task<Result<MutationRunResult, PipelineFailure>> ExecuteAsync(
        RunRequest request,
        Action<RunProgress> progress
    )
    {
        var clock = new PhaseClock();
        var layout = new RunLayout(request.OutputDirectory);
        var located = await mutantSource
            .PrepareAsync(
                request.Targets,
                layout.WorkDirectory,
                request.WithBaseline ? request.FingerprintSettings : null
            )
            .ConfigureAwait(false);
        var buildMs = clock.EndPhase();
        progress(new RunProgress.BuildCompleted(buildMs));
        if (
            located
            is Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>.Failed(
                var buildFailure
            )
        )
        {
            return new Result<MutationRunResult, PipelineFailure>.Failed(buildFailure);
        }

        var targets = (
            (Result<IReadOnlyList<Result<PreparedTarget, PipelineFailure>>, PipelineFailure>.Succeeded)located
        ).Value;
        var session = new PipelineSession(request, layout, progress, clock);
        var outcomes = await RunEachAsync(session, targets, TargetNames(request.Targets)).ConfigureAwait(false);
        var result = new MutationRunResult(outcomes, MutationRunResult.Aggregate(outcomes, buildMs, clock.TotalMs));
        reports.Write(result, ReportRoot(request.Targets), layout.ReportsDirectory);
        return new Result<MutationRunResult, PipelineFailure>.Succeeded(result);
    }

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
        if (target is Result<PreparedTarget, PipelineFailure>.Failed prepareFailure)
        {
            return Abandon(session, name, prepareFailure.Failure, 0);
        }

        var prepared = ((Result<PreparedTarget, PipelineFailure>.Succeeded)target).Value;
        var outcome = await targetRun.ExecuteAsync(session, prepared, name).ConfigureAwait(false);
        return outcome switch
        {
            Result<TargetResult, PipelineFailure>.Succeeded succeeded => new TargetOutcome.Completed(succeeded.Value),
            Result<TargetResult, PipelineFailure>.Failed failed => Abandon(
                session,
                name,
                failed.Failure,
                session.Clock.EndPhase()
            ),
        };
    }

    /// <summary>置き場と報告で対象を指す名前の決定。重複は連番の付加による一意化</summary>
    private static string[] TargetNames(IReadOnlyList<TargetRequest> targets)
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
    private static string ReportRoot(IReadOnlyList<TargetRequest> targets)
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
    private static string[] Segments(TargetRequest target) =>
        (Path.GetDirectoryName(target.ProjectPath) ?? ".").Split(Path.DirectorySeparatorChar);
}
