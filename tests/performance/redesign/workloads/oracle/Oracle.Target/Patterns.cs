namespace Oracle.Target;

public sealed record Order(string Customer, decimal Total, bool Express);

/// pattern matching、switch 式、record
public static class Patterns
{
    public static string Grade(int score) =>
        score switch
        {
            < 0 => "invalid",
            < 50 => "fail",
            >= 50 and < 80 => "pass",
            _ => "merit",
        };

    public static string Route(Order order) =>
        order switch
        {
            { Express: true, Total: > 100m } => "priority",
            { Express: true } => "express",
            { Total: >= 1000m } => "freight",
            _ => "standard",
        };

    public static Order Upgrade(Order order) => order.Total > 500m ? order with { Express = true } : order;

    public static int Describe(object? value) =>
        value switch
        {
            null => 0,
            int i when i > 10 => 2,
            int => 1,
            string { Length: > 0 } => 3,
            _ => -1,
        };
}
