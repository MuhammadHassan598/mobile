namespace EmpireSim.Core.Models;

/// <summary>
/// Military item crafting recipe. Items are crafted from minerals (not built as mills).
/// Each recipe produces 10 units.
/// </summary>
public sealed record MilitaryRecipe(
    string Id,
    string Name,
    string Icon,
    string Produces, // e.g., "Helmets"
    double WoodCost,
    double StoneCost,
    double IronCost,
    double CopperCost,
    double LeadCost,
    double Days, // production time in days
    string Condition
);

/// <summary>Military crafting recipes (6 items, batch of 10).</summary>
public static class MilitaryRecipes
{
    public static readonly IReadOnlyList<MilitaryRecipe> All = new List<MilitaryRecipe>
    {
        new("helmet", "Helmet", "🪖", "Helmets",
            WoodCost: 10, StoneCost: 0, IronCost: 0, CopperCost: 1, LeadCost: 0,
            Days: 1, Condition: "Required for recruitment"),
        new("dagger", "Dagger", "🗡️", "Daggers",
            WoodCost: 10, StoneCost: 0, IronCost: 2, CopperCost: 0, LeadCost: 0,
            Days: 1, Condition: "Required for recruitment"),
        new("pike", "Pike", "🔱", "Pikes",
            WoodCost: 12, StoneCost: 0, IronCost: 0, CopperCost: 1, LeadCost: 0,
            Days: 1, Condition: "Required for recruitment"),
        new("shotgun", "Shotgun", "🔫", "Shotguns",
            WoodCost: 10, StoneCost: 0, IronCost: 5, CopperCost: 2, LeadCost: 0,
            Days: 1, Condition: "Required for recruitment"),
        new("arquebus", "Arquebus", "🏹", "Arquebuses",
            WoodCost: 10, StoneCost: 0, IronCost: 6, CopperCost: 0, LeadCost: 0,
            Days: 1, Condition: "Required for recruitment"),
        new("shipparts", "Ship Parts", "⚓", "Ship Parts",
            WoodCost: 50, StoneCost: 20, IronCost: 4, CopperCost: 3, LeadCost: 2,
            Days: 2, Condition: "Required for recruitment"),
    };

    public static MilitaryRecipe Get(string id) => All.First(r => r.Id == id);

    public static string RequirementsText(MilitaryRecipe r)
    {
        var parts = new List<string>();
        if (r.WoodCost > 0) parts.Add($"Wood: {r.WoodCost:N0}");
        if (r.StoneCost > 0) parts.Add($"Stone: {r.StoneCost:N0}");
        if (r.IronCost > 0) parts.Add($"Iron: {r.IronCost:N0}");
        if (r.CopperCost > 0) parts.Add($"Copper: {r.CopperCost:N0}");
        if (r.LeadCost > 0) parts.Add($"Lead: {r.LeadCost:N0}");
        return string.Join(", ", parts);
    }
}

/// <summary>A military item batch under production.</summary>
public sealed class MilitaryCraftProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RecipeId { get; set; } = "";
    public double DaysLeft { get; set; }
    public double TotalDays { get; set; }
    public double Progress => TotalDays <= 0 ? 1 : 1 - DaysLeft / TotalDays;
}
