namespace Oracle.Target;

/// 例外と防御的検査
public static class Guards
{
    public static int Divide(int a, int b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("b must not be zero");
        }

        return a / b;
    }

    public static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("required", name) : value;

    public static int SafeParse(string text)
    {
        try
        {
            return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}
