namespace EmpireSim.Core.Models;

/// <summary>Religion definition with gameplay effects.</summary>
public sealed record ReligionDefinition(
    string Id,
    string Name,
    string Icon,
    string Description,
    string EffectDescription,
    double ConversionCost,
    int ConversionDays,
    // Effects
    double PopulationGrowthBonus,      // additive percentage points (e.g., 0.005)
    double ConstructionTimeMult,       // multiplicative (e.g., 0.95 = -5%)
    double SellingPriceMult,           // multiplicative (e.g., 1.05 = +5%)
    double ProductionSpeedMult);       // multiplicative (e.g., 1.05 = +5%)

/// <summary>Central religion registry.</summary>
public static class ReligionCatalog
{
    public static readonly IReadOnlyList<ReligionDefinition> All = new List<ReligionDefinition>
    {
        new("islam", "Islam", "☪️", "The faith of the Ottoman Empire and Mughals.",
            "+0.005% population growth rate",
            500000, 365, 0.005, 1.0, 1.0, 1.0),
        new("hinduism", "Hinduism", "🕉️", "The faith of India.",
            "Factory and mine construction time -5%",
            500000, 365, 0, 0.95, 1.0, 1.0),
        new("christianity", "Christianity", "✝️", "The faith of Europe.",
            "Base selling price +5%",
            500000, 365, 0, 1.0, 1.05, 1.0),
        new("buddhism", "Buddhism", "☸️", "The faith of Southeast Asia.",
            "Goods production speed +5%",
            500000, 365, 0, 1.0, 1.0, 1.05),
    };

    public static ReligionDefinition? Get(string id) =>
        All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static ReligionDefinition? GetByName(string name) =>
        All.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Religion conversion state.</summary>
public sealed class ReligionConversion
{
    public string TargetReligionId { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly ExpectedCompletion { get; set; }
    public double PaidCost { get; set; }
    public bool InProgress => !string.IsNullOrEmpty(TargetReligionId);
    public double Progress(DateOnly current)
    {
        if (!InProgress) return 0;
        int total = ExpectedCompletion.DayNumber - StartDate.DayNumber;
        if (total <= 0) return 1;
        int done = current.DayNumber - StartDate.DayNumber;
        return Math.Clamp((double)done / total, 0, 1);
    }
    public int DaysLeft(DateOnly current) =>
        InProgress ? Math.Max(0, ExpectedCompletion.DayNumber - current.DayNumber) : 0;
}

/// <summary>Central religion service for active effects.</summary>
public static class ReligionService
{
    /// <summary>Get the active religion definition for a nation.</summary>
    public static ReligionDefinition? GetActive(Nation nation)
    {
        return ReligionCatalog.GetByName(nation.Religion);
    }

    /// <summary>Population growth bonus (percentage points).</summary>
    public static double PopulationGrowthBonus(Nation nation) =>
        GetActive(nation)?.PopulationGrowthBonus ?? 0;

    /// <summary>Construction time multiplier.</summary>
    public static double ConstructionTimeMult(Nation nation) =>
        GetActive(nation)?.ConstructionTimeMult ?? 1.0;

    /// <summary>Selling price multiplier.</summary>
    public static double SellingPriceMult(Nation nation) =>
        GetActive(nation)?.SellingPriceMult ?? 1.0;

    /// <summary>Production speed multiplier.</summary>
    public static double ProductionSpeedMult(Nation nation) =>
        GetActive(nation)?.ProductionSpeedMult ?? 1.0;
}
