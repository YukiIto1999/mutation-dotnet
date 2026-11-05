namespace Oracle.Target;

/// 変異で無限ループになる候補(Timeout 判定)
public static class Loops
{
    public static int SumTo(int n)
    {
        var total = 0;
        var i = 0;
        while (i < n)
        {
            total += i;
            i++;
        }

        return total;
    }

    public static int CountDigits(int value)
    {
        var count = 0;
        do
        {
            value /= 10;
            count++;
        }
        while (value != 0);

        return count;
    }

    public static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }

        return a;
    }
}
