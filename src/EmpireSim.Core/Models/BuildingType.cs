namespace EmpireSim.Core.Models;

/// <summary>Buildable structures. Each has a cost, a build time and a daily effect.</summary>
public enum BuildingType
{
    Farm,
    Mine,
    Sawmill,
    Workshop
}

/// <summary>Static definition of a building: cost, build time, description.</summary>
public sealed record BuildingSpec(
    BuildingType Type,
    string Name,
    string Description,
    double GoldCost,
    double WoodCost,
    double IronCost,
    int BuildDays);

/// <summary>The catalogue of everything the player can construct.</summary>
public static class BuildingCatalog
{
    public static readonly IReadOnlyList<BuildingSpec> All = new List<BuildingSpec>
    {
        new(BuildingType.Farm, "Farm",
            "Produces food every day to feed your people.",
            GoldCost: 100, WoodCost: 0, IronCost: 0, BuildDays: 5),
        new(BuildingType.Mine, "Mine",
            "Produces iron every day for workshops and construction.",
            GoldCost: 250, WoodCost: 0, IronCost: 0, BuildDays: 10),
        new(BuildingType.Sawmill, "Sawmill",
            "Produces wood every day for workshops and construction.",
            GoldCost: 200, WoodCost: 0, IronCost: 0, BuildDays: 8),
        new(BuildingType.Workshop, "Workshop",
            "Turns 2 wood + 1 iron into 1 goods every day. Goods are sold for gold.",
            GoldCost: 400, WoodCost: 10, IronCost: 5, BuildDays: 12),
    };

    public static BuildingSpec Get(BuildingType type) =>
        All.First(s => s.Type == type);

    public static string CostText(BuildingSpec spec)
    {
        var parts = new List<string> { Currency.Cost(spec.GoldCost) };
        if (spec.WoodCost > 0) parts.Add($"{spec.WoodCost:N0} wood");
        if (spec.IronCost > 0) parts.Add($"{spec.IronCost:N0} iron");
        return string.Join(" + ", parts) + $" · {spec.BuildDays} days";
    }
}
