namespace Mutation.Domain;

/// <summary>worker process を起動する実体。host 実行体と worker assembly の組</summary>
/// <remarks>
/// worker は「host 実行体 worker assembly」の形で起動する。host は worker assembly の directory にある runtimeconfig と deps と依存 assembly から実行環境を決める
/// 走行は一度だけ決めた組を、再利用の同一性の捕捉と worker の起動の両方へ渡す。同じ値なので、捕捉した実体と実行した実体は一致する
/// </remarks>
/// <param name="ExecutablePath">worker を起動する host 実行体の絶対 path</param>
/// <param name="DllPath">worker assembly の絶対 path</param>
public sealed record WorkerHost(string ExecutablePath, string DllPath);
