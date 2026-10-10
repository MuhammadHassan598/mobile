using EmpireSim.Core.Services;

namespace EmpireSim.Core.Models;

/// <summary>National law definition.</summary>
public sealed record LawDefinition(
    string Id,
    string Name,
    string Icon,
    string Category, // "Economic" or "Production"
    string PolicyGroup, // trade, production_priority, mobilization, construction, diplomatic, general
    double SelectionCost,
    string[] Advantages,
    string[] Disadvantages,
    // Effect modifiers (multiplicative, 1.0 = no change)
    double ExportRevenueMult = 1.0,
    double FoodOutputMult = 1.0,
    double ResourceOutputMult = 1.0,
    double MilitaryGoodsMult = 1.0,
    double ImportPriceMult = 1.0,
    double RelationshipGainMult = 1.0,
    double ConstructionTimeMult = 1.0,
    double ConstructionCostMult = 1.0,
    double CivilianProdSpeedMult = 1.0,
    double GeneralProdOutputMult = 1.0,
    double RecruitmentTimeMult = 1.0,
    double MilitaryMaintenanceMult = 1.0,
    double ReserveTimeMult = 1.0,
    double CivilianOutputMult = 1.0);

/// <summary>Central law registry - 11 canonical laws.</summary>
public static class LawCatalog
{
    public static readonly IReadOnlyList<LawDefinition> All = new List<LawDefinition>
    {
        new("export_trade", "Flourishing Export Trade", "📤", "Economic", "trade", 2000,
            new[] { "+15% export selling revenue" },
            new[] { "-10% domestic availability for export goods" },
            ExportRevenueMult: 1.15),
        new("peacetime", "Peacetime", "🕊️", "Economic", "production_priority", 2000,
            new[] { "+15% food production", "+15% resource production" },
            new[] { "-20% military-goods production" },
            FoodOutputMult: 1.15, ResourceOutputMult: 1.15, MilitaryGoodsMult: 0.80),
        new("import_trade", "Flourishing Import Trade", "📥", "Economic", "trade", 2000,
            new[] { "+15% import availability", "-10% import price" },
            new[] { "+10% import dependence" },
            ImportPriceMult: 0.90),
        new("improved_diplomacy", "Improved Diplomacy", "🤝", "Economic", "diplomatic", 2000,
            new[] { "+15% relationship gains" },
            new[] { "+10% aggressive action costs" },
            RelationshipGainMult: 1.15),
        new("accelerated_construction", "Accelerated Construction", "🏗️", "Economic", "construction", 3000,
            new[] { "-20% construction time" },
            new[] { "+15% construction material cost" },
            ConstructionTimeMult: 0.80, ConstructionCostMult: 1.15),
        new("civilian_production", "Civilian Production", "🏭", "Economic", "production_priority", 2000,
            new[] { "+15% civilian production speed", "+10% food/resource output" },
            new[] { "-15% military-goods production speed" },
            CivilianProdSpeedMult: 1.15, FoodOutputMult: 1.10, ResourceOutputMult: 1.10, MilitaryGoodsMult: 0.85),
        // Production page
        new("production_rate", "Production Rate", "⚙️", "Production", "general", 2000,
            new[] { "+10% production output" },
            new[] { "+10% input consumption" },
            GeneralProdOutputMult: 1.10),
        new("military_production", "Military Production", "⚔️", "Production", "production_priority", 3000,
            new[] { "+15% military-goods production speed" },
            new[] { "-20% food/resource production speed" },
            MilitaryGoodsMult: 1.15, FoodOutputMult: 0.80, ResourceOutputMult: 0.80),
        new("early_mobilization", "Early Mobilization", "🚨", "Production", "mobilization", 3000,
            new[] { "-25% recruitment time" },
            new[] { "+10% military maintenance" },
            RecruitmentTimeMult: 0.75, MilitaryMaintenanceMult: 1.10),
        new("limited_mobilization", "Limited Mobilization", "📋", "Production", "mobilization", 2000,
            new[] { "-10% reserve activation time" },
            new[] { "Max 25% reserves mobilizable" },
            ReserveTimeMult: 0.90),
        new("full_mobilization", "Full Mobilization", "🔥", "Production", "mobilization", 5000,
            new[] { "+30% mobilization capacity", "-40% reserve time" },
            new[] { "-25% civilian output", "+20% military maintenance" },
            ReserveTimeMult: 0.60, CivilianOutputMult: 0.75, MilitaryMaintenanceMult: 1.20),
    };

    public static LawDefinition? Get(string id) => All.FirstOrDefault(l => l.Id == id);

    public static IEnumerable<LawDefinition> ByCategory(string category) =>
        All.Where(l => l.Category == category);
}

/// <summary>Central law service for active modifiers.</summary>
public static class LawService
{
    /// <summary>Get combined modifier for a specific effect across active laws.</summary>
    public static double GetModifier(Nation nation, Func<LawDefinition, double> selector)
    {
        double result = 1.0;
        foreach (var lawId in nation.ActiveLaws)
        {
            var law = LawCatalog.Get(lawId);
            if (law is not null)
                result *= selector(law);
        }
        return result;
    }

    public static double ExportRevenueMult(Nation n) => GetModifier(n, l => l.ExportRevenueMult);
    public static double FoodOutputMult(Nation n) => GetModifier(n, l => l.FoodOutputMult);
    public static double ResourceOutputMult(Nation n) => GetModifier(n, l => l.ResourceOutputMult);
    public static double MilitaryGoodsMult(Nation n) => GetModifier(n, l => l.MilitaryGoodsMult);
    public static double ImportPriceMult(Nation n) => GetModifier(n, l => l.ImportPriceMult);
    public static double RelationshipGainMult(Nation n) => GetModifier(n, l => l.RelationshipGainMult);
    public static double ConstructionTimeMult(Nation n) => GetModifier(n, l => l.ConstructionTimeMult);
    public static double ConstructionCostMult(Nation n) => GetModifier(n, l => l.ConstructionCostMult);
    public static double RecruitmentTimeMult(Nation n) => GetModifier(n, l => l.RecruitmentTimeMult);
    public static double MilitaryMaintenanceMult(Nation n) => GetModifier(n, l => l.MilitaryMaintenanceMult);
    /// <summary>Production output from laws and from researched technology (1.0 with neither).</summary>
    public static double GeneralProdOutputMult(Nation n) =>
        GetModifier(n, l => l.GeneralProdOutputMult) * ResearchService.ProductionMult(n);
}
