#!/usr/bin/env python3
"""ベンチマーク対象の合成 C# プロジェクトを決定的に生成する。

usage: generate_target.py OUTPUT_DIR [--modules N]
"""
import argparse
import pathlib

MODULE = """namespace Bench.Target;

public static class Mod{i}
{{
    public static int Clamp(int value) => value < {lo} ? {lo} : (value > {hi} ? {hi} : value);

    public static bool InRange(int value) => value >= {lo} && value <= {hi};

    public static int Scale(int value) => value * {k} + {c};

    public static string Label(int value) => value > {t} ? "high{i}" : "low{i}";

    public static int MaxOf(int[] values)
    {{
        var best = int.MinValue;
        foreach (var v in values)
        {{
            if (v > best)
            {{
                best = v;
            }}
        }}

        return best;
    }}

    public static int CountEven(System.Collections.Generic.IEnumerable<int> values) =>
        System.Linq.Enumerable.Count(values, v => v % 2 == 0);

    public static int Weak(int value)
    {{
        var result = value + {c};
        return result;
    }}

    public static int Orphan(int value) => value - {k};
{sum_below}}}
"""

SUM_BELOW = """
    public static int SumBelow(int n)
    {{
        var total = 0;
        var i = 0;
        while (i < n)
        {{
            total += i;
            i += 1;
        }}

        return total;
    }}
"""

TESTS = """using Bench.Target;
using Xunit;

namespace Bench.Target.Tests;

public class Mod{i}Tests
{{
    [Fact] public void Clamp_returns_low_bound_below_range() => Assert.Equal({lo}, Mod{i}.Clamp({below}));

    [Fact] public void Clamp_returns_high_bound_above_range() => Assert.Equal({hi}, Mod{i}.Clamp({above}));

    [Fact] public void Clamp_keeps_value_inside_range() => Assert.Equal({mid}, Mod{i}.Clamp({mid}));

    [Fact] public void InRange_true_inside() => Assert.True(Mod{i}.InRange({mid}));

    [Fact] public void InRange_true_at_bounds()
    {{
        Assert.True(Mod{i}.InRange({lo}));
        Assert.True(Mod{i}.InRange({hi}));
    }}

    [Fact] public void InRange_false_outside()
    {{
        Assert.False(Mod{i}.InRange({below}));
        Assert.False(Mod{i}.InRange({above}));
    }}

    [Fact] public void Scale_computes_linear_form() => Assert.Equal({scaled}, Mod{i}.Scale(3));

    [Fact] public void Label_high_above_threshold() => Assert.Equal("high{i}", Mod{i}.Label({t} + 1));

    [Fact] public void Label_low_at_threshold() => Assert.Equal("low{i}", Mod{i}.Label({t}));

    [Fact] public void MaxOf_finds_largest() => Assert.Equal(9, Mod{i}.MaxOf(new[] {{ 3, 9, 1 }}));

    [Fact] public void CountEven_counts_only_even() => Assert.Equal(2, Mod{i}.CountEven(new[] {{ 1, 2, 3, 4, 5 }}));

    [Fact] public void Weak_only_executes() => Mod{i}.Weak(1);
{sum_below_test}}}
"""

SUM_BELOW_TEST = """
    [Fact] public void SumBelow_sums_integers_below() => Assert.Equal(6, Mod{i}.SumBelow(4));
"""

BUILD_PROPS = """<Project>
  <!-- 生成物は repo root の analyzer 構成と CPM を継承しない -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
"""

TARGET_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
</Project>
"""

TESTS_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Bench.Target/Bench.Target.csproj" />
  </ItemGroup>
</Project>
"""


def module_values(i: int) -> dict:
    lo = 10 + (i % 7)
    hi = lo + 20 + (i % 5)
    mid = (lo + hi) // 2
    k = 2 + (i % 3)
    c = 1 + (i % 9)
    t = 5 + (i % 11)
    return {
        "i": i,
        "lo": lo,
        "hi": hi,
        "mid": mid,
        "below": lo - 5,
        "above": hi + 5,
        "k": k,
        "c": c,
        "t": t,
        "scaled": 3 * k + c,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--modules", type=int, default=40)
    args = parser.parse_args()
    root = pathlib.Path(args.output)
    target = root / "Bench.Target"
    tests = root / "Bench.Target.Tests"
    target.mkdir(parents=True, exist_ok=True)
    tests.mkdir(parents=True, exist_ok=True)
    (root / "Directory.Build.props").write_text(BUILD_PROPS)
    (target / "Bench.Target.csproj").write_text(TARGET_CSPROJ)
    (tests / "Bench.Target.Tests.csproj").write_text(TESTS_CSPROJ)
    for i in range(args.modules):
        values = module_values(i)
        with_loop = i % 10 == 0
        values["sum_below"] = SUM_BELOW.format(**values) if with_loop else ""
        values["sum_below_test"] = SUM_BELOW_TEST.format(**values) if with_loop else ""
        (target / f"Mod{i}.cs").write_text(MODULE.format(**values))
        (tests / f"Mod{i}Tests.cs").write_text(TESTS.format(**values))
    print(f"generated {args.modules} modules under {root}")


if __name__ == "__main__":
    main()
