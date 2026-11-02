namespace EmpireSim.Core.Models;

/// <summary>Type of covert operation attempted by a spy.</summary>
public enum SpyMissionType
{
    Theft,
    Sabotage,
    InciteRevolt
}

/// <summary>Status of a captured foreign spy.</summary>
public enum CapturedSpyStatus
{
    Imprisoned,
    Executed,
    Ransomed,
    Paraded
}

/// <summary>
/// A foreign intelligence operative captured inside the realm by imperial counter-espionage.
/// Held in the palace dungeons awaiting the ruler's sovereign decree.
/// </summary>
public sealed class CapturedSpy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The foreign country that dispatched this operative.</summary>
    public string FromNationId { get; set; } = "";

    /// <summary>Display name of the foreign country.</summary>
    public string FromNationName { get; set; } = "";

    /// <summary>What the operative was caught attempting to do.</summary>
    public SpyMissionType MissionType { get; set; }

    /// <summary>The calendar date when the operative was captured.</summary>
    public DateOnly CaptureDate { get; set; }

    /// <summary>Current fate: Imprisoned, Executed, Ransomed, or Paraded.</summary>
    public CapturedSpyStatus Status { get; set; } = CapturedSpyStatus.Imprisoned;

    /// <summary>Date when the ruler decreed their fate.</summary>
    public DateOnly? ResolvedDate { get; set; }

    /// <summary>Summary notes of the ruler's resolution.</summary>
    public string? ResolutionNotes { get; set; }
}
