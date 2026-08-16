using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>実行計画の算出で分かれた、実行が要る変異と即時確定の変異</summary>
/// <param name="Plans">テスト実行が要る変異の計画の列</param>
/// <param name="NoCoverageMutants">どのテストにも被覆されず即時確定する変異の連番</param>
/// <param name="ExcludedStaticMutants">static 除外の指定で対象外にした変異の連番</param>
public sealed record SchedulingOutcome(
    IReadOnlyList<MutantPlan> Plans,
    IReadOnlyList<MutantId> NoCoverageMutants,
    IReadOnlyList<MutantId> ExcludedStaticMutants
);
