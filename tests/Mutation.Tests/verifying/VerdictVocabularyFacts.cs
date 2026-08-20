
using Mutation.Verifying.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>状態名の相互変換と probe 上限方針の検査</summary>
public sealed class VerdictVocabularyFacts
{
    /// <summary>全ての確定結果が名前を経由して往復すること</summary>
    [Test]
    public async Task Every_verdict_roundtrips_through_its_name()
    {
        MutantVerdict[] verdicts =
        [
            new MutantVerdict.Killed("t"),
            new MutantVerdict.Survived(),
            new MutantVerdict.TimedOut(),
            new MutantVerdict.NoCoverage(),
            new MutantVerdict.CompileError(),
            new MutantVerdict.Ignored(),
        ];
        foreach (var verdict in verdicts)
        {
            var restored = VerdictNames.Restore(VerdictNames.For(verdict), "t");
            await Assert.That(restored.GetType()).IsEqualTo(verdict.GetType());
        }
    }

    /// <summary>未知の状態名が Ignored へ縮退すること</summary>
    [Test]
    public async Task Unknown_status_degrades_to_ignored()
    {
        await Assert.That(VerdictNames.Restore("Mystery", null) is MutantVerdict.Ignored).IsTrue();
    }

    /// <summary>検出テスト名のない Killed が目印付きで復元されること</summary>
    [Test]
    public async Task Killed_without_killer_gets_a_placeholder()
    {
        await Assert.That(VerdictNames.Restore("Killed", null) is MutantVerdict.Killed { KillerTest: "(baseline)" }).IsTrue();
    }

    /// <summary>probe 上限がテスト列の最大 probe 数に比例すること</summary>
    [Test]
    public async Task Hit_limit_scales_with_probe_counts()
    {
        var roster = new TestRoster(["a", "b"], [10, 100]);
        await Assert.That(HitLimits.For(roster, [new TestIndex(0), new TestIndex(1)])).IsEqualTo((100L * 200) + 100_000);
        await Assert.That(HitLimits.For(roster, [])).IsEqualTo(100_000L);
    }
}
