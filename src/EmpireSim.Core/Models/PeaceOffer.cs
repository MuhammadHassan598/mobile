namespace EmpireSim.Core.Models;

/// <summary>What a single clause of a peace proposal does. Saved as numbers: new kinds are only ever appended.</summary>
public enum PeaceTermKind
{
    /// <summary>The giver pays gold to the receiver.</summary>
    Gold,
    /// <summary>The giver hands over goods or resources (any tradable product: minerals, food and goods, military equipment).</summary>
    Goods,
    /// <summary>The giver hands a colony it owns to the receiver — the only separable territory the game has.</summary>
    Colony,
    /// <summary>The giver country is annexed by the receiver: its people, treasury, stocks, buildings and lands all pass over.</summary>
    Annexation,
    /// <summary>The war between the two ends (has no giver or receiver: it binds both).</summary>
    Peace,
}

/// <summary>
/// One clause of a proposal: who gives, who receives, and what. Every item names its giver and its receiver, so a term is never
/// ambiguous about which country pays.
/// </summary>
public sealed class PeaceTerm
{
    public PeaceTermKind Kind { get; set; }

    /// <summary>The country that gives (empty for <see cref="PeaceTermKind.Peace"/>).</summary>
    public string GiverId { get; set; } = "";

    /// <summary>The country that receives (empty for <see cref="PeaceTermKind.Peace"/>).</summary>
    public string ReceiverId { get; set; } = "";

    /// <summary>Gold, or the quantity of goods.</summary>
    public double Amount { get; set; }

    /// <summary>The trade product handed over (<see cref="PeaceTermKind.Goods"/>).</summary>
    public string ProductId { get; set; } = "";

    /// <summary>The colony handed over (<see cref="PeaceTermKind.Colony"/>).</summary>
    public string ColonyId { get; set; } = "";

    public static PeaceTerm Peace() => new() { Kind = PeaceTermKind.Peace };
    public static PeaceTerm Gold(string giverId, string receiverId, double amount) =>
        new() { Kind = PeaceTermKind.Gold, GiverId = giverId, ReceiverId = receiverId, Amount = amount };
    public static PeaceTerm Goods(string giverId, string receiverId, string productId, double quantity) =>
        new() { Kind = PeaceTermKind.Goods, GiverId = giverId, ReceiverId = receiverId, ProductId = productId, Amount = quantity };
    public static PeaceTerm Colony(string giverId, string receiverId, string colonyId) =>
        new() { Kind = PeaceTermKind.Colony, GiverId = giverId, ReceiverId = receiverId, ColonyId = colonyId };
    public static PeaceTerm Annexation(string giverId, string receiverId) =>
        new() { Kind = PeaceTermKind.Annexation, GiverId = giverId, ReceiverId = receiverId };

    public PeaceTerm Copy() => (PeaceTerm)MemberwiseClone();
}

public enum PeaceOfferStatus
{
    /// <summary>Waiting for the receiver's answer. Nothing has moved and the war goes on.</summary>
    Pending,
    /// <summary>Accepted: the terms were applied (once) and, if they specified peace, the war ended.</summary>
    Accepted,
    /// <summary>Rejected by the receiver: nothing moved, the war goes on.</summary>
    Rejected,
    /// <summary>Answered with different terms (the counteroffer is the next round).</summary>
    Countered,
    /// <summary>Taken back by its proposer before an answer.</summary>
    Withdrawn,
    /// <summary>Not answered in time, or the war it belonged to ended first.</summary>
    Expired,
}

/// <summary>
/// A peace proposal in the war between the player and one other country: the terms, who made them, who must answer, and where the
/// exchange stands. A counteroffer is a new offer one round later (the offer it answers becomes <see cref="PeaceOfferStatus.Countered"/>).
/// Saved with the game, so a negotiation in progress survives save and load.
/// </summary>
public sealed class PeaceOffer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The other country in the war (the war is always between it and the player).</summary>
    public string NationId { get; set; } = "";

    public string ProposerId { get; set; } = "";
    public string ReceiverId { get; set; } = "";
    public List<PeaceTerm> Terms { get; set; } = new();

    public DateOnly CreatedDate { get; set; }

    /// <summary>For an offer an AI country must answer: the day it answers. For an offer to the player: when it lapses.</summary>
    public DateOnly DueDate { get; set; }

    public PeaceOfferStatus Status { get; set; }

    /// <summary>1 for an opening proposal; each counteroffer adds one.</summary>
    public int Round { get; set; } = 1;

    /// <summary>The offer this one answers (null for an opening proposal).</summary>
    public string? ParentId { get; set; }

    /// <summary>Why it ended the way it did (the receiver's reason, or what blocked it).</summary>
    public string? Outcome { get; set; }

    public DateOnly? ResolvedDate { get; set; }

    public bool IsPending => Status == PeaceOfferStatus.Pending;
    public bool SpecifiesPeace => Terms.Any(t => t.Kind == PeaceTermKind.Peace || t.Kind == PeaceTermKind.Annexation);
}

/// <summary>
/// How a war between the player and one country has gone so far — what the AI weighs when it judges peace terms: battles won and
/// lost, soldiers lost on each side, and how long it has lasted. Started with the war, dropped when it ends. Saved with the game.
/// </summary>
public sealed class WarFront
{
    /// <summary>The enemy country.</summary>
    public string NationId { get; set; } = "";
    public DateOnly StartDate { get; set; }

    /// <summary>What the enemy is fighting for (fixed when the war began): a war it started for land is harder to end than one it was dragged into.</summary>
    public WarAim Aim { get; set; } = WarAim.Containment;

    /// <summary>Battles the player's side won (the player's armies that beat the enemy's garrison, and enemy armies the home army repelled).</summary>
    public int PlayerBattlesWon { get; set; }

    /// <summary>Battles the enemy's side won (a player army the garrison beat).</summary>
    public int EnemyBattlesWon { get; set; }

    public long PlayerLosses { get; set; }
    public long EnemyLosses { get; set; }
    public DateOnly? LastBattleDate { get; set; }

    /// <summary>The day the enemy last put terms to the player (it does not pester the player with proposals).</summary>
    public DateOnly? LastAiProposal { get; set; }

    /// <summary>The day the player's last proposal was turned down (a short pause before the next opening offer).</summary>
    public DateOnly? LastPlayerRejected { get; set; }
}
