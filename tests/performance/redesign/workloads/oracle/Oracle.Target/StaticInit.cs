namespace Oracle.Target;

/// static 初期化でだけ実行されるコード
public static class StaticInit
{
    public static readonly int Base = 40 + 2;

    public static readonly string Prefix = BuildPrefix("cfg");

    private static readonly int[] Table;

    static StaticInit()
    {
        Table = new int[4];
        for (var i = 0; i < Table.Length; i++)
        {
            Table[i] = i * 3;
        }
    }

    public static int Lookup(int index) => Table[index] + Base;

    private static string BuildPrefix(string name) => name.ToUpperInvariant() + ":";
}
