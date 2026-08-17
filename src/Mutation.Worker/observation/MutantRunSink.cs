using Mutation.Protocol;
using Xunit.Abstractions;

namespace Mutation.Worker;

/// <summary>変異検査の実行で最初の失敗を捉える受け口</summary>
public sealed class MutantRunSink(SwitchAccessor accessor, bool failFast) : IMessageSink
{
    /// <summary>assembly 実行の完了を伝える合図</summary>
    public ManualResetEventSlim Finished { get; } = new(false);

    /// <summary>実行を始めたテスト数</summary>
    public int ExecutedTests { get; private set; }

    /// <summary>最初に失敗したテストの表示名。失敗がなければ不在</summary>
    public string? KillerTest { get; private set; }

    /// <summary>最初の失敗が probe 上限超過によるものか</summary>
    public bool HitLimitExceeded { get; private set; }

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
                if (KillerTest is null)
                {
                    KillerTest = failed.Test.DisplayName;
                    HitLimitExceeded = failed.Messages.Any(m =>
                        m.Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal)
                    );
                }

                if (failFast)
                {
                    Finished.Set();
                    return false;
                }

                break;
            case ITestAssemblyFinished:
                Finished.Set();
                break;
        }

        return true;
    }
}
