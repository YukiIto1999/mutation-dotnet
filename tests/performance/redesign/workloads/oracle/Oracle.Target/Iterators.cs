namespace Oracle.Target;

/// iterator と遅延評価
public static class Iterators
{
    public static IEnumerable<int> Evens(int limit)
    {
        for (var i = 0; i <= limit; i++)
        {
            if (i % 2 == 0)
            {
                yield return i;
            }
        }
    }

    public static IEnumerable<string> NonEmpty(IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            yield return value;
        }
    }
}
