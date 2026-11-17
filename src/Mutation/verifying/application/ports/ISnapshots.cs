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
    /// <param name="targetName">対象 project の名前</param>
    /// <returns>前回と同じ判定を持つ対象の確定結果。一致しなければ不在</returns>
    TargetResult? TryRebuild(
        string snapshotPath,
        string? fingerprint,
        string testAssemblyPath,
        string mutatedAssemblyPath,
        string targetName
    );

    /// <summary>前回の保存から継承できる判定の取得。入力の閉包の同一性が前回と一致するときだけ継承する</summary>
    /// <param name="snapshotPath">保存 file の絶対 path</param>
    /// <param name="closureIdentity">今回の入力の閉包の同一性。不在なら一致を示せないので何も継承しない</param>
    /// <param name="mutants">今回の全変異</param>
    /// <param name="testAssemblyPath">今回のテスト assembly の絶対 path</param>
    /// <param name="coverage">今回の被覆表</param>
    /// <param name="roster">今回の連番順のテストの名簿</param>
    /// <returns>変異の連番から継承する判定への対応</returns>
    IReadOnlyDictionary<MutantId, MutantVerdict> Inherit(
        string snapshotPath,
        string? closureIdentity,
        IReadOnlyList<Mutant> mutants,
        string testAssemblyPath,
        CoverageMap coverage,
        TestRoster roster
    );

    /// <summary>判定の確定ごとの追記の開始。実行全体の判定は、返した記録の Commit で次回の継承元として保存する</summary>
    /// <param name="checkpoint">実行の前に分かっている判定を持つ、保存する内容。判定を確定していない変異は未判定で渡す</param>
    /// <returns>確定した判定を追記し、最後に保存へ畳む記録</returns>
    IVerdictJournal Begin(SnapshotSaveRequest checkpoint);
}

/// <summary>保存する内容一式</summary>
/// <param name="Mutants">全変異</param>
/// <param name="Verdicts">変異の連番から確定結果への対応</param>
/// <param name="Baseline">基準実行の観測</param>
/// <param name="Fingerprint">変異の生成入力の全体の指紋。不在なら全体の再構築の根拠にしない</param>
/// <param name="ClosureIdentity">判定を決める入力の閉包の同一性。不在なら変異ごとの継承の根拠にしない</param>
/// <param name="TestAssemblyPath">テスト assembly の絶対 path</param>
/// <param name="MutatedAssemblyPath">変異 assembly の絶対 path</param>
/// <param name="CompileErrorIds">rollback で無効化された変異の連番</param>
/// <param name="SnapshotPath">書き出す file の絶対 path</param>
public sealed record SnapshotSaveRequest(
    IReadOnlyList<Mutant> Mutants,
    IReadOnlyDictionary<MutantId, MutantVerdict> Verdicts,
    BaselineProfile Baseline,
    string? Fingerprint,
    string? ClosureIdentity,
    string TestAssemblyPath,
    string MutatedAssemblyPath,
    IReadOnlySet<MutantId> CompileErrorIds,
    string SnapshotPath
);
