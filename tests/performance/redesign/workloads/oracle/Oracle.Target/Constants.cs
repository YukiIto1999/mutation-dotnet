using System.ComponentModel;

namespace Oracle.Target;

public enum Level
{
    Low = 1,
    High = 10,
}

/// schemata で扱いにくい位置(const、既定引数、属性、case ラベル、enum 値)
public static class Constants
{
    public const int Limit = 5 + 5;

    public const string Tag = "v" + "1";

    [DefaultValue(3)]
    public static int Scale(int value, int factor = 2) => value * factor;

    public static int Bucket(int value)
    {
        switch (value)
        {
            case Limit:
                return 1;
            case Limit + 1:
                return 2;
            default:
                return 0;
        }
    }

    public static bool IsHigh(Level level) => level >= Level.High;
}
