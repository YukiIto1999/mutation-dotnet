

namespace Mutation.Verifying.Domain;

/// <summary>連番順に並んだテストの一意識別子と probe 呼び出し回数の名簿</summary>
public sealed record TestRoster
{
    /// <summary>二列の件数の一致を検証する、名簿の構築</summary>
    /// <param name="ids">連番からテスト一意識別子への対応</param>
    /// <param name="probeCounts">連番からテスト実行中の probe 呼び出し回数への対応</param>
    /// <exception cref="ArgumentException">二列の件数が食い違うときに送出</exception>
    public TestRoster(IReadOnlyList<string> ids, IReadOnlyList<long> probeCounts)
    {
        if (ids.Count != probeCounts.Count)
        {
            throw new ArgumentException($"識別子 {ids.Count} 件と probe 回数 {probeCounts.Count} 件が食い違う", nameof(ids));
        }

        Ids = ids;
        ProbeCounts = probeCounts;
    }

    /// <summary>連番からテスト一意識別子への対応</summary>
    public IReadOnlyList<string> Ids { get; }

    /// <summary>連番からテスト実行中の probe 呼び出し回数への対応</summary>
    public IReadOnlyList<long> ProbeCounts { get; }

    /// <summary>連番に対応するテストの一意識別子</summary>
    /// <param name="index">テストの連番</param>
    /// <returns>そのテストの一意識別子</returns>
    public string Id(TestIndex index) => Ids[index.Value];

    /// <summary>連番に対応するテストの probe 呼び出し回数</summary>
    /// <param name="index">テストの連番</param>
    /// <returns>baseline 実行で観測した probe 呼び出し回数</returns>
    public long ProbeCount(TestIndex index) => ProbeCounts[index.Value];
}
