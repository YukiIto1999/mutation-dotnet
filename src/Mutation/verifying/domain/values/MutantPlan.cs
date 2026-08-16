using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変異一件を検査するための実行計画</summary>
/// <param name="MutantId">対象の変異の連番</param>
/// <param name="Execution">判定を確定させる実行形</param>
public sealed record MutantPlan(MutantId MutantId, MutantExecution Execution);
