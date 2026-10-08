namespace EmpireSim.Core.Services;

/// <summary>
/// All tunable economy numbers in one place. These are STARTER values
/// for prototyping — tune them in a spreadsheet during balancing.
/// </summary>
public static class Balance
{
    // ---- Food (per in-game day) ----
    public const double FoodPerFarmPerDay = 10.0;
    public const double FoodPerPersonPerDay = 0.000008;

    // ---- Raw materials (per in-game day) ----
    public const double IronPerMinePerDay = 2.0;
    public const double WoodPerSawmillPerDay = 3.0;

    // ---- Workshop chain (per workshop per day) ----
    public const double WorkshopWoodConsumedPerDay = 2.0;
    public const double WorkshopIronConsumedPerDay = 1.0;
    public const double WorkshopGoodsProducedPerDay = 1.0;
    public const double GoodsSellPrice = 15.0;   // gold per goods unit

    // ---- Construction ----
    public const int MaxBuildQueue = 3;

    // ---- Commanders ----
    public const double LandCommanderUpkeepMult = 0.85;   // -15% land upkeep
    public const double LandCommanderRecruitMult = 0.90;  // -10% land recruit cost
    public const double FleetCommanderUpkeepMult = 0.85;  // -15% naval upkeep
    public const double FleetCommanderRecruitMult = 0.90; // -10% warship recruit cost
    public const double CinCUpkeepMult = 0.95;            // -5% all upkeep
    public const double CinCTaxMult = 1.05;              // +5% taxes

    // ---- Diplomacy ----
    public const double GiftCost = 500;
    public const double GiftRelationGain = 10;
    public const double TradePactFee = 500;
    public const double TradePactDailyIncome = 50;
    public const double TradePactRelationPerDay = 0.5;
    public const double RelationDriftPerDay = 0.2;   // toward 0 when at peace
    public const double PeaceTributeCost = 1000;
    public const double TributeArmyRatio = 1.5;      // need 1.5x their army
    public const double TributeFraction = 0.10;      // they pay 10% of treasury
    public const double TributeRefusalWarChance = 0.20;
    public const double AiWarRelationThreshold = -80;
    public const double AiDeclareWarChancePerDay = 0.02;
    public const double AiSueForPeaceChancePerDay = 0.05;
    public const double WarAttritionPerDay = 0.002;   // 0.2% of soldiers/day each side

    // ---- Espionage ----
    public const double EstablishNetworkCost = 800;
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
    public const double EdictEnactCost = 200;
    public const double StanceChangeCost = 200;

    // ---- Colonisation ----
    public const double ColonyCostSilver = 2000;
    public const double ColonyCostFood = 3000;
    public const int ColonyColonists = 2000;
    public const int ColonyDays = 30;
    public const int ColonyWarshipsRequired = 5;
    public const long ColonyStartPopulation = 5000;

    // ---- Victory ----
    public const int HegemonyNationCount = 8;

    // ---- Treasury (per in-game day) ----
    // Per-capita rates are rebased for historical 1600 populations: every
    // subject still pays tax and eats food, but the crown domain (royal
    // demesne) guarantees a base income so small nations stay playable.
    public const double TaxPerPersonPerDay = 0.00001;
    public const double CrownDomainIncomePerDay = 20.0;

    // ---- Population ----
    public const double GrowthPerDayWithSurplus = 0.00015;   // ~5.6% per year
    public const double StarvationDeclinePerDay = 0.001;     // ~30% per year while starving

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
