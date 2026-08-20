using Fixture.Target;
using Xunit;

namespace Fixture.Target.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_returns_sum() => Assert.Equal(5, Calculator.Add(2, 3));

    [Fact]
    public void IsPositive_true_for_positive() => Assert.True(Calculator.IsPositive(5));

    [Fact]
    public void IsPositive_false_for_zero() => Assert.False(Calculator.IsPositive(0));

    [Fact]
    public void IsPositive_false_for_negative() => Assert.False(Calculator.IsPositive(-2));

    [Fact]
    public void CoveredButUnasserted_only_executes() => Calculator.CoveredButUnasserted(1);

    [Fact]
    public void SumTo_sums_integers_below() => Assert.Equal(6, Calculator.SumTo(4));

    [Fact]
    public void WithOffset_adds_seven() => Assert.Equal(8, Configured.WithOffset(1));

    [Fact]
    public void WithSeed_adds_eight() => Assert.Equal(9, Configured.WithSeed(1));
}
