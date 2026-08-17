using Mutation.Protocol;

namespace Mutation.Worker;

/// <summary>一つのテスト assembly を常駐で繰り返し実行するホスト</summary>
public interface ITestHost : IDisposable
{
    /// <summary>全テストの一意識別子の列挙</summary>
    /// <returns>発見したテストの一意識別子</returns>
    IReadOnlyList<string> Discover();

    /// <summary>被覆収集モードでのテスト実行</summary>
    /// <param name="testIds">対象にするテストの一意識別子の列</param>
    /// <returns>テストごとの結果と、隔離が要る変異の連番</returns>
    WorkerResponse.Baselined Baseline(IReadOnlyList<string> testIds);

    /// <summary>一変異を活性化した選択テストの実行</summary>
    /// <param name="request">変異と対象テストと打ち切り方の指示</param>
    /// <returns>検査の結末。テストを特定できないときは失敗</returns>
    WorkerResponse Run(WorkerRequest.Run request);
}
