using Pauses.Target;
using Xunit;

namespace Pauses.Target.Tests;

// どのテストも一定の間を置いてから一つの method だけを呼ぶ。変異一件の判定に 0.4 秒かかり、走行の途中の状態を作れる
public class ArithmeticTests
{
    private static void Pause() => Thread.Sleep(400);

    [Fact]
    public void Add_adds()
    {
        Pause();
        Assert.Equal(5, Arithmetic.Add(2, 3));
    }

    [Fact]
    public void Subtract_subtracts()
    {
        Pause();
        Assert.Equal(-1, Arithmetic.Subtract(2, 3));
    }

    [Fact]
    public void Multiply_multiplies()
    {
        Pause();
        Assert.Equal(6, Arithmetic.Multiply(2, 3));
    }

    [Fact]
    public void IsPositive_is_true_for_positive()
    {
        Pause();
        Assert.True(Arithmetic.IsPositive(5));
    }

    [Fact]
    public void IsPositive_is_false_for_zero()
    {
        Pause();
        Assert.False(Arithmetic.IsPositive(0));
    }

    [Fact]
    public void IsNegative_is_true_for_negative()
    {
        Pause();
        Assert.True(Arithmetic.IsNegative(-5));
    }

    [Fact]
    public void Clamp_limits_a_large_value()
    {
        Pause();
        Assert.Equal(3, Arithmetic.Clamp(9, 3));
    }
}
