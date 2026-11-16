namespace Pauses.Target;

public static class Arithmetic
{
    public static int Add(int a, int b) => a + b;

    public static int Subtract(int a, int b) => a - b;

    public static int Multiply(int a, int b) => a * b;

    public static bool IsPositive(int value) => value > 0;

    public static bool IsNegative(int value) => value < 0;

    public static int Clamp(int value, int limit) => value > limit ? limit : value;
}
