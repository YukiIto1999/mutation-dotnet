using Frameworks.Target;

namespace Frameworks.Tunit.Tests;

/// <summary>TUnit での Calc の検査</summary>
public sealed class CalcTests
{
    [Test]
    public async Task Add_sums()
    {
        await Assert.That(Calc.Add(2, 3)).IsEqualTo(5);
        await Assert.That(Calc.Add(0, 1)).IsEqualTo(1);
    }

    [Test]
    public async Task Add_commutes() => await Assert.That(Calc.Add(3, 2)).IsEqualTo(Calc.Add(2, 3));

    [Test]
    public async Task Above_boundary()
    {
        await Assert.That(Calc.Above(11)).IsTrue();
        await Assert.That(Calc.Above(10)).IsFalse();
        await Assert.That(Calc.Above(5)).IsFalse();
    }
}
