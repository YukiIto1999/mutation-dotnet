using Xunit.Abstractions;

namespace Mutation.Worker;

/// <summary>テスト発見の結果を集める受け口</summary>
public sealed class DiscoverySink : IMessageSink
{
    /// <summary>発見した test case の控え</summary>
    private readonly List<ITestCase> testCases = [];

    /// <summary>発見済みのテストの列</summary>
    public IReadOnlyList<ITestCase> TestCases => testCases;

    /// <summary>発見の完了を伝える合図</summary>
    public ManualResetEventSlim Finished { get; } = new(false);

    /// <inheritdoc />
    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestCaseDiscoveryMessage discovery)
        {
            testCases.Add(discovery.TestCase);
        }

        if (message is IDiscoveryCompleteMessage)
        {
            Finished.Set();
        }

        return true;
    }
}
