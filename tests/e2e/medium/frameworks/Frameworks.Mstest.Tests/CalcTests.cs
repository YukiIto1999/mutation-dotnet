using Frameworks.Target;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Frameworks.Mstest.Tests;

/// <summary>MSTest での Calc の検査</summary>
[TestClass]
public sealed class CalcTests
{
    [TestMethod]
    public void Add_sums()
    {
        Assert.AreEqual(5, Calc.Add(2, 3));
        Assert.AreEqual(1, Calc.Add(0, 1));
    }

    [TestMethod]
    public void Above_boundary()
    {
        Assert.IsTrue(Calc.Above(11));
        Assert.IsFalse(Calc.Above(10));
        Assert.IsFalse(Calc.Above(5));
    }
}
