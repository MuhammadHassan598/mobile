using System.Text.Json.Serialization;

namespace EmpireSim.Core.Models;

/// <summary>The three tabs of the inbox.</summary>
public enum InboxCategory
{
    /// <summary>Espionage, enemy army movements, foreign military strength.</summary>
    Intelligence,
    /// <summary>War and peace, battles, calls to arms, army movements and allied assistance.</summary>
    Military,
    /// <summary>Diplomacy, trade, economic warnings, production, ruler rating, religion and general notifications.</summary>
    Other,
}

/// <summary>
/// What happened — the real type of the event that produced a message. Every topic belongs to exactly one
/// <see cref="InboxCategory"/> (see <see cref="InboxCatalog.CategoryOf"/>), so a message is filed by what it is, never by its wording.
/// Saved as numbers: new topics are only ever appended.
/// </summary>
public enum InboxTopic
{
    // ---- Intelligence ----
    /// <summary>A spy network was set up or strengthened in a country.</summary>
    SpyNetwork,
    /// <summary>The result of a spy mission: theft, sabotage, incited revolt.</summary>
    SpyMission,
    /// <summary>A spy of the player was caught.</summary>
    SpyCaught,
    /// <summary>An enemy (or watched) army is on the march.</summary>
    TroopMovement,
    /// <summary>What the spies report about a country's army, fleet and treasury.</summary>
    ForeignStrength,

    // ---- Military ----
    /// <summary>A country declared war on the player.</summary>
    WarDeclared,
    /// <summary>A war that involves one of the player's allies, or a country the player is at war with.</summary>
    AllyAtWar,
    /// <summary>An enemy invaded the player's country.</summary>
    Invasion,
    /// <summary>The result of a battle the player's army fought.</summary>
    Battle,
    /// <summary>An enemy sued for peace.</summary>
    Peace,
    /// <summary>The player won a battle and must decide the fate of the country.</summary>
    Victory,
    /// <summary>A country the player was allied or at war with was annexed.</summary>
    Annexation,
    /// <summary>An army of the player was recalled or moved.</summary>
    ArmyMovement,
    /// <summary>An ally marched or fought for the player.</summary>
    AllyAssistance,
    /// <summary>The answer to a call to arms or a request to attack.</summary>
    CallToArms,
    /// <summary>Soldiers lent, returned or refused.</summary>
    Reinforcements,
    /// <summary>The player's country fell.</summary>
    EmpireFallen,

    // ---- Other ----
    /// <summary>Treaties signed, refused or expired.</summary>
    Diplomacy,
    /// <summary>Aid, tribute and gifts received or refused.</summary>
    Aid,
    /// <summary>Trade deliveries and failed trades.</summary>
    Trade,
    /// <summary>Assembly proposals and policies.</summary>
    Assembly,
    /// <summary>Colony expeditions and foundings.</summary>
    Colony,
    /// <summary>A product shortage in the player's country.</summary>
    Shortage,
    /// <summary>Army maintenance due, or soldiers deserting for want of pay.</summary>
    Upkeep,
    /// <summary>Buildings, units and goods that finished production.</summary>
    Production,
    /// <summary>A change to the ruler's rating (national events).</summary>
    RulerRating,
    /// <summary>Conversions and other religious events.</summary>
    Religion,
    /// <summary>A technology was researched.</summary>
    Research,
}

/// <summary>
/// One message in the player's inbox. The inbox is only a record of what was reported: deleting a message never
/// changes the battle, treaty or economic change behind it. Saved with the game.
/// </summary>
public sealed class InboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateOnly Date { get; set; }
    public InboxTopic Topic { get; set; }
    public string Title { get; set; } = "";

    /// <summary>One line for the list.</summary>
    public string Summary { get; set; } = "";

    /// <summary>The full report shown when the message is opened (lines separated by new lines).</summary>
    public string Details { get; set; } = "";

    /// <summary>The country the message is about (its emblem is shown), if any.</summary>
    public string? CountryId { get; set; }

    public bool Read { get; set; }

    /// <summary>The tab the message is filed under, from its topic.</summary>
    [JsonIgnore]
    public InboxCategory Category => InboxCatalog.CategoryOf(Topic);
}

