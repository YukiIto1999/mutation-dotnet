using Mutation.Shared;


namespace Mutation.Mutating.Domain;

/// <summary>schemata コンパイルの成果</summary>
/// <param name="Mutants">採番済みの全変異</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="MutatedAssemblyPath">書き出した変異 assembly の path</param>
/// <param name="MutateMs">候補収集と採番の所要時間</param>
/// <param name="CompileMs">書換と emit と rollback の所要時間</param>
public sealed record MutatedArtifact(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlySet<MutantId> CompileErrorIds,
    string MutatedAssemblyPath,
    double MutateMs,
    double CompileMs
);
