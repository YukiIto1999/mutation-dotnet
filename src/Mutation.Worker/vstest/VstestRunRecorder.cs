using Mutation.Protocol;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Mutation.Worker;

/// <summary>変異検査の実行で最初の失敗を捉える VSTest の受け口</summary>
public sealed class VstestRunRecorder(SwitchAccessor accessor) : VstestRecorder
{
    /// <summary>実行を始めたテスト数</summary>
    public int ExecutedTests { get; private set; }

    /// <summary>最初に失敗したテストの表示名。失敗がなければ不在</summary>
    public string? KillerTest { get; private set; }

    /// <summary>最初の失敗が probe 上限超過によるものか</summary>
    public bool HitLimitExceeded { get; private set; }

    /// <inheritdoc />
    public override void RecordStart(TestCase testCase)
    {
        ExecutedTests++;
        accessor.TakeHitCount();
    }

    /// <inheritdoc />
    public override void RecordResult(TestResult testResult)
    {
        if (testResult.Outcome != TestOutcome.Failed || KillerTest is not null)
        {
            return;
        }

        KillerTest = testResult.TestCase.DisplayName;
        HitLimitExceeded =
            (testResult.ErrorMessage ?? "").Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal)
            || (testResult.ErrorStackTrace ?? "").Contains(InjectionContract.HitLimitMarker, StringComparison.Ordinal);
    }
}
