using Frameworks.Target;
using NUnit.Framework;

namespace Frameworks.Nunit.Tests;

/// <summary>NUnit での Calc の検査</summary>
public sealed class CalcTests
{
    [Test]
    public void Add_sums()
    {
        Assert.That(Calc.Add(2, 3), Is.EqualTo(5));
        Assert.That(Calc.Add(0, 1), Is.EqualTo(1));
    }

    [Test]
    public void Above_boundary()
    {
        Assert.That(Calc.Above(11), Is.True);
        Assert.That(Calc.Above(10), Is.False);
        Assert.That(Calc.Above(5), Is.False);
    }
}
