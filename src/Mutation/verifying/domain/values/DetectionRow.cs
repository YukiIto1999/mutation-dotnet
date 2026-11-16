namespace Mutation.Verifying.Domain;

/// <summary>変異一件の検出行列の一行</summary>
/// <param name="Planned">実行を求めたテストの実行順の連番</param>
/// <param name="Failed">変異の活性下で失敗したテストの観測順の連番</param>
/// <param name="NotRun">結果を観測できなかったテストの実行順の連番</param>
public sealed record DetectionRow(
    IReadOnlyList<TestIndex> Planned,
    IReadOnlyList<TestIndex> Failed,
    IReadOnlyList<TestIndex> NotRun
)
{
    /// <summary>実行を求めた全テストの結果を観測したか</summary>
    public bool IsComplete => NotRun.Count == 0;
}
