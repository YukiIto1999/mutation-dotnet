using Mutation.Protocol;
using Xunit.Abstractions;

namespace Mutation.Worker;

/// <summary>変異検査の実行で失敗と完了を捉える受け口</summary>
/// <param name="accessor">変異 assembly の切替クラスへの操作</param>
/// <param name="failFast">最初の失敗で実行を打ち切るか</param>
public sealed class MutantRunSink(SwitchAccessor accessor, bool failFast) : IMessageSink
{
    /// <summary>assembly 実行の完了を伝える合図</summary>
    public ManualResetEventSlim Finished { get; } = new(false);

    /// <summary>実行を始めたテスト数</summary>
    public int ExecutedTests { get; private set; }

    /// <summary>観測した失敗と完了の記録</summary>
    public RunObservation Observation { get; } = new();

    /// <inheritdoc />
    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestStarting:
                ExecutedTests++;
                accessor.TakeHitCount();
                break;
            case ITestFailed failed:
                Observation.Fail(
                    failed.TestCase.UniqueID,
                    failed.Test.DisplayName,
                    failed.Messages.Any(m => m.Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal))
                );
                if (failFast)
                {
                    Finished.Set();
                    return false;
                }

                break;
            case ITestCaseFinished finished when !failFast:
                Observation.Complete(finished.TestCase.UniqueID);
                break;
            case ITestAssemblyFinished:
                Finished.Set();
                break;
        }

        return true;
    }
}
