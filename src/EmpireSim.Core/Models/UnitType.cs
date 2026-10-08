namespace EmpireSim.Core.Models;

/// <summary>Land unit types available for recruitment.</summary>
public enum UnitType
{
    Musketeer,
    Pikeman,
    Cavalry,
    Cannon
}

/// <summary>Static definition of a unit type: costs, daily upkeep and battle strength.</summary>
public sealed record UnitSpec(
    UnitType Type,
    string Name,
    string Icon,
    string Description,
    double GoldCost,
    double WoodCost,
    double IronCost,
    double UpkeepPerDay,
    double Strength);

/// <summary>A stack of units of one type in a nation's army.</summary>
public sealed class UnitStack
{
    public UnitType Type { get; set; }
    public int Count { get; set; }
}

/// <summary>Catalogue of recruitable land units.</summary>
public static class UnitCatalog
{
    public static readonly IReadOnlyList<UnitSpec> All = new List<UnitSpec>
    {
        new(UnitType.Musketeer, "Musketeer", "🎯", "Line infantry with muskets. The backbone of the army.",
            GoldCost: 20, WoodCost: 0, IronCost: 0, UpkeepPerDay: 0.03, Strength: 1.0),
        new(UnitType.Pikeman, "Pikeman", "🔱", "Cheap spear infantry. Holds the line.",
            GoldCost: 15, WoodCost: 0, IronCost: 0, UpkeepPerDay: 0.025, Strength: 0.8),
        new(UnitType.Cavalry, "Cavalry", "🐎", "Fast horsemen for flanking.",
            GoldCost: 50, WoodCost: 0, IronCost: 0, UpkeepPerDay: 0.06, Strength: 1.2),
        new(UnitType.Cannon, "Cannon", "💣", "Siege artillery. Expensive but devastating.",
            GoldCost: 200, WoodCost: 0, IronCost: 5, UpkeepPerDay: 0.15, Strength: 2.5),
    };

    public static UnitSpec Get(UnitType type) => All.First(s => s.Type == type);

    /// <summary>Seeds a starting army with a historical-ish mix.</summary>
    public static List<UnitStack> SeedArmy(int totalSoldiers) => new()
    {
        new UnitStack { Type = UnitType.Musketeer, Count = totalSoldiers * 55 / 100 },
        new UnitStack { Type = UnitType.Pikeman,   Count = totalSoldiers * 30 / 100 },
        new UnitStack { Type = UnitType.Cavalry,   Count = totalSoldiers * 13 / 100 },
        new UnitStack { Type = UnitType.Cannon,    Count = totalSoldiers * 2 / 100 },
    };

    public static string CostText(UnitSpec spec)
    {
        var parts = new List<string> { Currency.Cost(spec.GoldCost) };
        if (spec.WoodCost > 0) parts.Add($"{spec.WoodCost:N0} wood");
        if (spec.IronCost > 0) parts.Add($"{spec.IronCost:N0} iron");
        return string.Join(" + ", parts);
    }
}

/// <summary>Warship definition (naval units are tracked separately from land stacks).</summary>
public static class WarshipSpec
{
    public const string Name = "Warship";
    public const string Icon = "🚢";
    public const double GoldCost = 500;
    public const double WoodCost = 20;
    public const double IronCost = 10;
    public const double UpkeepPerDay = 0.5;

    public static string CostText =>
        $"{Currency.Cost(GoldCost)} + {WoodCost:N0} wood + {IronCost:N0} iron";
}
