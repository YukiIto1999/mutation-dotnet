namespace Oracle.Target;

/// process 内の static な memo。初回だけ Compute を通るため、常駐 worker での状態汚染を露わにする
public static class StaticCache
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

    private static int Compute(int key) => key * key + 1;
}
