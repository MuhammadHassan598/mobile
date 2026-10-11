using EmpireSim.Core.Services;

namespace EmpireSim.Core.Models;

/// <summary>Which shortage rules an item falls under.</summary>
public enum ItemGroup
{
    Food,
    Mineral
}

/// <summary>Fixed daily usage of one item per person.</summary>
public sealed record ConsumptionSpec(string Item, double PerPersonPerDay, ItemGroup Group);

/// <summary>
/// Per-person daily usage of every food/goods item and mineral. Usage scales with
/// population: need = rate x population. Item names match ProductionBuildingSpec.Produces.
/// Edit the numbers here to tune.
/// </summary>
public static class ConsumptionCatalog
{
    public static readonly IReadOnlyList<ConsumptionSpec> All = new List<ConsumptionSpec>
    {
        // ---- Food & goods ----
        new("Salt", 0.00001212, ItemGroup.Food),
        new("Clothing", 0.00002010, ItemGroup.Food),
        new("Hats", 0.00002010, ItemGroup.Food),
        new("Fur", 0.00000909, ItemGroup.Food),
        new("Bread", 0.00013909, ItemGroup.Food),
        new("Meat", 0.00001253, ItemGroup.Food),
        new("Wheat", 0.00004646, ItemGroup.Food),
        new("Horses", 0.00000808, ItemGroup.Food),
        new("Milk", 0.00003030, ItemGroup.Food),
        new("Perfume", 0.00000303, ItemGroup.Food),
        new("Wool", 0.00001717, ItemGroup.Food),
        new("Flour", 0.00008838, ItemGroup.Food),

        // ---- Minerals (approved values) ----
        new("Wood", 0.00004000, ItemGroup.Mineral),
        new("Stone", 0.00001000, ItemGroup.Mineral),
        new("Iron", 0.00001500, ItemGroup.Mineral),
        new("Copper", 0.00000600, ItemGroup.Mineral),
        new("Lead", 0.00000300, ItemGroup.Mineral),
    };

    public static string Icon(string item) =>
        ProductionCatalog.All.FirstOrDefault(s => s.Produces == item)?.Icon ?? "📦";
}

/// <summary>
/// One day's shortage result: per item, the share of the daily need that went unmet,
/// in percentage points (2 = 2% short).
/// </summary>
public sealed class ShortageReport
{
    public Dictionary<string, double> UnmetPct { get; } = new();

    public IEnumerable<string> ShortItems => UnmetPct.Where(kv => kv.Value > 0).Select(kv => kv.Key);
}

/// <summary>Population-driven daily consumption and its shortage effects.</summary>
public static class ConsumptionService
{
    /// <summary>Daily need of one item for a population.</summary>
    public static double DailyNeed(ConsumptionSpec spec, long population) =>
        spec.PerPersonPerDay * population;

    /// <summary>Sets every item's stock to the given number of days of the nation's need.</summary>
    public static void SeedStock(Nation nation, int days)
    {
        foreach (var spec in ConsumptionCatalog.All)
        {
            double target = DailyNeed(spec, nation.Population) * days;
            nation.AddProduct(spec.Item, target - nation.GetProduct(spec.Item));
        }
    }

    /// <summary>Starting unmet share of every item's need for a nation of this size (big 30%, mid 15%, small 10%).</summary>
    public static double StartShortage(long population) =>
        population >= Balance.StartBigPopulation ? Balance.StartShortageBig
        : population >= Balance.StartMidPopulation ? Balance.StartShortageMid
        : Balance.StartShortageSmall;

    /// <summary>Daily output of one mill for this nation, with the same multipliers the daily tick applies.</summary>
    public static double OutputPerMill(Nation nation, ProductionBuildingSpec spec)
    {
        double catMult = spec.Category == ProductionCategory.Food ? LawService.FoodOutputMult(nation)
                       : spec.Category == ProductionCategory.Minerals ? LawService.ResourceOutputMult(nation) : 1.0;
        return spec.OutputPerDay * ReligionService.ProductionSpeedMult(nation)
             * catMult * LawService.GeneralProdOutputMult(nation);
    }

    /// <summary>
    /// Gives the nation the mills that cover (1 - starting shortage) of each item's daily need:
    /// mills = closest whole number to need x (1 - shortage) / output per mill. May be 0.
    /// Replaces any existing count for those mills; other buildings (gold mine) are left alone.
    /// </summary>
    public static void SeedMills(Nation nation)
    {
        double covered = 1.0 - StartShortage(nation.Population);
        foreach (var spec in ConsumptionCatalog.All)
        {
            var mill = ProductionCatalog.All.FirstOrDefault(b => b.Produces == spec.Item);
            if (mill is null) continue;
            double perMill = OutputPerMill(nation, mill);
            if (perMill <= 0) continue;
            int count = (int)Math.Round(DailyNeed(spec, nation.Population) * covered / perMill,
                MidpointRounding.AwayFromZero);
            if (count > 0) nation.ProductionBuildings[mill.Id] = count;
            else nation.ProductionBuildings.Remove(mill.Id);
        }
    }

    /// <summary>
    /// Tops the nation's mills up so every consumed item's daily output covers <paramref name="coverage"/> x its daily need
    /// (rounded up, so every needed item has a mill). This is how AI countries feed their people, and keep feeding them as the
    /// population grows. Never removes mills. Returns how many mills were added.
    /// </summary>
    public static int EnsureSupply(Nation nation, double coverage)
    {
        int added = 0;
        foreach (var spec in ConsumptionCatalog.All)
        {
            var mill = ProductionCatalog.All.FirstOrDefault(b => b.Produces == spec.Item);
            if (mill is null) continue;
            double perMill = OutputPerMill(nation, mill);
            double need = DailyNeed(spec, nation.Population);
            if (perMill <= 0 || need <= 0) continue;
            int wanted = (int)Math.Ceiling(need * coverage / perMill);
            int have = nation.GetProductionBuilding(mill.Id);
            if (have >= wanted) continue;
            nation.ProductionBuildings[mill.Id] = wanted;
            added += wanted - have;
        }
        return added;
    }

