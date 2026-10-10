namespace EmpireSim.Core.Models;

/// <summary>
/// A technology: bought with research points, then permanently feeds one existing multiplier
/// (1.0 = no effect). Only the player banks research, so AI countries are never affected.
/// </summary>
public sealed record TechnologySpec(
    string Id,
    string Name,
    string Icon,
    string Description,
    string Effect,
    double Cost,
    double BattleStrength = 1.0,
    double TradeIncome = 1.0,
    double Production = 1.0);

/// <summary>The short list of technologies.</summary>
public static class TechnologyCatalog
{
    public static readonly IReadOnlyList<TechnologySpec> All = new List<TechnologySpec>
    {
        new("drill_manuals", "Drill Manuals", "📘", "Standard drill turns levies into line infantry.",
            "+5% battle strength", 400, BattleStrength: 1.05),
        new("merchant_law", "Merchant Law", "⚖️", "Clear commercial law makes every trade pay.",
            "+10% trade income", 600, TradeIncome: 1.10),
        new("improved_tools", "Improved Tools", "🔧", "Better tools raise what every mill produces.",
            "+5% production output", 800, Production: 1.05),
    };

    public static TechnologySpec? Get(string id) => All.FirstOrDefault(t => t.Id == id);
}
