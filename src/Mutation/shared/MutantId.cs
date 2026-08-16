
namespace Mutation.Shared;

/// <summary>schemata の活性判定に使う変異の連番</summary>
/// <param name="Value">採番された整数値</param>
public readonly record struct MutantId(int Value);
