using Frameworks.Target;
using Xunit;

namespace Frameworks.Xunit.Tests;

/// <summary>xunit v2 での Calc の検査</summary>
public sealed class CalcTests
{
    [Fact]
    public void Add_sums()
    {
        Assert.Equal(5, Calc.Add(2, 3));
        Assert.Equal(1, Calc.Add(0, 1));
    }

    [Fact]
    public void Above_boundary()
    {
        Assert.True(Calc.Above(11));
        Assert.False(Calc.Above(10));
        Assert.False(Calc.Above(5));
    }
}
