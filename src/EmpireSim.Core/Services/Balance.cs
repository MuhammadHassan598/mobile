namespace EmpireSim.Core.Services;

/// <summary>
/// All tunable economy numbers in one place. These are STARTER values
/// for prototyping — tune them in a spreadsheet during balancing.
/// </summary>
public static class Balance
{
    /// <summary>Every nation starts with this many days of need in each item (food and minerals).</summary>
    public const int StartingStockDays = 7;

    // ---- Production buildings (food + mineral side) ----
    /// <summary>+100% output: 5/day becomes 10/day.</summary>
    public const double ProductionOutputMult = 2.0;
    /// <summary>+150,000% build cost: 6 becomes 9,006 (x1501). Applies to gold, wood, stone and iron.</summary>
    public const double ProductionCostMult = 1501.0;

    /// <summary>Every nation starts a new game with this much gold.</summary>
    public const double StartingGold = 50_000;

    // ---- Starting mills: share of each item's need left unmet at game start, by nation size ----
    public const double StartShortageBig = 0.30;
    public const double StartShortageMid = 0.15;
    public const double StartShortageSmall = 0.10;
    public const long StartBigPopulation = 10_000_000;
    public const long StartMidPopulation = 2_000_000;

    // ---- Shortage effects: per 1% of an item's daily need left unmet ----
    public const double ShortageRatingDropFoodPerPct = 0.000001;
    public const double ShortageRatingDropMineralPerPct = 0.0000003;
    public const double ShortageDeathsFoodPerPct = 1.3;       // people per day per 1% unmet (was 1.0, +30%)
    public const double ShortageDeathsMineralPerPct = 0.55;   // people per day per 1% unmet (was 0.5, +10%)

    // ---- Tax-driven extra deaths (rates and thresholds on the 0-100 tax scale) ----
    /// <summary>Safe tax threshold by supply conditions, checked in this priority order.</summary>
    public const double SafeTaxSurplus = 100;          // every item's mill output > 150% of its need
    public const double SafeTaxVeryLowShortage = 70;   // average shortage < 10%
    public const double SafeTaxLowShortage = 50;       // average shortage < 20%
    public const double SafeTaxDefault = 30;           // otherwise
    public const double SurplusOutputRatio = 1.5;
    public const double VeryLowAvgShortagePct = 10;
    public const double LowAvgShortagePct = 20;
    /// <summary>Extra deaths, in % of the shortage deaths, per point of tax above the safe threshold (per tax type).</summary>
    public const double TaxDeathIncreasePerPoint = 0.15;

    // ---- Raw materials (per in-game day) ----
    public const double IronPerMinePerDay = 2.0;
    public const double WoodPerSawmillPerDay = 3.0;

    // ---- Workshop chain (per workshop per day) ----
    public const double WorkshopWoodConsumedPerDay = 2.0;
    public const double WorkshopIronConsumedPerDay = 1.0;
    public const double WorkshopGoodsProducedPerDay = 1.0;
    public const double GoodsSellPrice = 15.0;   // gold per goods unit

    // ---- Construction ----
    public const int MaxBuildQueue = 999;
    public const int MaxRecruitmentQueue = 5;
    public const double RecruitDaysPerSoldier = 0.01;
    public const double MaxRecruitDays = 30;
    public const double MobilizeCostPerSoldier = 0.1;

    // ---- Commanders ----
    public const double LandCommanderUpkeepMult = 0.85;   // -15% land upkeep
    public const double LandCommanderRecruitMult = 0.90;  // -10% land recruit cost
    public const double FleetCommanderUpkeepMult = 0.85;  // -15% naval upkeep
    public const double FleetCommanderRecruitMult = 0.90; // -10% warship recruit cost
    public const double CinCUpkeepMult = 0.95;            // -5% all upkeep
    public const double CinCTaxMult = 1.05;              // +5% taxes

