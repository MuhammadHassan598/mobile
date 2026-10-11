namespace EmpireSim.Core.Models;

/// <summary>Tradeable product definition.</summary>
public sealed record TradeProduct(
    string Id,
    string Name,
    string Icon,
    string Category, // "Equipment", "Resource", "FoodGoods"
    double BasePricePer1000,
    bool CanBuy = true,
    bool CanSell = true);

/// <summary>Catalogue of tradeable products.</summary>
public static class TradeCatalog
{
    public static readonly IReadOnlyList<TradeProduct> All = new List<TradeProduct>
    {
        // Equipment
        new("helmet", "Helmet", "🪖", "Equipment", 500),
        new("dagger", "Dagger", "🗡️", "Equipment", 300),
        new("pike", "Pike", "🔱", "Equipment", 400),
        new("shotgun", "Shotgun", "🔫", "Equipment", 800),
        new("arquebus", "Arquebus", "🎯", "Equipment", 1000),
        new("shipparts", "Ship Parts", "🔧", "Equipment", 2000),
        // Resources
        new("wood", "Wood", "🪵", "Resource", 100),
        new("stone", "Stone", "🪨", "Resource", 80),
        new("iron", "Iron", "⛓️", "Resource", 300),
        new("copper", "Copper", "🟤", "Resource", 250),
        new("lead", "Lead", "⚫", "Resource", 200),
        // Food & goods (one product per consumed item; Name = the stock key in GoodsInventory)
        new("wheat", "Wheat", "🌾", "FoodGoods", 60),
        new("flour", "Flour", "🥣", "FoodGoods", 90),
        new("bread", "Bread", "🍞", "FoodGoods", 140),
        new("milk", "Milk", "🥛", "FoodGoods", 100),
        new("meat", "Meat", "🥩", "FoodGoods", 220),
        new("salt", "Salt", "🧂", "FoodGoods", 180),
        new("wool", "Wool", "🐑", "FoodGoods", 160),
        new("fur", "Fur", "🦊", "FoodGoods", 400),
        new("clothing", "Clothing", "🧵", "FoodGoods", 350),
        new("hats", "Hats", "🎩", "FoodGoods", 450),
        new("horses", "Horses", "🐎", "FoodGoods", 800),
        new("perfume", "Perfume", "🌸", "FoodGoods", 900),
    };

    public static TradeProduct? Get(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>The three groups the Trade screen sorts products into, in display order.</summary>
    public static readonly IReadOnlyList<TradeGroup> Groups = new List<TradeGroup>
    {
        new("military", "Military", "⚔️", "Equipment"),
        new("food", "Food", "🍞", "FoodGoods"),
        new("minerals", "Minerals", "⛏️", "Resource"),
    };

    /// <summary>The products of a group, in catalogue order.</summary>
    public static IEnumerable<TradeProduct> InGroup(TradeGroup group) => All.Where(p => p.Category == group.Category);
}

/// <summary>
/// A group of products on the Trade screen: military equipment, food (and the everyday goods that go with it:
/// clothing, wool, fur, horses...) and minerals (the raw materials: wood, stone and the metals).
/// </summary>
public sealed record TradeGroup(string Key, string Title, string Icon, string Category);

/// <summary>Trade contract status.</summary>
public enum TradeStatus
{
    Confirmed,
    InTransit,
    Delivered,
    Cancelled
}

/// <summary>A buy/sell contract between countries.</summary>
public sealed class TradeContract
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BuyerId { get; set; } = "";
    public string SellerId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public double Quantity { get; set; }
    public double PricePer1000 { get; set; }
    public double TotalValue { get; set; }
    public DateOnly CreatedDate { get; set; }
    public DateOnly DeliveryDate { get; set; }
    public DateOnly? ActualDeliveryDate { get; set; }
    public TradeStatus Status { get; set; }
    public bool IsPlayerBuyer { get; set; }
}

/// <summary>Market pricing: base prices with deterministic country modifiers.</summary>
public static class MarketPricing
{
    /// <summary>Price per 1000 units for a product from a specific country.</summary>
    public static double PricePer1000(string productId, string countryId, Nation? seller = null)
    {
        var product = TradeCatalog.Get(productId);
        if (product is null) return 0;
        // Deterministic modifier 0.85 - 1.15 based on IDs
        int hash = (productId + "|" + countryId).GetHashCode();
        double mod = 0.85 + (Math.Abs(hash) % 31) / 100.0;
        double price = product.BasePricePer1000 * mod;
        // Christianity: +5% selling price
        if (seller is not null)
            price *= ReligionService.SellingPriceMult(seller);
        return Math.Round(price, 2);
    }

    /// <summary>Total value = pricePer1000 * quantity / 1000.</summary>
    public static double TotalValue(double pricePer1000, double quantity)
    {
        return Math.Round(pricePer1000 * quantity / 1000, 2);
    }

    /// <summary>Delivery days based on deterministic "distance".</summary>
    public static int DeliveryDays(string buyerId, string sellerId)
    {
        int hash = (buyerId + "|" + sellerId).GetHashCode();
        return 3 + (Math.Abs(hash) % 5); // 3-7 days
    }
}
