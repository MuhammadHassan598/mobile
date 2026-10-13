using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>One item a territory's people eat or use: the daily need, what is in store, what its mills make, and how short it ran.</summary>
public sealed record TerritoryItem(string Item, string Icon, double NeedPerDay, double Stock, double OutputPerDay, double UnmetPct);

/// <summary>One kind of mill in a territory.</summary>
public sealed record TerritoryMill(string Id, string Name, string Icon, string Produces, int Count, double OutputPerDay);

/// <summary>Everything the Annexed States screens show about one territory: its people, tax, buildings, stocks and shortages.</summary>
public sealed record TerritoryReport(
    string Id, string Name, string Emblem, string CapitalName, string OwnerId, string OwnerName, DateOnly? AnnexedDate,
    long Population, long BirthsPerDay, double DeathsPerDay, double NetPerDay,
    double TaxRevenuePerDay, double OtherIncomePerDay, double TaxApproval, string ApprovalLabel,
    double SafeTaxThreshold, double TaxDeathIncreasePct,
    int Farms, int Mines, int Sawmills, int Workshops, int Mills, int UnderConstruction,
    IReadOnlyList<TerritoryMill> MillList, IReadOnlyList<TerritoryItem> Items, IReadOnlyList<string> ShortItems,
    string Status, bool Absorbed)
{
    /// <summary>Farms, mines, sawmills, workshops and production buildings: every facility that works in the territory.</summary>
    public int Facilities => Farms + Mines + Sawmills + Workshops + Mills;
    public double TotalIncomePerDay => TaxRevenuePerDay + OtherIncomePerDay;
}

/// <summary>What the master would pay, and how long it would take, for something built in a territory.</summary>
/// <param name="Gold">Paid from the master's treasury.</param>
/// <param name="Wood">Taken from the territory's own stockpile (so are stone and iron).</param>
/// <param name="Block">Why it cannot be done right now (null = it can).</param>
public sealed record BuildQuote(int Count, double Gold, double Wood, double Stone, double Iron, int Days, string? Block);

/// <summary>The effect of a set of tax rates on a territory, worked out by the existing taxation rules before anything is changed.</summary>
public sealed record TaxPreview(double RevenuePerDay, double RevenueChange, double ApprovalTarget, string ApprovalLabel,
    double SafeThreshold, double ExtraDeathPct, double ExtraDeathsPerDay, bool ShortageNow);

/// <summary>
/// Annexed States. A country that has been annexed is not a country any more (it is <see cref="Nation.IsEliminated"/>: no treasury, army,
/// diplomacy, decisions or Assembly vote), but its land and people live on as a TERRITORY of the country that annexed it
/// (<see cref="Nation.AnnexedById"/>). The territory keeps its own local economy in its own record — people, taxed workforce and tax
/// rates, stocks, buildings and building queue — so each territory can be managed separately, nothing is counted twice, and the daily
/// simulation (<c>SimulationService.AdvanceTerritory</c>) runs it by exactly the formulas of every other economy, with its tax and any
/// gold it digs credited to its master. The master (player or AI) is the only authority over it: every operation here is checked
/// against <see cref="Authority"/>.
/// </summary>
public static class AnnexedStateService
{
    /// <summary>The territories a country rules, in the order they were annexed.</summary>
    public static List<Nation> TerritoriesOf(GameState state, Nation owner) => state.AnnexedBy(owner.Id);

    /// <summary>Why <paramref name="owner"/> may not manage this territory (null = it may): it must be an annexed country under that very owner.</summary>
    public static string? Authority(GameState state, Nation owner, Nation? territory)
    {
        if (territory is null) return "No such territory.";
        if (owner.IsEliminated) return $"{owner.Name} no longer rules anything.";
        if (!territory.IsEliminated || territory.AnnexedById is null)
            return $"{territory.Name} is an independent country, not an annexed territory.";
        if (territory.AnnexedById != owner.Id)
        {
            string master = state.AllNations().FirstOrDefault(n => n.Id == territory.AnnexedById)?.Name ?? "another country";
            return $"{territory.Name} belongs to {master}: only its master can manage it.";
        }
        return null;
    }

    /// <summary>The territory a country rules, by id (null if there is none, or it belongs to someone else).</summary>
    public static Nation? Find(GameState state, Nation owner, string territoryId)
    {
        var t = state.AllNations().FirstOrDefault(n => n.Id == territoryId);
        return Authority(state, owner, t) is null ? t : null;
    }

    // ================= Reading =================

    private static double TaxMultiplier(Nation owner) => CommandStaffService.TaxMult(owner) * owner.TaxMult;

    /// <summary>The daily tax the territory pays its master at its current rates (the very figure the daily tick credits).</summary>
    public static double TaxRevenue(Nation owner, Nation territory) =>
        TaxationService.TotalRevenue(territory.Workforce, territory.TaxRates) * TaxMultiplier(owner);

