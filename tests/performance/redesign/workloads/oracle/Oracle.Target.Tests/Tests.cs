using Oracle.Target;
using Xunit;

namespace Oracle.Target.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(100, 100, 80)]
    [InlineData(100, 150, 80)]
    [InlineData(100, 11, 90)]
    [InlineData(100, 10, 100)]
    [InlineData(100, 1, 100)]
    public void Discount_by_quantity(int amount, int quantity, int expected) =>
        Assert.Equal((decimal)expected, Pricing.Discount(amount, quantity));

    [Fact] public void Shipping_is_free_for_zero() => Assert.Equal(0, Pricing.Shipping(0));

    [Fact] public void Shipping_scales_with_weight() => Assert.Equal(9, Pricing.Shipping(25));

    [Fact] public void Clamp_negative_to_zero() => Assert.Equal(0, Pricing.ClampNonNegative(-4));

    [Fact] public void Clamp_keeps_positive() => Assert.Equal(7, Pricing.ClampNonNegative(7));
}

public class AsyncPipelineTests
{
    [Fact] public async Task Sum_adds_all() => Assert.Equal(10, await AsyncPipeline.SumAsync([1, 2, 3, 4]));

    [Fact] public async Task Double_all() => Assert.Equal(new[] { 2, 4, 6 }, await AsyncPipeline.DoubleAllAsync([1, 2, 3]));

    [Fact] public async Task Fetch_zero() => Assert.Equal("zero", await AsyncPipeline.FetchAsync(0));

    [Fact] public async Task Fetch_item() => Assert.Equal("item-5", await AsyncPipeline.FetchAsync(5));

    [Fact] public async Task Fetch_negative_throws() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AsyncPipeline.FetchAsync(-1));
}

public class StaticCacheTests
{
    [Fact] public void Get_three() => Assert.Equal(10, StaticCache.Get(3));

    [Fact] public void Get_four() => Assert.Equal(17, StaticCache.Get(4));
}

public class StaticCacheConsumerTests
{
    // 別クラスから同じ key を読む。memo 済みなら Compute を通らない
    [Fact] public void Sum_of_cached_values() => Assert.Equal(27, StaticCache.Get(3) + StaticCache.Get(4));
}

public class StaticInitTests
{
    [Fact] public void Lookup_combines_table_and_base() => Assert.Equal(48, StaticInit.Lookup(2));

    [Fact] public void Prefix_is_upper() => Assert.Equal("CFG:", StaticInit.Prefix);
}

public class LoopsTests
{
    [Fact] public void SumTo_four() => Assert.Equal(6, Loops.SumTo(4));

    [Fact] public void CountDigits_of_number() => Assert.Equal(5, Loops.CountDigits(12345));

    [Fact] public void CountDigits_of_zero() => Assert.Equal(1, Loops.CountDigits(0));

    [Fact] public void Gcd_of_pair() => Assert.Equal(6, Loops.Gcd(12, 18));
}

public class IteratorsTests
{
    [Fact] public void Evens_up_to_six() => Assert.Equal([0, 2, 4, 6], Iterators.Evens(6));

    [Fact] public void NonEmpty_filters() => Assert.Equal(["a", "b"], Iterators.NonEmpty(["a", null, "", "b"]));
}

public class GenericsTests
{
    [Fact] public void Max_of_ints() => Assert.Equal(5, Generics.Max(3, 5));

    [Fact] public void Max_prefers_first_on_tie() => Assert.Equal("b", Generics.Max("b", "b"));

    [Fact] public void AllPositive_false_with_zero() => Assert.False(Generics.AllPositive([1, 0, 2], v => v));

    [Fact] public void AllPositive_true() => Assert.True(Generics.AllPositive([1, 2], v => v));

    [Fact] public void MapNonNull_skips_nulls() => Assert.Equal([1, 2], Generics.MapNonNull(["a", null, "bb"], s => s.Length));
}

public class PatternsTests
{
    [Theory]
    [InlineData(-1, "invalid")]
    [InlineData(0, "fail")]
    [InlineData(49, "fail")]
    [InlineData(50, "pass")]
    [InlineData(79, "pass")]
    [InlineData(80, "merit")]
    public void Grade_by_score(int score, string expected) => Assert.Equal(expected, Patterns.Grade(score));

    [Fact] public void Route_priority() => Assert.Equal("priority", Patterns.Route(new Order("a", 150m, true)));

    [Fact] public void Route_express() => Assert.Equal("express", Patterns.Route(new Order("a", 50m, true)));

    [Fact] public void Route_freight() => Assert.Equal("freight", Patterns.Route(new Order("a", 2000m, false)));

    [Fact] public void Route_standard() => Assert.Equal("standard", Patterns.Route(new Order("a", 10m, false)));

    [Fact] public void Upgrade_large_order() => Assert.True(Patterns.Upgrade(new Order("a", 600m, false)).Express);

    [Fact] public void Describe_values()
    {
        Assert.Equal(0, Patterns.Describe(null));
        Assert.Equal(2, Patterns.Describe(11));
        Assert.Equal(1, Patterns.Describe(3));
        Assert.Equal(3, Patterns.Describe("x"));
        Assert.Equal(-1, Patterns.Describe(2.5));
    }
}

