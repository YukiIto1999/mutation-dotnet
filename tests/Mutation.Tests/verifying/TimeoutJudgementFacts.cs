using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>期限の超過を競合による遅れと見なす基準の検査</summary>
public sealed class TimeoutJudgementFacts
{
    /// <summary>変異のない対照の実行が予算に収まれば、期限の超過を競合のせいにしないこと</summary>
    /// <param name="controlMs">対照の実行の所要時間</param>
    /// <param name="budgetMs">変異一件の実行時間予算</param>
    /// <returns>判定を待つ非同期検査</returns>
    [Test]
    [Arguments(1_000.0, 4_000.0)]
    [Arguments(4_000.0, 4_000.0)]
    public async Task A_control_run_within_the_budget_leaves_the_timeout_unexplained(double controlMs, double budgetMs)
    {
        await Assert.That(TimeoutJudgement.Contention(controlMs, budgetMs)).IsNull();
    }

    /// <summary>変異のない対照の実行が予算を超えれば、競合による遅れとして対照の所要時間と予算を残すこと</summary>
    /// <returns>判定を待つ非同期検査</returns>
    [Test]
    public async Task A_control_run_over_the_budget_is_contention()
    {
        await Assert.That(TimeoutJudgement.Contention(4_001.0, 4_000.0))
            .IsEqualTo(new UnjudgedCause.ContendedTimeout(4_001.0, 4_000.0));
    }

    /// <summary>変異のない対照の実行が終わらなければ、競合による遅れとして所要時間を持たずに残すこと</summary>
    /// <returns>判定を待つ非同期検査</returns>
    [Test]
    public async Task A_control_run_that_did_not_finish_is_contention()
    {
        await Assert.That(TimeoutJudgement.Contention(null, 4_000.0)).IsEqualTo(new UnjudgedCause.ContendedTimeout(null, 4_000.0));
    }
}
