namespace Fixture.Target;

public static class Configured
{
    public static readonly int Offset = 10 - 3;

    public static readonly int Seed = ComputeSeed(4);

    public static int WithOffset(int value) => value + Offset;

    public static int WithSeed(int value) => value + Seed;

    private static int ComputeSeed(int basis) => basis * 2;
}
