namespace Mutation.Worker;

/// <summary>一変異の run で観測した失敗と完了の記録</summary>
/// <remarks>最初の失敗による結末の決定</remarks>
public sealed class RunObservation
{
    /// <summary>失敗したテストの一意識別子の観測順の控え</summary>
    private readonly List<string> failed = [];

    /// <summary>失敗を記録済みのテストの一意識別子</summary>
    private readonly HashSet<string> failedSeen = new(StringComparer.Ordinal);

    /// <summary>結果まで観測したテストの一意識別子の観測順の控え</summary>
    private readonly List<string> completed = [];

    /// <summary>完了を記録済みのテストの一意識別子</summary>
    private readonly HashSet<string> completedSeen = new(StringComparer.Ordinal);

    /// <summary>最初に失敗したテストの表示名</summary>
    public string? KillerTest { get; private set; }

    /// <summary>最初の失敗が probe 上限超過によるものか</summary>
    public bool HitLimitExceeded { get; private set; }

    /// <summary>失敗した全テストの観測順の一意識別子</summary>
    public IReadOnlyList<string> FailedTestIds => failed;

    /// <summary>結果まで観測した全テストの観測順の一意識別子</summary>
    public IReadOnlyList<string> CompletedTestIds => completed;

    /// <summary>テスト一件の失敗の記録</summary>
    /// <remarks>検出テストと probe 上限超過の有無を決める最初の失敗</remarks>
    /// <param name="testId">失敗したテストの一意識別子</param>
    /// <param name="displayName">失敗したテストの表示名</param>
    /// <param name="hitLimitExceeded">その失敗が probe 上限超過によるものか</param>
    public void Fail(string testId, string displayName, bool hitLimitExceeded)
    {
        if (KillerTest is null)
        {
            KillerTest = displayName;
            HitLimitExceeded = hitLimitExceeded;
        }

        if (failedSeen.Add(testId))
        {
            failed.Add(testId);
        }
    }

    /// <summary>テスト一件の結果の観測の記録</summary>
    /// <param name="testId">結果まで観測したテストの一意識別子</param>
    public void Complete(string testId)
    {
        if (completedSeen.Add(testId))
        {
            completed.Add(testId);
        }
    }
}