    /// <summary>Gold the territory's own gold mines pay its master each day (by the daily tick's own formula).</summary>
    public static double OtherIncome(GameState state, Nation territory)
    {
        double total = 0;
        foreach (var (id, count) in territory.ProductionBuildings)
        {
            var spec = ProductionCatalog.All.FirstOrDefault(s => s.Id == id);
            if (spec is null || spec.Produces != "Gold") continue;
            total += Banned(state, spec, count * ConsumptionService.OutputPerMill(territory, spec) * AssemblyMult(state, territory));
        }
        return total;
    }

    private static double AssemblyMult(GameState state, Nation territory) =>
        AssemblyService.HasActivePolicy(state.ActiveAssemblyPolicies, AssemblyProposalTypes.ProductionBan, territory.Id, state.CurrentDate) ? 0.5 : 1.0;

    /// <summary>Output of a mill after the Assembly's rules: nothing at all if its product is prohibited.</summary>
    private static double Banned(GameState state, ProductionBuildingSpec spec, double output) =>
        AssemblyService.ProductionBlock(state, spec.Produces) is null ? output : 0;

    /// <summary>The report for one territory.</summary>
    public static TerritoryReport Report(GameState state, Nation owner, Nation territory)
    {
        territory.EnsureTaxationInitialized();
        long births = PopulationService.DailyBirths(territory);
        double deaths = territory.LastShortageDeaths;

        var mills = territory.ProductionBuildings.Where(kv => kv.Value > 0)
            .Select(kv => (spec: ProductionCatalog.All.FirstOrDefault(s => s.Id == kv.Key), count: kv.Value))
            .Where(x => x.spec is not null)
            .Select(x => new TerritoryMill(x.spec!.Id, x.spec.Name, x.spec.Icon, x.spec.Produces, x.count,
                Banned(state, x.spec, x.count * ConsumptionService.OutputPerMill(territory, x.spec) * AssemblyMult(state, territory))))
            .OrderBy(m => m.Name).ToList();

        var items = new List<TerritoryItem>();
        foreach (var spec in ConsumptionCatalog.All)
        {
            double output = mills.Where(m => m.Produces == spec.Item).Sum(m => m.OutputPerDay);
            territory.ShortagePct.TryGetValue(spec.Item, out double unmet);
            items.Add(new TerritoryItem(spec.Item, ConsumptionCatalog.Icon(spec.Item), ConsumptionService.DailyNeed(spec, territory.Population),
                territory.GetProduct(spec.Item), output, unmet));
        }
        var shortItems = items.Where(i => i.UnmetPct > 0).Select(i => i.Item).ToList();

        int millCount = territory.ProductionBuildings.Values.Sum();
        bool absorbed = territory.Population <= 0 && millCount == 0 && territory.Farms + territory.Mines + territory.Sawmills + territory.Workshops == 0;
        string master = owner.Name;
        string status =
            absorbed ? "Absorbed into the national economy (annexed before territories kept their own records)"
            : shortItems.Count > 0 ? $"Shortages: {string.Join(", ", shortItems.Take(4))}{(shortItems.Count > 4 ? "…" : "")}"
            : territory.TaxApproval < 20 ? $"Resentful of {master}'s taxes ({TaxationService.ApprovalLabel(territory.TaxApproval).ToLowerInvariant()})"
            : $"Under {master}'s rule · {TaxationService.ApprovalLabel(territory.TaxApproval).ToLowerInvariant()}";

        return new TerritoryReport(territory.Id, territory.Name, territory.Emblem, territory.CapitalName, owner.Id, owner.Name, territory.AnnexedDate,
            territory.Population, births, deaths, births - deaths,
            TaxRevenue(owner, territory), OtherIncome(state, territory), territory.TaxApproval, TaxationService.ApprovalLabel(territory.TaxApproval),
            territory.LastSafeTaxThreshold, territory.LastTaxDeathIncreasePct,
            territory.Farms, territory.Mines, territory.Sawmills, territory.Workshops, millCount, territory.ConstructionQueue.Count,
            mills, items, shortItems, status, absorbed);
    }

    // ================= Taxes =================

