namespace Oracle.Target;

/// LINQ の主要 method
public static class Linq
{
    public static int SumOfTopThree(IEnumerable<int> values) => values.OrderByDescending(v => v).Take(3).Sum();

    public static int FirstEvenOrDefault(IEnumerable<int> values) => values.FirstOrDefault(v => v % 2 == 0);

    public static bool AnyNegative(IEnumerable<int> values) => values.Any(v => v < 0);

    public static int DistinctCount(IEnumerable<int> values) => values.Distinct().Count();

    public static int[] SkipHeader(IEnumerable<int> values) => values.Skip(1).ToArray();

    public static int MinOrZero(IReadOnlyCollection<int> values) => values.Count == 0 ? 0 : values.Min();
}
