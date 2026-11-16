namespace Oracle.Target;

/// 並行実行と共有状態
public sealed class Counter
{
    private readonly object gate = new();
    private int value;

    public int Value => value;

    public void Add(int amount)
    {
        lock (gate)
        {
            value += amount;
        }
    }

    public static int ParallelSum(int count)
    {
        var counter = new Counter();
        Parallel.For(0, count, i => counter.Add(1));
        return counter.Value;
    }
}
