namespace Pollution.Target;

/// process 内 static memo。Compute は key ごとに process で一度だけ通る
public static class Memo
{
    private static readonly Dictionary<int, int> Cache = new();

    public static int Get(int key)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var value))
            {
                value = Compute(key);
                Cache[key] = value;
            }

            return value;
        }
    }

    private static int Compute(int key) => key * 2;
}
