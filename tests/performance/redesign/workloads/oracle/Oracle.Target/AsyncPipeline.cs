namespace Oracle.Target;

/// async/await、Task.WhenAll、ConfigureAwait、例外の伝播
public static class AsyncPipeline
{
    public static async Task<int> SumAsync(IEnumerable<int> values)
    {
        var total = 0;
        foreach (var value in values)
        {
            await Task.Yield();
            total += value;
        }

        return total;
    }

    public static async Task<int[]> DoubleAllAsync(int[] values)
    {
        var tasks = values.Select(async v =>
        {
            await Task.Delay(1).ConfigureAwait(false);
            return v * 2;
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static async Task<string> FetchAsync(int id)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        await Task.Delay(1).ConfigureAwait(false);
        return id == 0 ? "zero" : $"item-{id}";
    }
}
