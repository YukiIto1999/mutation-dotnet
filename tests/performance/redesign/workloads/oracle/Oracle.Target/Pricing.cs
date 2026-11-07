namespace Oracle.Target;

/// 境界と算術。全面的に検査されている部分と、境界の片側だけ未検査の部分を持つ
public static class Pricing
{
    public static decimal Discount(decimal amount, int quantity)
    {
        if (quantity >= 100)
        {
            return amount * 0.8m;
        }

        if (quantity > 10)
        {
            return amount * 0.9m;
        }

        return amount;
    }

    public static int Shipping(int weight) => weight <= 0 ? 0 : 5 + (weight / 10) * 2;

    // 0 の境界は検査しない(`<` → `<=` は等価 mutant: v == 0 で両方 0 を返す)
    public static int ClampNonNegative(int v) => v < 0 ? 0 : v;
}
