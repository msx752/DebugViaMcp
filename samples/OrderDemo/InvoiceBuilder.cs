namespace OrderDemo;

public sealed record InvoiceLine(string Sku, int Quantity, decimal Net, decimal Discount);

public sealed record Invoice(int CustomerId, IReadOnlyList<InvoiceLine> Lines, decimal Subtotal, decimal Discount, decimal Tax, decimal Total);

public static class InvoiceBuilder
{
    private const decimal TaxRate = 0.18m;

    public static Invoice Build(Customer customer, IReadOnlyList<Order> orders)
    {
        // One invoice line per SKU across all orders.
        var grouped = orders
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.Sku)
            .Select(g => new { Sku = g.Key, Quantity = g.Sum(l => l.Quantity), Net = g.Sum(l => l.Quantity * l.UnitPrice) })
            .OrderBy(g => g.Sku)
            .ToList();

        var tierRate = TierRate(customer.Tier);
        var lines = new List<InvoiceLine>();
        foreach (var g in grouped)
        {
            var skuRate = customer.Discounts.TryGetValue(g.Sku, out var rate) ? rate : 0m;
            var afterSku = g.Net - decimal.Round(g.Net * skuRate, 2);
            var discount = g.Net - decimal.Round(afterSku * (1 - tierRate), 2);
            lines.Add(new InvoiceLine(g.Sku, g.Quantity, decimal.Round(g.Net, 2), discount));
        }

        var subtotal = lines.Sum(l => l.Net);
        var totalDiscount = lines.Sum(l => l.Discount);
        var tax = decimal.Round((subtotal - totalDiscount) * TaxRate, 2);
        return new Invoice(customer.Id, lines, subtotal, totalDiscount, tax, subtotal - totalDiscount + tax);
    }

    private static decimal TierRate(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Gold => 0.10m,
        LoyaltyTier.Silver => 0.05m,
        _ => 0m,
    };
}
