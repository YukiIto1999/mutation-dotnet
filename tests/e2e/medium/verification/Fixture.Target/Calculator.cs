namespace Fixture.Target;

public static class Calculator
{
    public static int Add(int a, int b) => a + b;

    public static bool IsPositive(int value) => value > 0;

    public static int Untested(int a) => a * 2;

    public static int CoveredButUnasserted(int a) => a + 1;

    public static int SumTo(int n)
    {
        var total = 0;
        var i = 0;
        while (i < n)
        {
            total += i;
            i += 1;
        }

        return total;
    }
}
