using TUnit.Core;

namespace Mutation.Tests;

/// <summary>schemata 織り込み後の assembly の挙動の検査</summary>
public sealed class SchemataRoundtripFacts
{
    private const string Source = """
        namespace Sample;

        public static class Math2
        {
            public static int Add(int a, int b) => a + b;

            public static bool Below(int a, int limit) => a < limit;
        }
        """;

    /// <summary>不活性状態での元の挙動の維持</summary>
    [Test]
    public async Task Inactive_schemata_preserve_original_behavior()
    {
        var (assembly, _) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        await Assert.That(Invoke<int>(math, "Add", 2, 3)).IsEqualTo(5);
        await Assert.That(Invoke<bool>(math, "Below", 1, 2)).IsTrue();
    }

    /// <summary>変異ごとの排他的な活性化と挙動の変化</summary>
    [Test]
    public async Task Each_mutant_changes_behavior_exclusively()
    {
        var (assembly, mutantCount) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        var control = new SwitchControl(assembly);
        var changed = 0;
        for (var id = 0; id < mutantCount; id++)
        {
            control.Activate(id);
            var addChanged = Invoke<int>(math, "Add", 2, 3) != 5;
            var belowChanged = !Invoke<bool>(math, "Below", 1, 2);
            var boundaryChanged = Invoke<bool>(math, "Below", 2, 2);
            if (addChanged || belowChanged || boundaryChanged)
            {
                changed++;
            }
        }

        control.Activate(-1);
        await Assert.That(mutantCount).IsEqualTo(3);
        await Assert.That(changed).IsEqualTo(3);
        await Assert.That(Invoke<int>(math, "Add", 2, 3)).IsEqualTo(5);
    }

    /// <summary>被覆収集モードでの probe の記録</summary>
    [Test]
    public async Task Coverage_mode_records_executed_probes()
    {
        var (assembly, _) = Snippet.EmitWithSchemata(Source);
        var math = assembly.GetType("Sample.Math2")!;
        var control = new SwitchControl(assembly);
        control.StartCoverage();
        Invoke<int>(math, "Add", 2, 3);
        var hits = control.DrainHits();
        control.Activate(-1);
        await Assert.That(hits.Length).IsEqualTo(1);
        await Assert.That(control.DrainHits().Length).IsEqualTo(0);
    }

    private static T Invoke<T>(Type type, string method, params object[] arguments) =>
        (T)type.GetMethod(method)!.Invoke(null, arguments)!;

    private sealed class SwitchControl
    {
        private readonly Type type;

        public SwitchControl(System.Reflection.Assembly assembly)
        {
            type = assembly.GetType("MutationInjected.MutantSwitch")!;
        }

        public void Activate(int id) => type.GetMethod("Activate")!.Invoke(null, [id, long.MaxValue]);

        public void StartCoverage() => type.GetMethod("StartCoverage")!.Invoke(null, []);

        public int[] DrainHits() => (int[])type.GetMethod("DrainHits")!.Invoke(null, [])!;
    }
}
