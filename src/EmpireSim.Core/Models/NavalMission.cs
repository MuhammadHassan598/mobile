namespace EmpireSim.Core.Models;

/// <summary>Type of strategic naval deployment.</summary>
public enum NavalMissionType
{
    /// <summary>Enforces an economic and maritime siege on an enemy port/coastline during war.</summary>
    Blockade,

    /// <summary>Patrols imperial shipping lanes to defend merchant convoys against blockades and privateers.</summary>
    Patrol,

    /// <summary>State-sanctioned commerce raiding (letters of marque) against rival merchant shipping.</summary>
    Privateering
}

/// <summary>Lifecycle status of a naval mission.</summary>
public enum NavalMissionStatus
{
    Active,
    Recalled,
    Defeated
}

/// <summary>
/// A deployed naval squadron under imperial orders.
/// </summary>
public sealed class NavalMission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string TargetNationId { get; set; } = "";
    public NavalMissionType Type { get; set; }
    public int Warships { get; set; }
    public DateOnly StartDate { get; set; }
    public int DaysActive { get; set; }
    public double PlunderGold { get; set; }
    public NavalMissionStatus Status { get; set; } = NavalMissionStatus.Active;
}

/// <summary>
/// Result of a high-seas naval engagement between opposing war fleets.
/// </summary>
public sealed class NavalEngagementResult
{
    public string AttackerId { get; set; } = "";
    public string DefenderId { get; set; } = "";
    public int AttackerShips { get; set; }
    public int DefenderShips { get; set; }
    public int AttackerLosses { get; set; }
    public int DefenderLosses { get; set; }
    public int PrizesCaptured { get; set; }
    public bool AttackerWon { get; set; }
    public string Summary { get; set; } = "";
}