/// <summary>Which category, icon and default title each topic has.</summary>
public static class InboxCatalog
{
    public static InboxCategory CategoryOf(InboxTopic topic) => topic switch
    {
        InboxTopic.SpyNetwork or InboxTopic.SpyMission or InboxTopic.SpyCaught
            or InboxTopic.TroopMovement or InboxTopic.ForeignStrength => InboxCategory.Intelligence,

        InboxTopic.WarDeclared or InboxTopic.AllyAtWar or InboxTopic.Invasion or InboxTopic.Battle or InboxTopic.Peace
            or InboxTopic.Victory or InboxTopic.Annexation or InboxTopic.ArmyMovement or InboxTopic.AllyAssistance
            or InboxTopic.CallToArms or InboxTopic.Reinforcements or InboxTopic.EmpireFallen => InboxCategory.Military,

        _ => InboxCategory.Other,
    };

    public static string CategoryName(InboxCategory c) => c switch
    {
        InboxCategory.Intelligence => "Intelligence",
        InboxCategory.Military => "Military",
        _ => "Other",
    };

    public static string CategoryIcon(InboxCategory c) => c switch
    {
        InboxCategory.Intelligence => "🕵️",
        InboxCategory.Military => "⚔️",
        _ => "📜",
    };

    public static string TopicTitle(InboxTopic t) => t switch
    {
        InboxTopic.SpyNetwork => "Spy network",
        InboxTopic.SpyMission => "Spy mission",
        InboxTopic.SpyCaught => "Spy caught",
        InboxTopic.TroopMovement => "Troop movement",
        InboxTopic.ForeignStrength => "Foreign strength",
        InboxTopic.WarDeclared => "War declared",
        InboxTopic.AllyAtWar => "War involving an ally",
        InboxTopic.Invasion => "Invasion",
        InboxTopic.Battle => "Battle report",
        InboxTopic.Peace => "Peace",
        InboxTopic.Victory => "Victory",
        InboxTopic.Annexation => "Country annexed",
        InboxTopic.ArmyMovement => "Army movement",
        InboxTopic.AllyAssistance => "Allied assistance",
        InboxTopic.CallToArms => "Call to arms",
        InboxTopic.Reinforcements => "Reinforcements",
        InboxTopic.EmpireFallen => "Your empire has fallen",
        InboxTopic.Diplomacy => "Diplomacy",
        InboxTopic.Aid => "Aid and tribute",
        InboxTopic.Trade => "Trade",
        InboxTopic.Assembly => "Assembly",
        InboxTopic.Colony => "Colony",
        InboxTopic.Shortage => "Shortage",
        InboxTopic.Upkeep => "Army upkeep",
        InboxTopic.Production => "Production",
        InboxTopic.RulerRating => "Ruler rating",
        InboxTopic.Religion => "Religion",
        InboxTopic.Research => "Research",
        _ => t.ToString(),
    };

    public static string TopicIcon(InboxTopic t) => t switch
    {
        InboxTopic.SpyNetwork => "🕵️",
        InboxTopic.SpyMission => "🗝️",
        InboxTopic.SpyCaught => "🚨",
        InboxTopic.TroopMovement => "🪖",
        InboxTopic.ForeignStrength => "📊",
        InboxTopic.WarDeclared => "🚩",
        InboxTopic.AllyAtWar => "🛡️",
        InboxTopic.Invasion => "⚔️",
        InboxTopic.Battle => "⚔️",
        InboxTopic.Peace => "🕊️",
        InboxTopic.Victory => "🏆",
        InboxTopic.Annexation => "🏳️",
        InboxTopic.ArmyMovement => "🧭",
        InboxTopic.AllyAssistance => "🛡️",
        InboxTopic.CallToArms => "📯",
        InboxTopic.Reinforcements => "🪖",
        InboxTopic.EmpireFallen => "💀",
        InboxTopic.Diplomacy => "🤝",
        InboxTopic.Aid => "🪙",
        InboxTopic.Trade => "📦",
        InboxTopic.Assembly => "🏛️",
        InboxTopic.Colony => "⛵",
        InboxTopic.Shortage => "⚠️",
        InboxTopic.Upkeep => "💰",
        InboxTopic.Production => "🏭",
        InboxTopic.RulerRating => "👑",
        InboxTopic.Religion => "⛪",
        InboxTopic.Research => "🔬",
        _ => "✉️",
    };
}
