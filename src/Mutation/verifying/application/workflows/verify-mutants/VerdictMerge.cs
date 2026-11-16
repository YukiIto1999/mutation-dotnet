using Mutation.Shared;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>即時確定、継承、実行の判定を一つの対応に合流させる規則</summary>
internal static class VerdictMerge
{
    /// <summary>後段が前段を上書きする、即時確定・継承・実行の判定の合流</summary>
    /// <param name="compileErrorIds">rollback で無効化された変異の連番</param>
    /// <param name="schedule">実行計画の算出で即時確定した変異</param>
    /// <param name="inherited">前回の保存から継承した判定</param>
    /// <param name="executed">今回の実行が持つ判定</param>
    /// <returns>変異の連番から判定への対応</returns>
    public static Dictionary<MutantId, MutantVerdict> Merge(
        IReadOnlySet<MutantId> compileErrorIds,
        SchedulingOutcome schedule,
        IReadOnlyDictionary<MutantId, MutantVerdict> inherited,
        IReadOnlyDictionary<MutantId, MutantVerdict> executed
    )
    {
        var verdicts = new Dictionary<MutantId, MutantVerdict>();
        foreach (var id in compileErrorIds)
        {
            verdicts[id] = new MutantVerdict.CompileError();
        }

        foreach (var id in schedule.NoCoverageMutants)
        {
            verdicts[id] = new MutantVerdict.NoCoverage();
        }

        foreach (var id in schedule.ExcludedStaticMutants)
        {
            verdicts[id] = new MutantVerdict.Ignored();
        }

        foreach (var (id, verdict) in inherited)
        {
            verdicts[id] = verdict;
        }

        foreach (var (id, verdict) in executed)
        {
            verdicts[id] = verdict;
        }

        return verdicts;
    }
}
