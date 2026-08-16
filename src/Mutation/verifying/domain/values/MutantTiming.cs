using Mutation.Shared;

namespace Mutation.Verifying.Domain;

/// <summary>変異一件の検査に費やした時間の記録</summary>
/// <param name="MutantId">対象の変異の連番</param>
/// <param name="ElapsedMs">判定確定までの所要時間</param>
/// <param name="ExecutedTests">判定確定までに実行したテスト数</param>
/// <param name="Isolated">新規 process での実行で確定したか</param>
public sealed record MutantTiming(MutantId MutantId, double ElapsedMs, int ExecutedTests, bool Isolated = false);
