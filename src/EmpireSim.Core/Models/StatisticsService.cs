namespace EmpireSim.Core.Models;

/// <summary>Central statistics queries (read-only reporting).</summary>
public static class StatisticsService
{
    /// <summary>Daily income breakdown.</summary>
    public static List<(string Icon, string Name, double Amount)> IncomeBreakdown(Nation n)
    {
        n.EnsureTaxationInitialized();
        return new List<(string, string, double)>
        {
            ("💰", "Tax Income", TaxationService.TotalRevenue(n.Workforce, n.TaxRates)),
            ("🏰", "Crown Domain", Services.Balance.CrownDomainIncomePerDay),
            ("⛏️", "Gold Mine", n.ProductionBuildings
                .Where(kvp => ProductionCatalog.Get(kvp.Key)?.Produces == "Gold")
                .Sum(kvp => kvp.Value * (ProductionCatalog.Get(kvp.Key)?.OutputPerDay ?? 0))),
        };
    }

    /// <summary>Daily expense breakdown.</summary>
    public static List<(string Icon, string Name, double Amount)> ExpenseBreakdown(Nation n)
    {
        double upkeep = 0;
        foreach (var s in n.Units)
            upkeep += UnitCatalog.Get(s.Type).UpkeepPerDay * s.Count;
        upkeep += n.Warships * WarshipSpec.UpkeepPerDay;
        upkeep *= LawService.MilitaryMaintenanceMult(n);
        double wages = n.Commanders.Sum(c => c.DailyWage);
        return new List<(string, string, double)>
        {
            ("⚔️", "Troop Upkeep", upkeep),
            ("🎖️", "Commanders", wages),
        };
    }

    /// <summary>Daily production by product.</summary>
    public static List<(string Icon, string Name, double PerDay, string Category)> DailyProduction(Nation n)
    {
        var result = new List<(string, string, double, string)>();
        double prodMult = ReligionService.ProductionSpeedMult(n);
        double foodMult = LawService.FoodOutputMult(n);
        double resMult = LawService.ResourceOutputMult(n);
        double genMult = LawService.GeneralProdOutputMult(n);

        foreach (var kvp in n.ProductionBuildings)
        {
            var spec = ProductionCatalog.Get(kvp.Key);
            if (spec is null) continue;
            double catMult = spec.Category == ProductionCategory.Food ? foodMult
                           : spec.Category == ProductionCategory.Minerals ? resMult : 1.0;
            double output = kvp.Value * spec.OutputPerDay * prodMult * catMult * genMult;
            string cat = spec.Category == ProductionCategory.Food ? "Food"
                       : spec.Category == ProductionCategory.Minerals ? "Resources" : "Other";
            result.Add((spec.Icon, spec.Produces, output, cat));
        }
        // Military crafting (average per day based on active projects)
        foreach (var proj in n.MilitaryCraftQueue)
        {
            var recipe = MilitaryRecipes.Get(proj.RecipeId);
            double perDay = 10.0 / Math.Max(1, proj.TotalDays) * LawService.MilitaryGoodsMult(n);
            result.Add((recipe.Icon, recipe.Name, perDay, "Military"));
        }
        return result;
    }

    /// <summary>Average market prices per 1000 units.</summary>
    public static List<(string Icon, string Name, double Price, string Category)> AveragePrices(GameState state)
    {
        var result = new List<(string, string, double, string)>();
        var nations = state.AllNations().ToList();
        foreach (var p in TradeCatalog.All)
        {
            double sum = 0;
            int count = 0;
            foreach (var n in nations)
            {
                sum += MarketPricing.PricePer1000(p.Id, n.Id);
                count++;
            }
            double avg = count > 0 ? sum / count : 0;
            result.Add((p.Icon, p.Name, avg, p.Category));
        }
        return result;
    }

    /// <summary>Victory progress.</summary>
    public static (int Captured, int CapturedTarget, int Religious, int ReligiousTarget, int Colonies, int ColoniesTarget)
        VictoryProgress(GameState state)
    {
        var player = state.PlayerNation;
        int captured = state.NationsAnnexedByPlayer;
        int capturedTarget = 20; // configurable

        var playerRel = ReligionCatalog.GetByName(player.Religion);
        int religious = 0;
        if (playerRel is not null)
        {
            religious = state.AllNations().Count(n =>
                n.Religion.Equals(playerRel.Name, StringComparison.OrdinalIgnoreCase));
        }
        int religiousTarget = 20;
        int colonies = player.ColoniesFounded;
        int coloniesTarget = 5;

        return (captured, capturedTarget, religious, religiousTarget, colonies, coloniesTarget);
    }

    /// <summary>Tax tolerance per group (0-100).</summary>
    public static List<(string Name, string Icon, double Tolerance)> TaxTolerance(Nation n)
    {
        n.EnsureTaxationInitialized();
        // Food does not move tolerance until the item-based redesign: neutral ratio.
        double toleranceMod = TaxationService.ToleranceModifier(TaxationService.NormalSupplyRatio);

        var groups = new[]
        {
            ("Peasants", "🌾", n.TaxRates.Peasants),
            ("Craftsmen", "🔨", n.TaxRates.Craftsmen),
            ("Military", "⚔️", n.TaxRates.MilitaryPersonnel),
            ("Merchants", "💼", n.TaxRates.Merchants),
            ("Spies", "🕵️", n.TaxRates.Spies),
            ("Saboteurs", "💣", n.TaxRates.Saboteurs),
        };
        // Tolerance = 100 - (rate * toleranceMod)
        return groups.Select(g => (g.Item1, g.Item2, Math.Clamp(100 - g.Item3 * toleranceMod, 0, 100))).ToList();
    }
}