    /// <summary>
    /// Removes one day of consumption from the nation's stocks (clamped at 0) and
    /// reports how much of each item's need could not be met.
    /// </summary>
    public static ShortageReport Apply(Nation nation)
    {
        var report = new ShortageReport();
        foreach (var spec in ConsumptionCatalog.All)
        {
            double need = DailyNeed(spec, nation.Population);
            double used = Math.Min(nation.GetProduct(spec.Item), need);
            if (used > 0) nation.AddProduct(spec.Item, -used);
            report.UnmetPct[spec.Item] = need > 0 ? (need - used) / need * 100.0 : 0;
        }
        return report;
    }

    /// <summary>Daily Ruler Rating loss: unmet % per item x the group's per-1% rate.</summary>
    public static double RatingDrop(ShortageReport report) =>
        Sum(report, Balance.ShortageRatingDropFoodPerPct, Balance.ShortageRatingDropMineralPerPct);

    /// <summary>Daily deaths (fractional): unmet % per item x the group's per-1% rate.</summary>
    public static double Deaths(ShortageReport report) =>
        Sum(report, Balance.ShortageDeathsFoodPerPct, Balance.ShortageDeathsMineralPerPct);

    private static double Sum(ShortageReport report, double foodPerPct, double mineralPerPct)
    {
        double total = 0;
        foreach (var spec in ConsumptionCatalog.All)
        {
            if (!report.UnmetPct.TryGetValue(spec.Item, out double pct)) continue;
            total += pct * (spec.Group == ItemGroup.Food ? foodPerPct : mineralPerPct);
        }
        return total;
    }

    /// <summary>
    /// True when every consumed item has a daily mill output above 150% of its daily need.
    /// Uses the nation's existing mills (ProductionBuildings) and the existing need calculation.
    /// </summary>
    public static bool HasLargeSurplus(Nation nation)
    {
        foreach (var spec in ConsumptionCatalog.All)
        {
            double need = DailyNeed(spec, nation.Population);
            if (need <= 0) return false;
            var mill = ProductionCatalog.All.FirstOrDefault(b => b.Produces == spec.Item);
            double output = mill is null ? 0 : nation.GetProductionBuilding(mill.Id) * OutputPerMill(nation, mill);
            if (output <= need * Balance.SurplusOutputRatio) return false;
        }
        return true;
    }

    /// <summary>
    /// Safe tax threshold (0-100), in priority order: large surplus 100; any item short 30
    /// (whatever the size of the shortage); otherwise (no shortage, no large surplus) 60.
    /// </summary>
    public static double SafeTaxThreshold(Nation nation, ShortageReport report)
    {
        if (HasLargeSurplus(nation)) return Balance.SafeTaxSurplus;
        if (report.ShortItems.Any()) return Balance.SafeTaxShortage;
        return Balance.SafeTaxNormal;
    }

    /// <summary>
    /// Total extra deaths in % of the shortage deaths: for each of the six tax types,
    /// max(0, rate - safe threshold) x 0.15, summed.
    /// </summary>
    public static double TaxDeathIncreasePct(TaxRates rates, double safeThreshold)
    {
        double total = 0;
        foreach (var rate in new[] { rates.Peasants, rates.Craftsmen, rates.MilitaryPersonnel,
                                     rates.Merchants, rates.Spies, rates.Saboteurs })
            total += Math.Max(0, rate - safeThreshold) * Balance.TaxDeathIncreasePerPoint;
        return total;
    }

    /// <summary>
    /// Applies a day's shortage effects: Ruler Rating drops (clamped 0-100) and people die.
    /// Shortage deaths are raised by the tax death increase, and a tax in the danger zone
    /// also lowers the rating by 0.05 per month. Fractional deaths accumulate
    /// on the nation until they add up to a whole person.
    /// </summary>
    public static void ApplyShortageEffects(Nation nation, ShortageReport report)
    {
        double safeTax = SafeTaxThreshold(nation, report);
        double taxIncreasePct = TaxDeathIncreasePct(nation.TaxRates, safeTax);
        double drop = RatingDrop(report);
        // Any tax in the danger zone (above the safe threshold) costs a flat 0.05 rating per month,
        // whatever the level, spread over the days of the month.
        if (taxIncreasePct > 0)
            drop += Balance.TaxDangerRatingDropPerMonth / Balance.DaysPerMonth;
        double deaths = Deaths(report) * (1 + taxIncreasePct / 100.0);
        nation.LastSafeTaxThreshold = safeTax;
        nation.LastTaxDeathIncreasePct = taxIncreasePct;

        nation.RulerRating = Math.Clamp(nation.RulerRating - drop, 0, 100);

        nation.ShortageDeathCarry += deaths;
        long whole = (long)Math.Floor(nation.ShortageDeathCarry);
        if (whole > 0)
        {
            whole = Math.Min(whole, nation.Population);
            nation.Population -= whole;
            nation.ShortageDeathCarry -= whole;
        }

        nation.ShortagePct = new Dictionary<string, double>(report.UnmetPct);
        nation.LastShortageDeaths = deaths;
        nation.LastRatingDrop = drop;
    }
}
