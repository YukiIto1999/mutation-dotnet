namespace Oracle.Target;

/// instance 状態、event、collection 初期化子
public sealed class Inventory
{
    private readonly Dictionary<string, int> stock = new() { ["apple"] = 3 };

    public event Action<string>? Depleted;

    public int Count(string item) => stock.TryGetValue(item, out var n) ? n : 0;

    public void Receive(string item, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        stock[item] = Count(item) + amount;
    }

    public bool Take(string item, int amount)
    {
        var current = Count(item);
        if (current < amount)
        {
            return false;
        }

        stock[item] = current - amount;
        if (stock[item] == 0)
        {
            Depleted?.Invoke(item);
        }

        return true;
    }

    public IReadOnlyList<string> Items() => stock.Keys.Order().ToList();
}
