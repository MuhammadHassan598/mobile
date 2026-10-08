namespace EmpireSim.Core.Models;

/// <summary>Production building categories.</summary>
public enum ProductionCategory
{
    Military,
    Food,
    Minerals
}

/// <summary>
/// A production building definition. Buildings produce goods/resources daily.
/// </summary>
public sealed record ProductionBuildingSpec(
    string Id,
    string Name,
    string Icon,
    string Produces,
    ProductionCategory Category,
    double GoldCost,
    double WoodCost,
    double StoneCost,
    double IronCost,
    int BuildDays,
    double OutputPerDay
);

/// <summary>All production buildings, grouped by category.</summary>
public static class ProductionCatalog
{
    public static readonly IReadOnlyList<ProductionBuildingSpec> All = new List<ProductionBuildingSpec>
    {
        // ---- Military ----
        new("helmet", "Helmet Workshop", "🪖", "Helmets", ProductionCategory.Military, 2, 0, 0, 1, 5, 2),
        new("dagger", "Dagger Forge", "🗡️", "Daggers", ProductionCategory.Military, 2, 0, 0, 1, 5, 2),
        new("pike", "Pike Workshop", "🔱", "Pikes", ProductionCategory.Military, 3, 2, 0, 1, 6, 2),
        new("shotgun", "Gunsmith", "🔫", "Shotguns", ProductionCategory.Military, 5, 2, 0, 3, 8, 1),
        new("shipparts", "Shipyard", "⚓", "Ship Parts", ProductionCategory.Military, 8, 10, 0, 5, 10, 1),
        new("arquebus", "Arquebus Workshop", "🏹", "Arquebuses", ProductionCategory.Military, 4, 1, 0, 2, 7, 1),

        // ---- Food ----
        new("saltmine", "Salt Mine", "🧂", "Salt", ProductionCategory.Food, 3, 0, 5, 0, 6, 5),
        new("tailoring", "Tailoring Workshop", "🧵", "Clothing", ProductionCategory.Food, 2, 2, 3, 0, 5, 3),
        new("hatworkshop", "Hat Workshop", "🎩", "Hats", ProductionCategory.Food, 2, 2, 3, 0, 5, 2),
        new("furfarm", "Fur Farm", "🦊", "Fur", ProductionCategory.Food, 2, 1, 2, 0, 5, 3),
        new("bakery", "Bakery", "🍞", "Bread", ProductionCategory.Food, 3, 2, 5, 0, 6, 10),
        new("cattlefarm", "Cattle Farm", "🐄", "Meat", ProductionCategory.Food, 3, 1, 2, 0, 6, 8),
        new("farm", "Farm", "🌾", "Wheat", ProductionCategory.Food, 1, 0, 0, 0, 4, 10),
        new("studfarm", "Stud Farm", "🐎", "Horses", ProductionCategory.Food, 4, 2, 3, 0, 7, 2),
        new("cowfarm", "Cow Farm", "🥛", "Milk", ProductionCategory.Food, 2, 1, 2, 0, 5, 6),
        new("perfume", "Perfume Workshop", "🌸", "Perfume", ProductionCategory.Food, 5, 1, 4, 0, 8, 1),
        new("sheepfarm", "Sheep Farm", "🐑", "Wool", ProductionCategory.Food, 2, 1, 2, 0, 5, 4),
        new("flourmill", "Flour Mill", "🌾", "Flour", ProductionCategory.Food, 3, 3, 5, 0, 6, 8),

        // ---- Minerals ----
        new("ironmine", "Iron Mine", "⛏️", "Iron", ProductionCategory.Minerals, 3, 2, 5, 0, 6, 5),
        new("copperminer", "Copper Mine", "🟤", "Copper", ProductionCategory.Minerals, 3, 2, 5, 0, 6, 4),
        new("leadminer", "Lead Mine", "⚫", "Lead", ProductionCategory.Minerals, 3, 2, 5, 0, 6, 4),
        new("goldmine", "Gold Mine", "🪙", "Gold", ProductionCategory.Minerals, 5, 3, 8, 0, 8, 2),
        new("sawmill", "Sawmill", "🪵", "Wood", ProductionCategory.Minerals, 2, 0, 3, 0, 5, 8),
        new("stonequarry", "Stone Quarry", "🪨", "Stone", ProductionCategory.Minerals, 2, 2, 0, 0, 5, 6),
    };

    public static ProductionBuildingSpec Get(string id) =>
        All.First(s => s.Id == id);

    public static IReadOnlyList<ProductionBuildingSpec> ByCategory(ProductionCategory cat) =>
        All.Where(s => s.Category == cat).ToList();

    public static string CostText(ProductionBuildingSpec spec)
    {
        var parts = new List<string> { Currency.Cost(spec.GoldCost) };
        if (spec.WoodCost > 0) parts.Add($"{spec.WoodCost:N0} wood");
        if (spec.StoneCost > 0) parts.Add($"{spec.StoneCost:N0} stone");
        if (spec.IronCost > 0) parts.Add($"{spec.IronCost:N0} iron");
        return string.Join(" + ", parts);
    }
}
