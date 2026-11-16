using Mutation.Protocol;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Mutation.Worker;

/// <summary>変異検査の実行で失敗と完了を捉える VSTest の受け口</summary>
/// <param name="accessor">変異 assembly の切替クラスへの操作</param>
/// <param name="allFailures">最初の失敗の後も失敗と完了を記録し続けるか</param>
public sealed class VstestRunRecorder(SwitchAccessor accessor, bool allFailures) : VstestRecorder
{
    /// <summary>実行を始めたテスト数</summary>
    public int ExecutedTests { get; private set; }

    /// <summary>観測した失敗と完了の記録</summary>
    public RunObservation Observation { get; } = new();

    /// <inheritdoc />
    public override void RecordStart(TestCase testCase)
    {
        ExecutedTests++;
        accessor.TakeHitCount();
    }

    /// <inheritdoc />
    public override void RecordResult(TestResult testResult)
    {
        var failed = testResult.Outcome == TestOutcome.Failed;
        if (!allFailures && (!failed || Observation.KillerTest is not null))
        {
            return;
        }

        var testId = testResult.TestCase.Id.ToString();
        if (testResult.Outcome is TestOutcome.Passed or TestOutcome.Failed or TestOutcome.Skipped)
        {
            Observation.Complete(testId);
        }

        if (!failed)
        {
            return;
        }

        Observation.Fail(
            testId,
            testResult.TestCase.DisplayName,
            (testResult.ErrorMessage ?? "").Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal)
                || (testResult.ErrorStackTrace ?? "").Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal)
        );
    }
}
