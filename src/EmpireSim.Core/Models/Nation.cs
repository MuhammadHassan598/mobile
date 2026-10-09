namespace EmpireSim.Core.Models;

/// <summary>
/// A playable (or AI) nation: treasury, food stockpile, population,
/// standing army and the provinces that produce everything.
/// </summary>
public sealed class Nation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Dominant faith, shown on the nation-select screen.</summary>
    public string Religion { get; set; } = "";

    /// <summary>Historical 1600 population estimate, shown on the nation-select screen.</summary>
    public long HistoricalPopulation { get; set; }

    /// <summary>Emblem (emoji) used for the civilization grid and banner.</summary>
    public string Emblem { get; set; } = "";

    /// <summary>Lifetime colonies founded (statistics).</summary>
    public int ColoniesFounded { get; set; }

    /// <summary>Lifetime battles won (statistics).</summary>
    public int BattlesWon { get; set; }
    public string ColorHex { get; set; } = "#8B0000";
    public bool IsPlayer { get; set; }

    /// <summary>Gold treasury. The single currency of the realm.</summary>
    public double Gold { get; set; }

    public bool CanPay(double goldAmount) => Gold >= goldAmount;

    /// <summary>Pays a Gold-denominated amount. Returns false when insufficient.</summary>
    public bool PayGold(double amount)
    {
        if (amount <= 0) return true;
        if (!CanPay(amount)) return false;
        Gold -= amount;
        return true;
    }

    /// <summary>Income arrives as Gold.</summary>
    public void EarnGold(double amount) => Gold += amount;

    /// <summary>Food stockpile in generic food units.</summary>
    public double Food { get; set; }

    /// <summary>Wood stockpile. Produced by sawmills, used by workshops and construction.</summary>
    public double Wood { get; set; }

    /// <summary>Stone stockpile (minerals).</summary>
    public double Stone { get; set; }

    /// <summary>Lead stockpile (minerals).</summary>
    public double Lead { get; set; }

    /// <summary>Copper stockpile (minerals).</summary>
    public double Copper { get; set; }

    /// <summary>Iron stockpile. Produced by mines, used by workshops and construction.</summary>
    public double Iron { get; set; }

    /// <summary>Manufactured goods. Produced by workshops, sold for gold.</summary>
    public double Goods { get; set; }

    /// <summary>Military item stockpiles (crafted, required for recruitment).</summary>
    public double Helmets { get; set; }
    public double Daggers { get; set; }
    public double Pikes { get; set; }
    public double Shotguns { get; set; }
    public double Arquebuses { get; set; }
    public double ShipParts { get; set; }

    /// <summary>Gets a military item count by recipe ID.</summary>
    public double GetMilitaryItem(string recipeId) => recipeId switch
    {
        "helmet" => Helmets,
        "dagger" => Daggers,
        "pike" => Pikes,
        "shotgun" => Shotguns,
        "arquebus" => Arquebuses,
        "shipparts" => ShipParts,
        _ => 0
    };

    /// <summary>Gets tradable stock for a product ID (for trade system).</summary>
    public double GetProductStock(string productId) => productId switch
    {
        "wood" => Wood,
        "stone" => Stone,
        "iron" => Iron,
        "copper" => Copper,
        "lead" => Lead,
        "food" => Food,
        "helmet" => Helmets,
        "dagger" => Daggers,
        "pike" => Pikes,
        "shotgun" => Shotguns,
        "arquebus" => Arquebuses,
        "shipparts" => ShipParts,
        _ => GetGood(productId)
    };

    /// <summary>Adds to product stock (negative to remove). Returns false if insufficient.</summary>
    public bool AdjustProductStock(string productId, double delta)
    {
        double current = GetProductStock(productId);
        if (current + delta < -0.001) return false;
        switch (productId)
        {
            case "wood": Wood += delta; break;
            case "stone": Stone += delta; break;
            case "iron": Iron += delta; break;
            case "copper": Copper += delta; break;
            case "lead": Lead += delta; break;
            case "food": Food += delta; break;
            case "helmet": case "dagger": case "pike":
            case "shotgun": case "arquebus": case "shipparts":
                AddMilitaryItem(productId, delta); break;
            default:
                AddGood(productId, delta); break;
        }
        return true;
    }

    /// <summary>Adds to a military item stockpile by recipe ID.</summary>
    public void AddMilitaryItem(string recipeId, double amount)
    {
        switch (recipeId)
        {
            case "helmet": Helmets += amount; break;
            case "dagger": Daggers += amount; break;
            case "pike": Pikes += amount; break;
            case "shotgun": Shotguns += amount; break;
            case "arquebus": Arquebuses += amount; break;
            case "shipparts": ShipParts += amount; break;
        }
    }

    public long Population { get; set; }

    /// <summary>Land forces by unit type. Total headcount is <see cref="Soldiers"/>.</summary>
    public List<UnitStack> Units { get; set; } = new();

    /// <summary>Total land soldiers across all stacks.</summary>
    public int Soldiers => Units.Sum(u => u.Count);

    public int Warships { get; set; }

    /// <summary>Appointed commanders (one per role at most).</summary>
    public List<Commander> Commanders { get; set; } = new();

    public bool HasCommander(CommanderRole role) => Commanders.Any(c => c.Role == role);

    // ---- Diplomacy (player-centric; only meaningful on AI nations) ----
    /// <summary>Relation toward the player, -100 (hatred) to +100 (allied).</summary>
    public double RelationToPlayer { get; set; }
    public bool AtWarWithPlayer { get; set; }
    public bool HasTradePactWithPlayer { get; set; }
    public bool IsEliminated { get; set; }

    // ---- Laws & religion ----
    public List<EdictType> ActiveEdicts { get; set; } = new();
    public ReligiousStance Stance { get; set; } = ReligiousStance.Pragmatic;

    public bool HasEdict(EdictType edict) => ActiveEdicts.Contains(edict);

    public double TaxMult =>
        (HasEdict(EdictType.WarTaxes) ? 1.25 : 1.0)
        * (HasEdict(EdictType.GrainDole) ? 0.85 : 1.0)
        * (HasEdict(EdictType.MerchantCharters) ? 0.90 : 1.0)
        * (Stance == ReligiousStance.Devout ? 0.90 : 1.0);

    public double GrowthMult =>
        (HasEdict(EdictType.WarTaxes) ? 0.50 : 1.0)
        * (HasEdict(EdictType.GrainDole) ? 1.50 : 1.0)
        * (Stance == ReligiousStance.Devout ? 1.20 : 1.0)
        * (Stance == ReligiousStance.Tolerant ? 0.90 : 1.0);

    public double UpkeepMultExtra => HasEdict(EdictType.MilitaryDrills) ? 1.10 : 1.0;
    public double BattleStrengthMult => HasEdict(EdictType.MilitaryDrills) ? 1.10 : 1.0;

    public double TradeIncomeMult =>
        (HasEdict(EdictType.MerchantCharters) ? 1.50 : 1.0)
        * (Stance == ReligiousStance.Tolerant ? 1.20 : 1.0);

    public double RelationDriftBonus => Stance == ReligiousStance.Tolerant ? 0.30 : 0.0;

    /// <summary>Capital city name, shown on the select screen and map.</summary>
    public string CapitalName { get; set; } = "";

    /// <summary>Map anchor (replaces the old capital-province label).</summary>
    public double MapX { get; set; }

    /// <summary>Map anchor (replaces the old capital-province label).</summary>
    public double MapY { get; set; }

    /// <summary>Capital pin position on the parchment select-screen map,
    /// in image percent (0-100). Set per nation — the painted map is not
    /// the game coordinate space.</summary>
    public double PinX { get; set; }

    /// <summary>Capital pin position on the parchment select-screen map,
    /// in image percent (0-100).</summary>
    public double PinY { get; set; }

    /// <summary>Territory polygons, visual only — the nation is one atomic country.</summary>
    public List<List<MapPoint>> Territory { get; set; } = new();

    /// <summary>Farms across the whole country.</summary>
    public int Farms { get; set; }

    /// <summary>Mines across the whole country.</summary>
    public int Mines { get; set; }

    /// <summary>Sawmills across the whole country.</summary>
    public int Sawmills { get; set; }

    /// <summary>Workshops across the whole country.</summary>
    public int Workshops { get; set; }

    /// <summary>Production building counts by building ID (helmet, bakery, ironmine, ...).</summary>
    public Dictionary<string, int> ProductionBuildings { get; set; } = new();

    /// <summary>Gets the count of a production building, 0 if none built.</summary>
    public int GetProductionBuilding(string id) =>
        ProductionBuildings.TryGetValue(id, out int c) ? c : 0;

    /// <summary>Stockpile of produced goods by good name (Salt, Bread, Iron, ...).</summary>
    public Dictionary<string, double> GoodsInventory { get; set; } = new();

    /// <summary>Gets the amount of a produced good, 0 if none.</summary>
    public double GetGood(string goodName) =>
        GoodsInventory.TryGetValue(goodName, out double v) ? v : 0;

    public void AddGood(string goodName, double amount)
    {
        if (!GoodsInventory.ContainsKey(goodName))
            GoodsInventory[goodName] = 0;
        GoodsInventory[goodName] = Math.Max(0, GoodsInventory[goodName] + amount);
    }

    /// <summary>Military item batches currently in production.</summary>
    public List<MilitaryCraftProject> MilitaryCraftQueue { get; set; } = new();

    /// <summary>Diplomatic action cooldowns: action ID -> game date when available again.</summary>
    public Dictionary<string, DateOnly> DiplomacyCooldowns { get; set; } = new();

    /// <summary>Soldiers being recruited (timed queue).</summary>
    public List<RecruitmentProject> RecruitmentQueue { get; set; } = new();

    /// <summary>Trained reserves awaiting mobilization.</summary>
    public int Reserves { get; set; }

    /// <summary>Active mercenary contracts.</summary>
    public List<MercenaryContract> MercenaryContracts { get; set; } = new();

    /// <summary>National unrest 0-100 (from conscription, war, etc).</summary>
    public double Unrest { get; set; }

    /// <summary>Population demographics (initialized from population).</summary>
    public Demographics Demographics { get; set; } = new();

    /// <summary>Occupational workforce classification.</summary>
    public Workforce Workforce { get; set; } = new();

    /// <summary>Tax rates for the six groups.</summary>
    public TaxRates TaxRates { get; set; } = new();

    /// <summary>Public tax approval 0-100 (default 50).</summary>
    public double TaxApproval { get; set; } = 50;

    /// <summary>Active religion conversion (null if none).</summary>
    public ReligionConversion? ReligionConversion { get; set; }

    /// <summary>Ensure demographics/workforce are initialized (for existing saves).</summary>
    public void EnsureTaxationInitialized()
    {
        if (Demographics.Total != Population)
            Demographics = Demographics.FromPopulation(Population);
        if (Workforce.Peasants == 0 && Population > 0)
            Workforce = Workforce.FromPopulation(Population, Soldiers);
        // Sync military personnel with actual soldiers
        Workforce.MilitaryPersonnel = Soldiers;
    }

    /// <summary>Buildings currently under construction.</summary>
    public List<ConstructionProject> ConstructionQueue { get; set; } = new();

    // ---- Army maintenance (6-month payday cycle, per design doc) ----

    /// <summary>Upkeep accrued since the last payday.</summary>
    public double UpkeepAccrued { get; set; }

    /// <summary>The date the next maintenance payment is due.</summary>
    public DateOnly NextPayday { get; set; }

    /// <summary>Days of grace left once a payday is missed. 0 = not in grace.</summary>
    public int GraceDaysLeft { get; set; }

    public bool IsInGracePeriod => GraceDaysLeft > 0;
}
