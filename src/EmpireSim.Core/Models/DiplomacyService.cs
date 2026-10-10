namespace EmpireSim.Core.Models;

/// <summary>
/// Relationship status derived from 0-100 rating.
/// </summary>
public enum RelationStatus
{
    Hostile,    // 0-29
    Tension,    // 30-39
    Neutrality, // 40-59
    Amity,      // 60-79
    Alliance    // 80-100
}

/// <summary>
/// Central diplomacy logic: relationship mapping, status, colors, effects.
/// Maps the existing -100..+100 RelationToPlayer to 0..100 display scale.
/// </summary>
public static class DiplomacyService
{
    // Status thresholds (configurable)
    public const int HostileMax = 29;
    public const int TensionMax = 39;
    public const int NeutralityMax = 59;
    public const int AmityMax = 79;

    // Aid configuration
    public const double AidRelationshipFactor = 100; // gold per +1 relationship
    public const int MaxAidGainPerTransaction = 10;

    /// <summary>Convert -100..+100 to 0..100.</summary>
    public static int ToDisplayRating(double relationToPlayer)
    {
        int r = (int)Math.Round((relationToPlayer + 100) / 2);
        return Math.Clamp(r, 0, 100);
    }

    /// <summary>Raises (or lowers) a nation's regard for the player by display points (1 point = 2 stored points).</summary>
    public static void AddRating(Nation nation, double displayPoints) =>
        nation.RelationToPlayer = Math.Clamp(nation.RelationToPlayer + displayPoints * 2, -100, 100);

    /// <summary>Convert 0..100 back to -100..+100.</summary>
    public static double FromDisplayRating(int display)
    {
        return Math.Clamp(display, 0, 100) * 2 - 100;
    }

    public static RelationStatus GetStatus(int rating) => rating switch
    {
        <= HostileMax => RelationStatus.Hostile,
        <= TensionMax => RelationStatus.Tension,
        <= NeutralityMax => RelationStatus.Neutrality,
        <= AmityMax => RelationStatus.Amity,
        _ => RelationStatus.Alliance
    };

    public static string StatusName(RelationStatus s) => s switch
    {
        RelationStatus.Hostile => "hostile",
        RelationStatus.Tension => "tension",
        RelationStatus.Neutrality => "neutrality",
        RelationStatus.Amity => "amity",
        RelationStatus.Alliance => "alliance",
        _ => "unknown"
    };

    /// <summary>CSS class for relationship text color (parchment-compatible).</summary>
    public static string StatusColorClass(RelationStatus s) => s switch
    {
        RelationStatus.Hostile => "rel-hostile",     // reddish
        RelationStatus.Tension => "rel-tension",     // reddish-orange
        RelationStatus.Neutrality => "rel-neutral",  // gold/orange
        RelationStatus.Amity => "rel-amity",        // green
        RelationStatus.Alliance => "rel-alliance",   // strong green
        _ => ""
    };

    /// <summary>Calculate relationship gain from aid (with diminishing returns cap).</summary>
    public static int AidRelationshipGain(double aidAmount)
    {
        int gain = (int)Math.Floor(aidAmount / AidRelationshipFactor);
        return Math.Min(gain, MaxAidGainPerTransaction);
    }

    /// <summary>Military power for balance-of-forces (soldiers + warships*100).</summary>
    public static double MilitaryPower(Nation n)
    {
        int soldiers = n.Units.Sum(u => u.Count);
        return soldiers + n.Warships * 100;
    }

    /// <summary>Player share 0..1 for balance bar.</summary>
    public static double PlayerShare(double playerPower, double targetPower)
    {
        double total = playerPower + targetPower;
        return total <= 0 ? 0.5 : playerPower / total;
    }
}

/// <summary>Diplomatic event log entry.</summary>
public sealed class DiplomaticEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateOnly GameDate { get; set; }
    public string SourceCountryId { get; set; } = "";
    public string TargetCountryId { get; set; } = "";
    public string ActionId { get; set; } = "";
    public string Result { get; set; } = "";
    public double RelationshipDelta { get; set; }
    public string Description { get; set; } = "";
}
