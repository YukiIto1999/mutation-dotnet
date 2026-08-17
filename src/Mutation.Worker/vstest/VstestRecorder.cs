using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Mutation.Worker;

/// <summary>VSTest adapter からの実行報告の共通の受け口</summary>
public abstract class VstestRecorder : IFrameworkHandle
{
    /// <inheritdoc />
    public bool EnableShutdownAfterTestRun { get; set; }

    /// <inheritdoc />
    public abstract void RecordStart(TestCase testCase);

    /// <inheritdoc />
    public abstract void RecordResult(TestResult testResult);

    /// <inheritdoc />
    public void RecordEnd(TestCase testCase, TestOutcome outcome)
    {
    }

    /// <inheritdoc />
    public void RecordAttachments(IList<AttachmentSet> attachmentSets)
    {
    }

    /// <inheritdoc />
    public int LaunchProcessWithDebuggerAttached(
        string filePath,
        string? workingDirectory,
        string? arguments,
        IDictionary<string, string?>? environmentVariables
    ) => throw new NotSupportedException("debugger 起動には応じない");

    /// <inheritdoc />
    public void SendMessage(TestMessageLevel testMessageLevel, string? message)
    {
        if (testMessageLevel >= TestMessageLevel.Warning && message is not null)
        {
            Console.Error.WriteLine(message);
        }
    }
}
