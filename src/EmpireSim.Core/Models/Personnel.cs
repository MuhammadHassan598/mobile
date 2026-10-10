namespace EmpireSim.Core.Models;

/// <summary>Personnel type definition for recruitment.</summary>
public sealed record PersonnelDefinition(
    string Id,
    string Name,
    string Icon,
    string Description,
    double GoldCostPerPerson,
    string[] EligibleSources);

/// <summary>Registry of recruitable personnel.</summary>
public static class PersonnelCatalog
{
    public static readonly IReadOnlyList<PersonnelDefinition> All = new List<PersonnelDefinition>
    {
        new("military", "Military Personnel", "⚔️",
            "Train civilians as soldiers. They join your reserves.",
            0.5, new[] { "peasants", "craftsmen" }),
        new("spy", "Spies", "🕵️",
            "Recruit intelligence operatives for espionage.",
            2.0, new[] { "peasants", "craftsmen", "merchants" }),
        new("saboteur", "Saboteurs", "💣",
            "Recruit saboteurs for covert operations.",
            2.0, new[] { "peasants", "craftsmen" }),
    };

    public static PersonnelDefinition? Get(string id) => All.FirstOrDefault(p => p.Id == id);
}

/// <summary>Population/personnel service.</summary>
public static class PopulationService
{
    /// <summary>
    /// People born per day: population x (base growth x growth multiplier + religion bonus).
    /// Used by the daily tick and the Statistics screen so they always agree.
    /// </summary>
    public static long DailyBirths(Nation nation)
    {
        double rate = Services.Balance.GrowthPerDayWithSurplus * nation.GrowthMult
                    + ReligionService.PopulationGrowthBonus(nation) / 100.0;   // Islam: +0.005 points
        return (long)(nation.Population * rate);
    }

    /// <summary>Get workforce count by group key.</summary>
    public static long GetGroupCount(Workforce w, string key) => key switch
    {
        "peasants" => w.Peasants,
        "craftsmen" => w.Craftsmen,
        "military" => w.MilitaryPersonnel,
        "merchants" => w.Merchants,
        "spies" => w.Spies,
        "saboteurs" => w.Saboteurs,
        _ => 0
    };

    /// <summary>Transfer population between groups. Returns error or null.</summary>
    public static string? Transfer(Workforce w, string from, string to, long count)
    {
        if (count <= 0) return "Invalid quantity.";
        long available = GetGroupCount(w, from);
        if (available < count) return $"Not enough {from} (have {available:N0}).";

        AddToGroup(w, from, -count);
        AddToGroup(w, to, count);
        return null;
    }

    private static void AddToGroup(Workforce w, string key, long delta)
    {
        switch (key)
        {
            case "peasants": w.Peasants = Math.Max(0, w.Peasants + delta); break;
            case "craftsmen": w.Craftsmen = Math.Max(0, w.Craftsmen + delta); break;
            case "military": w.MilitaryPersonnel = Math.Max(0, w.MilitaryPersonnel + delta); break;
            case "merchants": w.Merchants = Math.Max(0, w.Merchants + delta); break;
            case "spies": w.Spies = Math.Max(0, w.Spies + delta); break;
            case "saboteurs": w.Saboteurs = Math.Max(0, w.Saboteurs + delta); break;
        }
    }
}
