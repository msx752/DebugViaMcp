namespace OrderDemo;

public sealed class Order(int id, IReadOnlyList<OrderLine> lines)
{
    public int Id { get; } = id;
    public IReadOnlyList<OrderLine> Lines { get; } = lines;
    public string Note { get; set; } = new string('x', 5000);
    public int TouchCount { get; private set; }

    /// <summary>Has a visible side effect so the spike can detect whether evaluation mutated state.</summary>
    public int Touch() => ++TouchCount;
}

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
