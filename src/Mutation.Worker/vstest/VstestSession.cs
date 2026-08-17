using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Mutation.Worker;

/// <summary>VSTest adapter へ渡す文脈の一式。framework 内並列は無効の形</summary>
public static class VstestSession
{
    /// <summary>framework 内並列を無効にする固定 run settings</summary>
    private const string NoParallelSettingsXml = """
        <RunSettings>
          <RunConfiguration>
            <DisableParallelization>true</DisableParallelization>
            <MaxCpuCount>1</MaxCpuCount>
          </RunConfiguration>
          <NUnit>
            <NumberOfTestWorkers>0</NumberOfTestWorkers>
          </NUnit>
          <MSTest>
            <Parallelize>
              <Workers>1</Workers>
            </Parallelize>
          </MSTest>
        </RunSettings>
        """;

    /// <summary>発見と実行に使う共通の文脈</summary>
    /// <returns>並列無効の設定を持つ文脈</returns>
    public static IRunContext Context() => new AdapterContext();

    /// <summary>adapter の警告以上だけを stderr へ流す受け口</summary>
    public static IMessageLogger Logger { get; } = new StderrLogger();

    /// <summary>発見された test case を集める受け口</summary>
    public sealed class CaseSink : ITestCaseDiscoverySink
    {
        /// <summary>発見した test case の控え</summary>
        private readonly List<TestCase> cases = [];

        /// <summary>発見された test case の列</summary>
        public IReadOnlyList<TestCase> Cases => cases;

        /// <inheritdoc />
        public void SendTestCase(TestCase discoveredTest) => cases.Add(discoveredTest);
    }

    /// <summary>並列無効の固定設定だけを持つ実行文脈</summary>
    private sealed class AdapterContext : IRunContext
    {
        /// <inheritdoc />
        public IRunSettings? RunSettings { get; } = new FixedSettings();

        /// <inheritdoc />
        public bool KeepAlive => false;

        /// <inheritdoc />
        public bool InIsolation => false;

        /// <inheritdoc />
        public bool IsDataCollectionEnabled => false;

        /// <inheritdoc />
        public bool IsBeingDebugged => false;

        /// <inheritdoc />
        public string? TestRunDirectory => null;

        /// <inheritdoc />
        public string? SolutionDirectory => null;

        /// <inheritdoc />
        public ITestCaseFilterExpression? GetTestCaseFilter(
            IEnumerable<string>? supportedProperties,
            Func<string, TestProperty?> propertyProvider
        ) => null;
    }

    /// <summary>並列無効の固定 run settings</summary>
    private sealed class FixedSettings : IRunSettings
    {
        /// <inheritdoc />
        public string SettingsXml => VstestSession.NoParallelSettingsXml;

        /// <inheritdoc />
        public ISettingsProvider? GetSettings(string? settingsName) => null;
    }

    /// <summary>警告以上だけを stderr へ流す受け口</summary>
    private sealed class StderrLogger : IMessageLogger
    {
        /// <inheritdoc />
        public void SendMessage(TestMessageLevel testMessageLevel, string? message)
        {
            if (testMessageLevel >= TestMessageLevel.Warning && message is not null)
            {
                Console.Error.WriteLine(message);
            }
        }
    }
}
