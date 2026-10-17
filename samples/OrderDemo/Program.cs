using OrderDemo;

var service = new OrderService(limit: 1000m);
for (var id = 1; id <= 20; id++)
{
    var lines = Enumerable.Range(1, id % 4 + 1)
        .Select(n => new OrderLine($"SKU-{n}", n * id, 25m))
        .ToList();
    service.Submit(new Order(id, lines));
    Thread.Sleep(500);
}

// Phase 2 (after the 20 orders above): a customer with nested data, then an invoice built with LINQ.
var customer = new Customer(7, "Ada Lovelace", new Address("1 Analytical Way", "Istanbul", "TR"), LoyaltyTier.Gold);
customer.Tags.Add("vip");
customer.Tags.Add("beta");
customer.Discounts["SKU-2"] = 0.15m;
var customerOrders = Enumerable.Range(101, 5)
    .Select(orderId => new Order(orderId, Enumerable.Range(1, orderId % 3 + 2)
        .Select(n => new OrderLine($"SKU-{n}", n + orderId % 5, 19.99m * n))
        .ToList()))
    .ToList();
var customerJson = System.Text.Json.JsonSerializer.Serialize(customer);
Console.WriteLine($"Customer JSON: {customerJson}");
var invoice = InvoiceBuilder.Build(customer, customerOrders);
Console.WriteLine($"Invoice for {customer.Name}: subtotal {invoice.Subtotal}, discount {invoice.Discount}, tax {invoice.Tax}, total {invoice.Total}");
