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

    /// <summary>Silver purse. The working currency: taxes, upkeep, building.</summary>
    public double Silver { get; set; }

    /// <summary>Gold purse. 1 Gold = 100 Silver. A store of value.</summary>
    public double Gold { get; set; }

    /// <summary>Total wealth measured in Silver.</summary>
    public double WealthInSilver => Silver + Gold * Currency.SilverPerGold;

    public bool CanPay(double silverAmount) => WealthInSilver >= silverAmount;

    /// <summary>
    /// Pays a Silver-denominated amount: spends Silver first, then
    /// auto-converts Gold if needed. Returns false when wealth is insufficient.
    /// </summary>
    public bool PaySilver(double amount)
    {
        if (amount <= 0) return true;
        if (!CanPay(amount)) return false;
        if (Silver >= amount)
        {
            Silver -= amount;
            return true;
        }
        double need = amount - Silver;
        double goldNeeded = Math.Ceiling(need / Currency.SilverPerGold);
        Gold -= goldNeeded;
        Silver = goldNeeded * Currency.SilverPerGold - need;
        return true;
    }

    /// <summary>Income always arrives as Silver.</summary>
    public void EarnSilver(double amount) => Silver += amount;

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

    public List<Province> Provinces { get; set; } = new();

    /// <summary>Buildings currently under construction.</summary>
    public List<ConstructionProject> ConstructionQueue { get; set; } = new();

    public int TotalFarms => Provinces.Sum(p => p.Farms);
    public int TotalMines => Provinces.Sum(p => p.Mines);
    public int TotalSawmills => Provinces.Sum(p => p.Sawmills);
    public int TotalWorkshops => Provinces.Sum(p => p.Workshops);

    // ---- Army maintenance (6-month payday cycle, per design doc) ----

    /// <summary>Upkeep accrued since the last payday.</summary>
    public double UpkeepAccrued { get; set; }

    /// <summary>The date the next maintenance payment is due.</summary>
    public DateOnly NextPayday { get; set; }

    /// <summary>Days of grace left once a payday is missed. 0 = not in grace.</summary>
    public int GraceDaysLeft { get; set; }

    public bool IsInGracePeriod => GraceDaysLeft > 0;
}
