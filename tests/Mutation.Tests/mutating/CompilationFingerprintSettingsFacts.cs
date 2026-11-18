using Mutation.Domain;
using TUnit.Core;

namespace Mutation.Tests;

public sealed partial class CompilationFingerprintFacts
{
    /// <summary>変異の集合と判定を左右する設定のどれが変わっても、閉包の同一性と全体の指紋が変わること</summary>
    /// <param name="setting">変える設定の名前</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("mutate")]
    [Arguments("since")]
    [Arguments("changed-lines")]
    [Arguments("ignore-operators")]
    [Arguments("ignore-methods")]
    [Arguments("validate-survivors")]
    [Arguments("exclude-static")]
    public async Task A_change_to_any_setting_that_shapes_the_verdicts_changes_the_closure_identity(string setting)
    {
        using var project = new ClosureProject();
        var before = project.Compute()!;

        var after = project.Compute(Changed(setting))!;

        await Assert.That(after.Closure).IsNotEqualTo(before.Closure);
        await Assert.That(after.Fingerprint).IsNotEqualTo(before.Fingerprint);
    }

    /// <summary>列の設定で、一つの要素が区切りの文字を含む形と、二つの要素に分けた形が、別の同一性になること</summary>
    /// <remarks>列を `,` で連結して記述すると、`["a,b"]` と `["a","b"]` が同じ記述になり、選別が違うのに同じ保存を使ってしまう</remarks>
    /// <param name="list">値を入れる列の設定の名前</param>
    /// <returns>検査の完了</returns>
    [Test]
    [Arguments("mutate")]
    [Arguments("ignore-operators")]
    [Arguments("ignore-methods")]
    public async Task One_entry_holding_a_comma_and_two_entries_give_different_identities(string list)
    {
        using var project = new ClosureProject();

        var joined = project.Compute(Listing(list, ["a,b"]))!;
        var split = project.Compute(Listing(list, ["a", "b"]))!;

        await Assert.That(joined.Closure).IsNotEqualTo(split.Closure);
        await Assert.That(joined.Fingerprint).IsNotEqualTo(split.Fingerprint);
    }

    /// <summary>選別も実行の設定も既定の設定</summary>
    private static readonly ReuseSettings Baseline = new(new MutationSelection([], null, false, [], []), ValidateSurvivors: false, ExcludeStatic: false);

    /// <summary>基準の設定から、指定の設定だけを変えた設定</summary>
    private static ReuseSettings Changed(string setting) =>
        setting switch
        {
            "mutate" => Listing("mutate", ["src/**"]),
            "since" => Baseline with { Selection = Baseline.Selection with { SinceRef = "main" } },
            "changed-lines" => Baseline with { Selection = Baseline.Selection with { ChangedLinesOnly = true } },
            "ignore-operators" => Listing("ignore-operators", ["BinaryOperatorMutator"]),
            "ignore-methods" => Listing("ignore-methods", ["Log"]),
            "validate-survivors" => Baseline with { ValidateSurvivors = true },
            "exclude-static" => Baseline with { ExcludeStatic = true },
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, "未知の設定"),
        };

    /// <summary>基準の設定から、列の設定一つへ値を入れた設定</summary>
    private static ReuseSettings Listing(string list, string[] values) =>
        list switch
        {
            "mutate" => Baseline with { Selection = Baseline.Selection with { MutatePatterns = values } },
            "ignore-operators" => Baseline with { Selection = Baseline.Selection with { IgnoredOperators = values } },
            "ignore-methods" => Baseline with { Selection = Baseline.Selection with { IgnoredMethods = values } },
            _ => throw new ArgumentOutOfRangeException(nameof(list), list, "未知の列の設定"),
        };

}