    // ---- Diplomacy ----
    public const double GiftCost = 5;
    public const double GiftRelationGain = 10;
    public const double TradePactFee = 5;
    public const double TradePactDailyIncome = 0.5;
    public const double TradePactRelationPerDay = 0.5;
    public const double RelationDriftPerDay = 0.2;   // toward 0 when at peace
    public const double PeaceTributeCost = 10;
    public const double TributeArmyRatio = 1.5;      // need 1.5x their army
    public const double TributeFraction = 0.10;      // they pay 10% of treasury
    public const double TributeRefusalWarChance = 0.20;
    public const double AiWarRelationThreshold = -80;
    public const double AiDeclareWarChancePerDay = 0.02;
    public const double AiSueForPeaceChancePerDay = 0.05;
    public const double WarAttritionPerDay = 0.002;   // 0.2% of soldiers/day each side

    // ---- Espionage ----
    public const double EstablishNetworkCost = 8;
    public const int EstablishNetworkStrength = 20;
    public const int MaxNetworkStrength = 100;
    public const int NetworkGrowthPerDay = 1;
    public const int StealMinStrength = 30;
    public const int StealStrengthCost = 20;
    public const double StealFractionMin = 0.05;
    public const double StealFractionMax = 0.15;
    public const double StealDiscoveryChance = 0.25;
    public const int SabotageMinStrength = 40;
    public const int SabotageStrengthCost = 30;
    public const double SabotageDiscoveryChance = 0.30;
    public const int InciteMinStrength = 50;
    public const int InciteStrengthCost = 40;
    public const double InciteDiscoveryChance = 0.35;
    public const double InciteDesertionFraction = 0.05;
    public const double DiscoveryRelationHit = 30;

    // ---- Warfare ----
    public const double GarrisonFraction = 0.4;    // share of defender's army that fights
    public const double HomeAdvantageMult = 1.25;  // defender battle-power multiplier
    public const int MinInvasionForce = 500;
    public const int MarchBaseDays = 4;            // minimum march time between nations
    public const int MarchDaysPerDistance = 40;    // map px per extra march day
    public const int MarchMaxDays = 30;
    public const double AttackerWinCasualtyMin = 0.10;
    public const double AttackerWinCasualtyMax = 0.20;
    public const double AttackerLossCasualtyMin = 0.50;
    public const double AttackerLossCasualtyMax = 0.70;
    public const double DefenderWinCasualtyMin = 0.40;  // of the garrison
    public const double DefenderWinCasualtyMax = 0.60;
    public const double DefenderLossCasualtyMin = 0.10; // of the garrison
    public const double DefenderLossCasualtyMax = 0.20;
    public const double BattlePopulationLoss = 0.10;    // province pop lost on capture
    public const double AiInvasionArmyRatio = 1.2;      // AI invades when 1.2x stronger
    public const double AiInvasionChancePerDay = 0.01;

    // ---- Laws & religion ----
    public const double EdictEnactCost = 2;
    public const double StanceChangeCost = 2;

    // ---- Colonisation ----
    public const double ColonyCostGold = 20;
    public const double ColonyCostFood = 3000;   // paid in Wheat
    public const int ColonyColonists = 2000;
    public const int ColonyDays = 30;
    public const int ColonyWarshipsRequired = 5;
    public const long ColonyStartPopulation = 5000;

    // ---- Treasury (per in-game day) ----
    // Per-capita rates are rebased for historical 1600 populations: every
    // subject still pays tax and eats food, but the crown domain (royal
    // demesne) guarantees a base income so small nations stay playable.
    public const double TaxPerPersonPerDay = 0.0000001;
    public const double CrownDomainIncomePerDay = 20.0;

    // ---- Population ----
    public const double GrowthPerDayWithSurplus = 0.00015;   // ~5.6% per year

    // ---- Army maintenance payday cycle ----
    public const int PaydayIntervalDays = 180;               // every 6 months
    public const int GracePeriodDays = 14;                   // days to pay after a missed payday
    public const double DesertionFractionOnGraceExpiry = 0.10; // 10% of soldiers desert

    // ---- Clock ----
    public static double DaysPerSecond(Models.GameSpeed speed) => speed switch
    {
        Models.GameSpeed.Normal => 1,
        Models.GameSpeed.Fast => 5,
        Models.GameSpeed.VeryFast => 20,
        _ => 0,
    };
}
