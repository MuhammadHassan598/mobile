namespace EmpireSim.Core.Models;

/// <summary>Kinds of tracked bilateral agreements.</summary>
public enum TreatyType
{
    Embassy,
    NonAggression,
    DefensiveAlliance,
    TradeAgreement,
    // New types are appended: types are saved as numbers, so existing saves keep their meaning.
    /// <summary>Shared research: the partner hands you part of its daily research points.</summary>
    ResearchContract,
    /// <summary>The guarantor vouches for another country's independence.</summary>
    SovereigntyGuarantee
}

/// <summary>
/// A tracked agreement between two nations (saved with the game).
/// Embassy and SovereigntyGuarantee are directional: <see cref="NationAId"/> is the nation that
/// SENT the embassy / GIVES the guarantee, <see cref="NationBId"/> hosts it / is protected.
/// Every other type is mutual.
/// </summary>
public sealed class Treaty
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TreatyType Type { get; set; }
    public string NationAId { get; set; } = "";
    public string NationBId { get; set; } = "";
    public DateOnly SignedDate { get; set; }

    /// <summary>Null = lasts until cancelled.</summary>
    public DateOnly? ExpiresDate { get; set; }

    public bool Involves(string nationId) => NationAId == nationId || NationBId == nationId;

    public bool Links(string a, string b) =>
        (NationAId == a && NationBId == b) || (NationAId == b && NationBId == a);

    public string Other(string nationId) => nationId == NationAId ? NationBId : NationAId;

    public bool IsActiveOn(DateOnly date) => ExpiresDate is null || date < ExpiresDate;
}

/// <summary>
/// Soldiers lent to another nation. The soldiers sit in the host's army (so they
/// fight, cost upkeep there and are not counted twice); on the return date whatever
/// is left of them is taken back from the host and handed to the owner.
/// </summary>
public sealed class TroopLoan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerId { get; set; } = "";
    public string HostId { get; set; } = "";

    /// <summary>What was lent (a private copy — never the same objects as either army).</summary>
    public List<UnitStack> Force { get; set; } = new();

    public DateOnly ReturnDate { get; set; }
    public int Soldiers => Force.Sum(s => s.Count);
}

/// <summary>
/// A founded colony and what it added to its owner, so ownership can be
/// validated and the colony can be handed to another nation.
/// </summary>
public sealed class Colony
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string FoundedById { get; set; } = "";
    public DateOnly FoundedDate { get; set; }

    /// <summary>What the colony contributed to its owner's nation.</summary>
    public long Population { get; set; }
    public int Farms { get; set; }
    public int Mines { get; set; }
}
