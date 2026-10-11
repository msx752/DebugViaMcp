namespace OrderDemo;

public sealed class OrderService(decimal limit)
{
    public bool Submit(Order order)
    {
        var total = Calculate(order);
        if (total > limit)
        {
            Console.WriteLine($"Order {order.Id} rejected: {total}");
            return false;
        }

        Console.WriteLine($"Order {order.Id} accepted: {total}");
        return true;
    }

    private static decimal Calculate(Order order)
    {
        decimal sum = 0;
        foreach (var line in order.Lines)
        {
            sum += line.Quantity * line.UnitPrice;
        }

        return sum;
    }
}