    /// <summary>What a set of rates would do, by the existing rules, without changing anything.</summary>
    public static TaxPreview PreviewTaxes(GameState state, Nation owner, Nation territory, TaxRates rates)
    {
        territory.EnsureTaxationInitialized();
        double revenue = TaxationService.TotalRevenue(territory.Workforce, rates) * TaxMultiplier(owner);
        double current = TaxRevenue(owner, territory);
        double burden = TaxationService.WeightedBurden(territory.Workforce, rates);
        double target = TaxationService.TargetApproval(burden, TaxationService.NormalSupplyRatio);

        var report = new ShortageReport();
        foreach (var kv in territory.ShortagePct) report.UnmetPct[kv.Key] = kv.Value;
        double safe = ConsumptionService.SafeTaxThreshold(territory, report);
        double extraPct = ConsumptionService.TaxDeathIncreasePct(rates, safe);
        double baseDeaths = ConsumptionService.Deaths(report);   // deaths a day from shortage alone
        return new TaxPreview(revenue, revenue - current, target, TaxationService.ApprovalLabel(target), safe, extraPct,
            baseDeaths * extraPct / 100.0, report.ShortItems.Any());
    }

    /// <summary>Sets the territory's tax rates (each 0-100). Returns an error message, or null on success.</summary>
    public static string? SetTaxRates(GameState state, Nation owner, Nation territory, TaxRates rates)
    {
        if (Authority(state, owner, territory) is { } why) return why;
        foreach (var r in new[] { rates.Peasants, rates.Craftsmen, rates.MilitaryPersonnel, rates.Merchants, rates.Spies, rates.Saboteurs })
            if (!double.IsFinite(r) || r < 0 || r > 100) return "Tax rates must be between 0 and 100.";
        territory.TaxRates = new TaxRates
        {
            Peasants = rates.Peasants, Craftsmen = rates.Craftsmen, MilitaryPersonnel = rates.MilitaryPersonnel,
            Merchants = rates.Merchants, Spies = rates.Spies, Saboteurs = rates.Saboteurs,
        };
        if (owner.IsPlayer) state.Log($"Changed the tax rates of {territory.Name}.");
        return null;
    }

    // ================= Building =================

    private static string? StockShort(Nation territory, double wood, double stone, double iron)
    {
        if (territory.Wood + 1e-9 < wood) return $"Needs {wood:N0} wood from {territory.Name}'s stockpile (it holds {territory.Wood:N0}).";
        if (territory.Stone + 1e-9 < stone) return $"Needs {stone:N0} stone from {territory.Name}'s stockpile (it holds {territory.Stone:N0}).";
        if (territory.Iron + 1e-9 < iron) return $"Needs {iron:N0} iron from {territory.Name}'s stockpile (it holds {territory.Iron:N0}).";
        return null;
    }

    /// <summary>What it takes to build <paramref name="count"/> production buildings in a territory, and whether it can be done now.</summary>
    public static BuildQuote QuoteProduction(GameState state, Nation owner, Nation territory, string buildingId, int count)
    {
        var spec = ProductionCatalog.All.FirstOrDefault(s => s.Id == buildingId);
        if (spec is null) return new BuildQuote(count, 0, 0, 0, 0, 0, "Unknown building.");
        int days = CommandStaffService.BuildDays(owner, spec.BuildDays);
        double gold = spec.GoldCost * count, wood = spec.WoodCost * count, stone = spec.StoneCost * count, iron = spec.IronCost * count;
        string? block = Authority(state, owner, territory);
        if (block is null && count <= 0) block = "Choose how many to build.";
        block ??= AssemblyService.ProductionBlock(state, spec.Produces);
        if (block is null && territory.ConstructionQueue.Count + count > Balance.MaxBuildQueue) block = $"{territory.Name}'s build queue is full (max {Balance.MaxBuildQueue}).";
        if (block is null && !owner.CanPay(gold)) block = $"Needs {Currency.Cost(gold)} from the treasury of {owner.Name} (it holds {Currency.Cost(owner.Gold)}).";
        block ??= StockShort(territory, wood, stone, iron);
        return new BuildQuote(count, gold, wood, stone, iron, days, block);
    }

