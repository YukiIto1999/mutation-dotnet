

namespace Mutation.Verifying.Domain;

/// <summary>基準実行(無変異のテスト実行)で観測した、テストの素性と名簿と被覆の集約</summary>
public sealed record BaselineProfile
{
    /// <summary>素性と名簿の件数の一致を検証する、集約の構築</summary>
    /// <param name="tests">連番順のテストの素性</param>
    /// <param name="roster">連番順のテストの名簿</param>
    /// <param name="coverage">変異と被覆テストの対応表</param>
    /// <exception cref="ArgumentException">素性と名簿の件数が食い違うときに送出</exception>
    public BaselineProfile(IReadOnlyList<TestCaseInfo> tests, TestRoster roster, CoverageMap coverage)
    {
        if (tests.Count != roster.Ids.Count)
        {
            throw new ArgumentException($"テストの素性 {tests.Count} 件と名簿 {roster.Ids.Count} 件が食い違う", nameof(tests));
        }

        Tests = tests;
        Roster = roster;
        Coverage = coverage;
    }

    /// <summary>連番順のテストの素性</summary>
    public IReadOnlyList<TestCaseInfo> Tests { get; }

    /// <summary>連番順のテストの名簿</summary>
    public TestRoster Roster { get; }

    /// <summary>変異と被覆テストの対応表</summary>
    public CoverageMap Coverage { get; }
}
