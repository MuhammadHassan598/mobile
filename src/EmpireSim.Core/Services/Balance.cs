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
    public const double SafeTaxSurplus = 100;   // every item's mill output > 150% of its need
    public const double SafeTaxShortage = 30;   // any item short, however small or large the shortage
    public const double SafeTaxNormal = 60;     // no shortage, but not in large surplus
    public const double SurplusOutputRatio = 1.5;
    /// <summary>Extra deaths, in % of the shortage deaths, per point of tax above the safe threshold (per tax type).</summary>
    public const double TaxDeathIncreasePerPoint = 0.15;
    /// <summary>Ruler Rating lost per month while any tax is above the safe threshold (flat, whatever the level).</summary>
    public const double TaxDangerRatingDropPerMonth = 0.05;
    public const double DaysPerMonth = 30;

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

    // ---- Command staff (see CommandStaffService). Each is the effect of a CAPABLE holder; distinguished / renowned holders scale it. ----
    public const double LandCommanderUpkeepMult = 0.85;   // Land Commander: -15% land upkeep
    public const double LandCommanderRecruitMult = 0.90;  //   -10% land recruit cost
    public const double LandCommanderBattleMult = 1.05;   //   +5% battle strength of its armies (the existing BattleStrengthMult hook)
    public const double FleetCommanderUpkeepMult = 0.85;  // Fleet Commander: -15% naval upkeep
    public const double FleetCommanderRecruitMult = 0.90; //   -10% warship cost (naval battles are not modelled, so no fighting bonus)
    public const double CinCUpkeepMult = 0.95;            // Commander-in-Chief: -5% upkeep of all forces
    public const double CinCAttritionMult = 0.75;         //   -25% soldiers lost to attrition while at war
    public const double TributeCollectorTaxMult = 1.05;   // Chief Tribute Collector: +5% tax revenue
    public const double TributeCollectorTributeMult = 1.25; //   +25% on tribute demanded from other countries
    public const double TradeHeadImportMult = 0.95;       // Head of Trade: -5% on the price of what you buy
    public const double TradeHeadExportMult = 1.05;       //   +5% on what your sales earn
    public const double TradeHeadPactIncomeMult = 1.10;   //   +10% on trade-pact income
    public const double ConstructionHeadTimeMult = 0.85;  // Head of Construction: -15% construction time
    public const double StaffMinMult = 0.50;              // no combination of staff cuts a cost or time below half

    public const double StaffTier2Strength = 1.35;        // a distinguished holder's effect, relative to a capable one
    public const double StaffTier3Strength = 1.70;        //   and a renowned one's
    public const double StaffTier2HireCost = 2.0;         // hire cost multiplier of the tiers (capable = the post's base cost)
    public const double StaffTier3HireCost = 4.0;
    public const double StaffTier2Wage = 1.6;             // wage multiplier of the tiers
    public const double StaffTier3Wage = 2.5;
    public const double CommanderInChiefHireCost = 20, CommanderInChiefWage = 0.03;
    public const double LandCommanderHireCost = 12, LandCommanderWage = 0.02;
    public const double FleetCommanderHireCost = 12, FleetCommanderWage = 0.02;
    public const double CivilPostHireCost = 15, CivilPostWage = 0.02;   // tribute collector, head of trade, head of construction
    public const double AiStaffReserve = 10;              // an AI country appoints only with this many times the hire cost in its treasury

    // ---- Diplomacy ----
    public const double GiftCost = 5;
    public const double GiftRelationGain = 10;
    public const double TradePactFee = 5;
    public const double TradePactDailyIncome = 0.5;
    public const double TradePactRelationPerDay = 0.5;
    public const double RelationDriftPerDay = 0.2;   // toward 0 when at peace
    public const double TributeArmyRatio = 1.5;      // need 1.5x their army
    public const double TributeFraction = 0.10;      // they pay 10% of treasury
    public const double TributeRefusalWarChance = 0.20;
    public const double AiWarRelationThreshold = -80;
    public const double AiDeclareWarChancePerDay = 0.02;
    public const double WarAttritionPerDay = 0.002;   // 0.2% of soldiers/day each side

    // ---- Peace negotiations (see PeaceService). There is no fee for opening talks: what is offered is the whole price. ----
    public const int PeaceReplyDays = 1;              // an AI country's envoys answer the day after they are given terms
    public const int PeaceOfferDays = 30;             // a proposal left unanswered lapses after this long
    public const int PeaceMaxRounds = 4;              // the opening proposal plus counteroffers; the last round is accept or reject
    public const int PeaceRetryDays = 5;              // after the player's proposal is rejected, the pause before the next opening proposal
    public const int PeaceWearyDays = 360;            // a war this old has worn a country down completely
    public const double PeaceMinScale = 3000;         // the stakes of a war are never worth less than this much gold
    public const double PeaceStakeFraction = 0.5;     // ...otherwise half the two countries' average liquid wealth
    public const double PeaceValuePerPerson = 0.025;  // gold a person is worth when a country or a colony changes hands
    public const double PeaceValuePerBuilding = 500;  // gold a farm or a mine is worth in a colony
    public const double PeaceGiveLandMult = 1.5;      // giving land away hurts more than its price: a country resists the loss
    public const double PeaceCounterBand = 0.6;       // an AI counters when the gap is under this share of the stakes; otherwise it rejects
    public const double PeaceNoise = 0.15;            // share of the stakes by which one ruler's mood moves its price (deterministic per offer)
    public const int AiPeaceMinWarDays = 20;          // an AI country does not put terms to the player before the war has run this long
    public const double AiPeaceProposalChancePerDay = 0.06;   // times how much it wants peace
    public const int AiPeaceProposalGapDays = 45;     // between two proposals of the same AI country
    public const int PeaceAgreementRating = 35;       // how a country regards the player after a fair negotiated peace (0-100 scale)
    public const int PeaceHumbledRating = 22;         // ...after paying the player
    public const int PeaceSatisfiedRating = 42;       // ...after being paid

    // ---- Diplomatic actions ----
    // Ratings and relation gains use the 0-100 display scale (the stored RelationToPlayer
    // is -100..100, so one display point = 2 stored points). "MinRating" is what you need
    // to even ask; "AcceptScore" is what the other country's attitude must reach to say yes
    // (rating + embassy/pact/shared-faith goodwill, see TreatyService.Score).
    public const int ProposalRejectedRatingPenalty = 1;   // a refused proposal stings a little
    public const int ProposalCooldownDays = 7;            // before you may ask the same thing again

    public const double EmbassyCost = 500;
    public const int EmbassyMinRating = 40;
    public const int EmbassyAcceptScore = 45;
    public const double EmbassyRelationPerDay = 0.3;      // stored scale per day; beats the 0.2 daily drift to neutral

    public const double NapCost = 1_500;
    public const int NapDays = 365;
    public const int NapMinRating = 45;
    public const int NapAcceptScore = 55;

    public const double AllianceCost = 3_000;
    public const int AllianceMinRating = 60;
    public const int AllianceAcceptScore = 70;
    public const int AllianceHonorMinRating = 40;         // below this an ally ignores the call
    public const double AllianceAidFraction = 0.25;       // share of the ally's army sent to defend you
    public const double AllianceRelationPerDay = 0.3;     // stored scale per day; beats the 0.2 daily drift to neutral

    public const double TradeAgreementCost = 1_000;
    public const int TradeAgreementMinRating = 50;
    public const int TradeAgreementAcceptScore = 55;
    public const double TradeAgreementImportMult = 0.90;  // price multiplier when you buy from a partner
    public const double TradeAgreementExportMult = 1.10;  // revenue multiplier when you sell to a partner

    public const int BreakPactRatingPenalty = 20;         // cancelling a non-aggression pact or alliance
    public const int BreakMinorTreatyRatingPenalty = 5;   // cancelling an embassy or trade agreement

    public const int MinHomeGuard = 500;                  // soldiers you must keep when sending or giving troops
    public const int TroopLoanDays = 90;
    public const int SendTroopsMinRating = 40;
    public const int SendTroopsAcceptScore = 50;
    public const int SendTroopsMaxGain = 10;              // relation gain cap, 1 per 1,000 soldiers lent
    public const double CallToArmsFraction = 0.30;        // share of the ally's army that marches
    public const int CallToArmsAcceptScore = 60;
    public const double CallToArmsMinStrengthRatio = 0.30; // ally refuses to face an enemy >3x its size
    public const int CallToArmsCooldownDays = 14;

    public const int GiveArmyMinRating = 30;
    public const int GiveArmyMaxGain = 20;                // relation gain cap, 1 per 500 power given

    public const double GiftMinAmount = 100;
    public const int GiftCooldownDays = 3;

    public const double ImproveRelationsCost = 300;
    public const int ImproveRelationsGain = 5;
    public const int ImproveRelationsGainWithEmbassy = 10;
    public const int ImproveRelationsMaxRating = 90;
    public const int ImproveRelationsCooldownDays = 14;

    public const int AidAcceptScore = 65;
    public const double AidRequestFraction = 0.05;        // share of the asked-for resource they hand over
    public const int AidRequestRatingCost = 2;            // asking is a favour
    public const int AidCooldownDays = 30;

    public const int ColonyGiftRatingGain = 15;

    // Allied assistance (Military page): a friend hands over part of its army.
    public const int AlliedHelpMinRating = 70;
    public const double AlliedHelpFraction = 0.10;        // share of the ally's soldiers handed over
    public const int AlliedHelpRatingCost = 2;            // asking is a favour
    public const int AlliedHelpCooldownDays = 30;

    // Ask Attack: a friendly country (an alliance is not needed) joins your war.
    public const int AskAttackMinRating = 70;
    public const int AskAttackAcceptScore = 70;
    public const int AskAttackCooldownDays = 14;

    // ---- The AI world: AI countries farm, recruit, ally, fight each other and pick targets ----
    // Economy: only the country the player picks starts short of food; every other country feeds itself.
    public const double AiSupplyCoverage = 1.15;          // mills cover 115% of need (topped up as the population grows)
    public const double AiRecruitPerThink = 0.03;         // share of its target army an AI recruits per think (while below target)

    // Cadence and reach.
    public const int AiThinkIntervalDays = 10;            // each country weighs its options this often (staggered)
    public const double AiWarRange = 650;                 // map distance between capitals within which countries fight
    public const double AiNeighbourDistance = 300;        // closer than this, countries quarrel a little more
    public const double AiRelationDecayPerDay = 0.05;     // drift of wartime/alliance changes back to the starting regard

    // Alliances: a country may ally with every friend within reach (there is no limit on how many).
    public const double AiAllianceChance = 0.10;          // per think, chance that it looks for a new ally
    public const double AiAllianceMinRelation = 25;       // -100..100: friends only (in practice, countries of one faith)
    public const double AiAllianceRelationGain = 15;
    public const double AiDefenderAllyJoinChance = 0.8;   // an ally of the attacked country joins the war
    public const double AiAttackerAllyJoinChance = 0.3;   // an ally of the attacker joins the war
    public const double AiAllyHonorMinRelation = 20;      // below this an ally ignores the call

    // Wars: any country may go to war with any other within reach. Whether it does is a judgement of motives
    // (land, grudge, fear, duty to an ally) weighed against its chances; nothing is ruled out by relative strength or regard.
    public const double AiGrudgeWeight = 2.0;             // how much a ruler's dislike of a country weighs in wanting war with it
    public const double AiLandWeight = 0.5;               // ... how much coveting its lands weighs
    public const double AiFearWeight = 0.5;               // ... how much fearing its strength weighs
    public const double AiDutyWeight = 1.0;               // ... how much an ally's war weighs
    public const double AiWarPressure = 0.008;            // pace: per think, chance that a ruler with the strongest possible appetite declares war (appetite = motives x chances)
    public const double AiOddsSharpness = 4.0;            // how strongly poor odds hold a ruler back (1 = gently, higher = a gambler's war is rarer)
    public const int AiMaxWars = 1;                       // wars it starts itself (allies may join more)
    public const double AiWarRelationHit = -60;           // regard drop when a war is declared

    // Fighting and peace.
    public const int AiInvasionIntervalDays = 30;         // each side may send an army this often
    public const double AiInvasionChance = 0.7;
    public const double AiInvasionCommitFraction = 0.5;   // share of its soldiers an AI army marches with
    public const int AiPeaceCheckDays = 15;
    public const double AiPeaceChance = 0.08;
    public const int AiMinWarForce = 600;                 // a side with fewer soldiers than this wants peace
    public const int AiMaxWarDays = 540;
    public const double AiWarPeaceRelation = 0;           // regard after a peace (-100..100): neutral, so no grudge is left to start the same war again

    // Victory: after the player beats a country in battle, the player chooses what to do with it
    // (annex it, take its resources, or let it go). Each choice has its own flow.
    public const int VictoryDecisionDays = 30;            // unanswered, the country is let go
    public const double VictorySpoilsFraction = 0.30;     // share of treasury and stocks taken by "resources"
    public const int VictorySpoilsMaxRating = 20;         // a plundered country regards you no better than this (0-100)
    public const int VictoryMercyRating = 50;             // a country you let go starts from neutral

    // Sovereignty guarantee: you vouch for a country's independence.
    public const double SovereigntyCost = 2_500;
    public const int SovereigntyDays = 730;
    public const int SovereigntyMinRating = 55;
    public const int SovereigntyAcceptScore = 60;
    public const double SovereigntyRelationPerDay = 0.3;  // stored scale per day; beats the 0.2 daily drift
    public const int SovereigntyScoreBonus = 5;           // guaranteed countries trust you more

    // Research: points per day come from population; technologies are bought with points.
    public const double ResearchBasePerDay = 1.0;
    public const double ResearchPerMillionPeople = 0.1;   // +1 point a day per 10 million people
    public const double ResearchContractCost = 2_000;
    public const int ResearchContractDays = 365;
    public const int ResearchContractMinRating = 50;
    public const int ResearchContractAcceptScore = 60;
    public const double ResearchContractShare = 0.5;      // share of the partner's daily research handed to you
    public const int ResearchContractMax = 3;

    public const int MissionaryMinRating = 30;
    public const double MissionaryCost = 2_000;
    public const double MissionaryInfluencePerMission = 15;
    public const double MissionaryConversionThreshold = 100;
    public const int MissionaryCooldownDays = 14;
    public const int MissionaryConversionRatingGain = 10;
    public const int MissionaryDevoutRatingPenalty = 2;   // devout rulers resent missionaries

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
    public const double MarchSpeedPerDay = 30;     // the one movement speed: map units an army covers in a day (days = capital-to-capital distance / this, at least 1)
    public const int MarchFallbackDays = 20;       // march time when a capital has no coordinates and no territory to estimate from (see MapGeography)
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
