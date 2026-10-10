using EmpireSim.Core.Services;

namespace EmpireSim.Core.Models;

/// <summary>Which page of the country panel an action lives on.</summary>
public enum DiplomaticTab
{
    Hostile = 0,
    Treaties = 1,
    Relations = 2
}

/// <summary>Static description of one diplomatic action: icon, label and what it needs / does.</summary>
public sealed record DiplomaticActionSpec(
    string Id,
    string Name,
    string Icon,
    DiplomaticTab Tab,
    string Requirements,
    string Effects,
    string? BlockedReason = null);

/// <summary>How a diplomatic action ended.</summary>
public enum DipOutcome
{
    /// <summary>Carried out.</summary>
    Done,
    /// <summary>The other country said no (a decision, not a rule violation).</summary>
    Rejected,
    /// <summary>A rule stopped it before anyone was asked: nothing was spent.</summary>
    Invalid
}

/// <summary>Result of a diplomatic action, with a message ready to show the player.</summary>
public readonly record struct DipResult(DipOutcome Outcome, string Message)
{
    public bool Ok => Outcome == DipOutcome.Done;
    public static DipResult Done(string message) => new(DipOutcome.Done, message);
    public static DipResult Rejected(string message) => new(DipOutcome.Rejected, message);
    public static DipResult Invalid(string message) => new(DipOutcome.Invalid, message);
}

/// <summary>Whether an action can be used on a country right now, and why not.</summary>
public readonly record struct DipStatus(bool Available, string Reason, string CostText);

/// <summary>The 14 diplomatic actions, in the order the panel shows them.</summary>
public static class DiplomaticActionCatalog
{
    public const string Embassy = "embassy";
    public const string NonAggression = "nap";
    public const string Alliance = "alliance";
    public const string Trade = "trade";
    public const string Research = "research";
    public const string SendTroops = "sendtroops";
    public const string CallToArms = "calltoarms";
    public const string GiveArmy = "givearmy";
    public const string Gift = "gift";
    public const string Improve = "improve";
    public const string Aid = "aid";
    public const string Sovereignty = "sovereignty";
    public const string PresentColony = "colony";
    public const string Missionary = "missionary";

    public static readonly IReadOnlyList<DiplomaticActionSpec> All = new List<DiplomaticActionSpec>
    {
        // ---- Treaties tab ----
        new(Embassy, "Embassy", "🏛️", DiplomaticTab.Treaties,
            $"At peace; relations {Balance.EmbassyMinRating}+; {Currency.Cost(Balance.EmbassyCost)}. They must agree.",
            "Tracks your embassy. Required for every pact below, warms relations a little each day, and is lost if war breaks out."),
        new(NonAggression, "Non-Aggression Pact", "🕊️", DiplomaticTab.Treaties,
            $"Embassy there; relations {Balance.NapMinRating}+; {Currency.Cost(Balance.NapCost)}. They must agree.",
            $"For {Balance.NapDays} days neither side may declare war, invade or run hostile spy ops against the other. Breaking it early costs relations."),
        new(Alliance, "Defensive Alliance", "🛡️", DiplomaticTab.Treaties,
            $"Embassy there; relations {Balance.AllianceMinRating}+; {Currency.Cost(Balance.AllianceCost)}. They must agree.",
            $"If someone declares war on you, your allies march {Balance.AllianceAidFraction:P0} of their army against the aggressor. Allies can be called to your wars. You cannot attack an ally."),
        new(Trade, "Trade Agreement", "📜", DiplomaticTab.Treaties,
            $"Embassy there; relations {Balance.TradeAgreementMinRating}+; {Currency.Cost(Balance.TradeAgreementCost)}. They must agree.",
            $"Goods you buy from them cost {1 - Balance.TradeAgreementImportMult:P0} less, goods you sell them pay {Balance.TradeAgreementExportMult - 1:P0} more, plus the daily pact income."),
        new(Research, "Research Contract", "📚", DiplomaticTab.Treaties,
            "Needs a research system.",
            "Shared research. Not available: the game has no research or technology mechanic yet.",
            BlockedReason: "Needs a research system — the game has no research or technology mechanic yet."),
        new(SendTroops, "Send Troops", "🪖", DiplomaticTab.Treaties,
            $"Embassy there; relations {Balance.SendTroopsMinRating}+; keep {Balance.MinHomeGuard:N0} soldiers at home. They must agree.",
            $"Lends soldiers to their army for {Balance.TroopLoanDays} days, then what is left comes home. Raises relations. Recalled at once if war breaks out."),
        new(CallToArms, "Call to Arms", "📯", DiplomaticTab.Treaties,
            "A defensive ally, and you are at war with someone else. They must agree.",
            $"The ally marches {Balance.CallToArmsFraction:P0} of its own army against your enemy. Its spoils are its own."),

        // ---- Relations tab ----
        new(GiveArmy, "Give Army", "🎖️", DiplomaticTab.Relations,
            $"At peace; relations {Balance.GiveArmyMinRating}+; keep {Balance.MinHomeGuard:N0} soldiers at home.",
            "Permanently hands soldiers to them (moved, never copied). Raises relations by the strength given."),
        new(PresentColony, "Present a Colony", "⛵", DiplomaticTab.Relations,
            $"A colony you own; at peace; relations {Balance.GiveArmyMinRating}+.",
            "Transfers the colony's people and buildings to them. Raises relations."),
        new(Gift, "Send a Gift", "🎁", DiplomaticTab.Relations,
            $"At peace; at least {Currency.Cost(Balance.GiftMinAmount)}.",
            $"Gold moves to their treasury. +1 relation per {Currency.Cost(DiplomacyService.AidRelationshipFactor)} (up to +{DiplomacyService.MaxAidGainPerTransaction})."),
        new(Improve, "Improve Relations", "💌", DiplomaticTab.Relations,
            $"At peace; relations under {Balance.ImproveRelationsMaxRating}; {Currency.Cost(Balance.ImproveRelationsCost)}.",
            $"An envoy mission: +{Balance.ImproveRelationsGain} relations (+{Balance.ImproveRelationsGainWithEmbassy} with an embassy). Cooldown {Balance.ImproveRelationsCooldownDays} days."),
        new(Aid, "Ask for Aid", "🙏", DiplomaticTab.Relations,
            $"At peace; they must rate you at least {Balance.AidAcceptScore} (embassy and shared faith help) and hold some of the resource.",
            $"They give {Balance.AidRequestFraction:P0} of the resource you ask for. Costs {Balance.AidRequestRatingCost} relations; cooldown {Balance.AidCooldownDays} days."),
        new(Sovereignty, "Support Sovereignty", "⚖️", DiplomaticTab.Relations,
            "Needs vassals / independence rules.",
            "Not available: the game has no vassals, independence or wars between AI countries to support.",
            BlockedReason: "Needs a sovereignty system — the game has no vassals, independence or AI-vs-AI wars yet."),
        new(Missionary, "Missionary Work", "📿", DiplomaticTab.Relations,
            $"Your faith is in the religion catalogue; they follow another; at peace; relations {Balance.MissionaryMinRating}+; {Currency.Cost(Balance.MissionaryCost)}.",
            $"Adds religious influence (devout rulers resist, tolerant ones welcome it). At {Balance.MissionaryConversionThreshold:N0} influence they adopt your faith and its bonuses."),
    };

    public static DiplomaticActionSpec? Get(string id) => All.FirstOrDefault(a => a.Id == id);

    public static IEnumerable<DiplomaticActionSpec> ForTab(DiplomaticTab tab) => All.Where(a => a.Tab == tab);
}
