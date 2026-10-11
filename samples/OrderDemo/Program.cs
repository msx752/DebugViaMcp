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
