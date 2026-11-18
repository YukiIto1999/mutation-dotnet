using Mutation.Domain;

namespace Mutation.Application;

/// <summary>判定の確定ごとに追記し、最後に保存へ畳む記録</summary>
/// <remarks>最後の保存の前に走行が止まっても、追記した判定は次の継承で使える</remarks>
public interface IVerdictJournal : IDisposable
{
    /// <summary>確定した判定一件の追記</summary>
    /// <param name="id">判定を確定した変異の連番</param>
    /// <param name="verdict">確定した判定</param>
    void Append(MutantId id, MutantVerdict verdict);

    /// <summary>実行全体の最終の判定の保存と、追記した記録の取り除き</summary>
    /// <param name="final">保存する最終の内容</param>
    void Commit(SnapshotSaveRequest final);
}