    /// <summary>The most of a production building the master can start in a territory right now, and what limits it.</summary>
    public static (int Max, string LimitedBy) MaxProduction(GameState state, Nation owner, Nation territory, string buildingId)
    {
        var spec = ProductionCatalog.Get(buildingId);
        int max = Math.Max(0, Balance.MaxBuildQueue - territory.ConstructionQueue.Count);
        string by = "the build queue";
        void Limit(double have, double each, string name)
        {
            if (each <= 0) return;
            int fits = (int)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, have) / each));
            if (fits < max) { max = fits; by = name; }
        }
        Limit(owner.Gold, spec.GoldCost, "the treasury's gold");
        Limit(territory.Wood, spec.WoodCost, "the territory's wood");
        Limit(territory.Stone, spec.StoneCost, "the territory's stone");
        Limit(territory.Iron, spec.IronCost, "the territory's iron");
        while (max > 0 && QuoteProduction(state, owner, territory, buildingId, max).Block is not null) max--;
        return (max, by);
    }

    /// <summary>
    /// Starts building production buildings in a territory, by the existing rules: the gold comes from the master's treasury, the wood, stone
    /// and iron from the territory's own stockpile, and the head of construction of the master shortens the time. The finished mills belong
    /// to the territory. Returns an error message, or null on success.
    /// </summary>
    public static string? BuildProduction(GameState state, Nation owner, Nation territory, string buildingId, int count)
    {
        var quote = QuoteProduction(state, owner, territory, buildingId, count);
        if (quote.Block is { } why) return why;
        var spec = ProductionCatalog.Get(buildingId);
        owner.PayGold(quote.Gold);
        territory.Wood -= quote.Wood;
        territory.Stone -= quote.Stone;
        territory.Iron -= quote.Iron;
        for (int i = 0; i < count; i++)
            territory.ConstructionQueue.Add(new ConstructionProject
            {
                Building = BuildingType.Farm,   // (placeholder: ProductionBuildingId is used, as for the master's own builds)
                ProductionBuildingId = buildingId, DaysLeft = quote.Days, TotalDays = quote.Days,
            });
        if (owner.IsPlayer) state.Log($"Started building {count}x {spec.Name} in {territory.Name} ({quote.Days} days each).");
        return null;
    }

    /// <summary>What it takes to build a farm, mine, sawmill or workshop in a territory.</summary>
    public static BuildQuote QuoteBasic(GameState state, Nation owner, Nation territory, BuildingType type)
    {
        var spec = BuildingCatalog.Get(type);
        double costMult = LawService.ConstructionCostMult(owner);
        double gold = spec.GoldCost * costMult, wood = spec.WoodCost * costMult, iron = spec.IronCost * costMult;
        int days = CommandStaffService.BuildDays(owner,
            Math.Max(1, (int)Math.Ceiling(spec.BuildDays * ReligionService.ConstructionTimeMult(owner) * LawService.ConstructionTimeMult(owner))));
        string? block = Authority(state, owner, territory);
        if (block is null && type == BuildingType.Mine) block = AssemblyService.ProductionBlock(state, "iron");
        if (block is null && type == BuildingType.Sawmill) block = AssemblyService.ProductionBlock(state, "wood");
        if (block is null && territory.ConstructionQueue.Count >= Balance.MaxBuildQueue) block = $"{territory.Name}'s build queue is full (max {Balance.MaxBuildQueue}).";
        if (block is null && !owner.CanPay(gold)) block = $"Needs {Currency.Cost(gold)} from the treasury of {owner.Name} (it holds {Currency.Cost(owner.Gold)}).";
        block ??= StockShort(territory, wood, 0, iron);
        return new BuildQuote(1, gold, wood, 0, iron, days, block);
    }

    /// <summary>Starts building a farm, mine, sawmill or workshop in a territory, by the same rules as the master's own. Returns an error, or null.</summary>
    public static string? BuildBasic(GameState state, Nation owner, Nation territory, BuildingType type)
    {
        var quote = QuoteBasic(state, owner, territory, type);
        if (quote.Block is { } why) return why;
        var spec = BuildingCatalog.Get(type);
        owner.PayGold(quote.Gold);
        territory.Wood -= quote.Wood;
        territory.Iron -= quote.Iron;
        territory.ConstructionQueue.Add(new ConstructionProject { Building = type, DaysLeft = quote.Days, TotalDays = quote.Days });
        if (owner.IsPlayer) state.Log($"Started building a {spec.Name} in {territory.Name} ({quote.Days} days).");
        return null;
    }

    // ================= Stock =================

    /// <summary>What a territory holds that could be sent to the national stockpile (id, name, quantity).</summary>
    public static List<(string Id, string Name, string Icon, double Quantity)> Shippable(Nation territory) =>
        TradeCatalog.All.Select(p => (p.Id, p.Name, p.Icon, Quantity: Math.Floor(territory.GetProductStock(p.Id))))
            .Where(x => x.Quantity >= 1).ToList();

    /// <summary>
    /// Sends stock from a territory to its master's own stockpile: it leaves the territory (and its people can no longer eat it) and
    /// arrives at once, exactly once. Returns an error message, or null on success.
    /// </summary>
    public static string? ShipStock(GameState state, Nation owner, Nation territory, string productId, double quantity)
    {
        if (Authority(state, owner, territory) is { } why) return why;
        var product = TradeCatalog.Get(productId);
        if (product is null) return "Unknown goods.";
        if (!(quantity > 0) || !double.IsFinite(quantity)) return "Choose a quantity above zero.";
        double have = territory.GetProductStock(productId);
        if (quantity > have + 1e-9) return $"{territory.Name} holds only {have:N0} {product.Name}.";
        territory.AdjustProductStock(productId, -quantity);
        owner.AdjustProductStock(productId, quantity);
        if (owner.IsPlayer) state.Log($"Shipped {quantity:N0} {product.Name} from {territory.Name} to the national stockpile.");
        return null;
    }
}
