using Mutation.Shared;
using Mutation.Verifying.Domain;

namespace Mutation.Verifying.Application;

/// <summary>前回実行の保存と継承を担う port</summary>
public interface ISnapshots
{
    /// <summary>保存と生成入力が完全一致するときの、前回結果の再構築。一致しなければ不在</summary>
    /// <param name="snapshotPath">保存 file の絶対 path</param>
    /// <param name="fingerprint">今回の生成入力の指紋</param>
    /// <param name="testAssemblyPath">今回のテスト assembly の絶対 path</param>
    /// <param name="mutatedAssemblyPath">前回の変異 assembly の置き場</param>
    /// <param name="timings">今回の段階別所要時間</param>
    /// <returns>前回と同じ判定を持つ確定結果。一致しなければ不在</returns>
    MutationRunResult? TryRebuild(
        string snapshotPath,
        string? fingerprint,
        string testAssemblyPath,
        string mutatedAssemblyPath,
        PhaseTimings timings
    );

    /// <summary>前回の保存から継承できる判定の取得</summary>
    /// <param name="snapshotPath">保存 file の絶対 path</param>
    /// <param name="mutants">今回の全変異</param>
    /// <param name="testAssemblyPath">今回のテスト assembly の絶対 path</param>
    /// <param name="coverage">今回の被覆表</param>
    /// <param name="roster">今回の連番順のテストの名簿</param>
    /// <returns>変異の連番から継承する判定への対応</returns>
    IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(
        string snapshotPath,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    );

    /// <summary>今回の実行全体の、次回の継承元としての書き出し</summary>
    /// <param name="request">保存する内容一式</param>
    void Save(SnapshotSaveRequest request);
}

/// <summary>保存する内容一式</summary>
/// <param name="Mutants">全変異</param>
/// <param name="Verdicts">変異の連番から確定結果への対応</param>
/// <param name="Baseline">基準実行の観測</param>
/// <param name="Fingerprint">変異の生成入力の指紋</param>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="MutatedAssemblyPath">変異 assembly の絶対 path</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="SnapshotPath">書き出す file の絶対 path</param>
public sealed record SnapshotSaveRequest(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlyDictionary<MutantId, MutantVerdict> Verdicts,
    BaselineProfile Baseline,
    string? Fingerprint,
    string TestAssemblyPath,
    string MutatedAssemblyPath,
    IReadOnlySet<MutantId> CompileErrorIds,
    string SnapshotPath
);
