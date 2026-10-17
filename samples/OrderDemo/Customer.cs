namespace OrderDemo;

public enum LoyaltyTier
{
    Standard,
    Silver,
    Gold,
}

public sealed record Address(string Street, string City, string Country);

public sealed class Customer(int id, string name, Address address, LoyaltyTier tier)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public Address Address { get; } = address;
    public LoyaltyTier Tier { get; } = tier;
    public List<string> Tags { get; } = [];

    /// <summary>Extra discount rate per SKU, applied before the loyalty discount.</summary>
    public Dictionary<string, decimal> Discounts { get; } = [];
}
