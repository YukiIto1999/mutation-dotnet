namespace Oracle.Target;

/// generic と制約
public static class Generics
{
    public static T Max<T>(T a, T b)
        where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

    public static bool AllPositive<T>(IEnumerable<T> values, Func<T, int> measure) =>
        values.All(v => measure(v) > 0);

    public static TOut[] MapNonNull<TIn, TOut>(IEnumerable<TIn?> values, Func<TIn, TOut> map)
        where TIn : class => values.Where(v => v is not null).Select(v => map(v!)).ToArray();
}