public class StringsTests
{
    [Fact] public void Normalize_trims_and_lowers() => Assert.Equal("abc", Strings.Normalize("  AbC "));

    [Fact] public void IsCommand_requires_slash_and_name()
    {
        Assert.True(Strings.IsCommand("/go"));
        Assert.False(Strings.IsCommand("/"));
        Assert.False(Strings.IsCommand("go/"));
    }

    [Fact] public void Initials_of_name() => Assert.Equal("AL", Strings.Initials("ada  lovelace"));

    [Fact] public void Product_code_shape()
    {
        Assert.True(Strings.IsProductCode("ABC-1234"));
        Assert.False(Strings.IsProductCode("AB-1234"));
    }

    [Fact] public void Pad_left_with_zero() => Assert.Equal("0042", Strings.Pad("42", 4));
}

public class LinqTests
{
    [Fact] public void Top_three() => Assert.Equal(12, Linq.SumOfTopThree([1, 5, 4, 3]));

    [Fact] public void First_even() => Assert.Equal(4, Linq.FirstEvenOrDefault([1, 3, 4, 6]));

    [Fact] public void Any_negative() => Assert.True(Linq.AnyNegative([1, -1]));

    [Fact] public void Distinct_count() => Assert.Equal(3, Linq.DistinctCount([1, 1, 2, 3]));

    [Fact] public void Skip_header() => Assert.Equal([2, 3], Linq.SkipHeader([1, 2, 3]));

    [Fact] public void Min_or_zero()
    {
        Assert.Equal(0, Linq.MinOrZero([]));
        Assert.Equal(2, Linq.MinOrZero([5, 2, 9]));
    }
}

public class GuardsTests
{
    [Fact] public void Divide_ok() => Assert.Equal(3, Guards.Divide(9, 3));

    [Fact] public void Divide_by_zero_throws() => Assert.Throws<DivideByZeroException>(() => Guards.Divide(1, 0));

    [Fact] public void Required_rejects_blank() => Assert.Throws<ArgumentException>(() => Guards.Required(" ", "x"));

    [Fact] public void Required_returns_value() => Assert.Equal("v", Guards.Required("v", "x"));

    [Fact] public void SafeParse_values()
    {
        Assert.Equal(12, Guards.SafeParse("12"));
        Assert.Equal(-1, Guards.SafeParse("x"));
    }
}

public class ConstantsTests
{
    [Fact] public void Limit_is_ten() => Assert.Equal(10, Constants.Limit);

    [Fact] public void Tag_is_v1() => Assert.Equal("v1", Constants.Tag);

    [Fact] public void Scale_uses_default_factor() => Assert.Equal(6, Constants.Scale(3));

    [Fact] public void Bucket_labels()
    {
        Assert.Equal(1, Constants.Bucket(10));
        Assert.Equal(2, Constants.Bucket(11));
        Assert.Equal(0, Constants.Bucket(3));
    }

    [Fact] public void High_level() => Assert.True(Constants.IsHigh(Level.High));
}

public class NullablesTests
{
    [Fact] public void Display_name_fallback() => Assert.Equal("anon", new Profile().DisplayName("anon"));

    [Fact] public void Display_name_nickname() => Assert.Equal("n", new Profile { Nickname = "n" }.DisplayName("anon"));

    [Fact] public void Age_or_zero() => Assert.Equal(0, new Profile().AgeOrZero());

    [Fact] public void Manager_name() => Assert.Equal("m", new Profile { Manager = new Profile { Nickname = "m" } }.ManagerName());

    [Fact] public void Ensure_nickname_keeps_existing()
    {
        var p = new Profile { Nickname = "a" };
        p.EnsureNickname("b");
        Assert.Equal("a", p.Nickname);
    }
}

public class CounterTests
{
    [Fact] public void Parallel_sum_is_exact() => Assert.Equal(20000, Counter.ParallelSum(20000));
}

public class InventoryTests
{
    [Fact] public void Starts_with_apples() => Assert.Equal(3, new Inventory().Count("apple"));

    [Fact] public void Receive_adds()
    {
        var inv = new Inventory();
        inv.Receive("pear", 2);
        Assert.Equal(2, inv.Count("pear"));
    }

    [Fact] public void Receive_ignores_non_positive()
    {
        var inv = new Inventory();
        inv.Receive("pear", 0);
        Assert.Equal(0, inv.Count("pear"));
    }

    [Fact] public void Take_depletes_and_raises_event()
    {
        var inv = new Inventory();
        string? depleted = null;
        inv.Depleted += item => depleted = item;
        Assert.True(inv.Take("apple", 3));
        Assert.Equal("apple", depleted);
        Assert.Equal(0, inv.Count("apple"));
    }

    [Fact] public void Take_too_many_fails() => Assert.False(new Inventory().Take("apple", 4));

    [Fact] public void Items_sorted()
    {
        var inv = new Inventory();
        inv.Receive("banana", 1);
        Assert.Equal(["apple", "banana"], inv.Items());
    }
}
