namespace EmpireSim.Core.Models;

/// <summary>What kind of thing moved between states.</summary>
public enum MovementKind
{
    /// <summary>Soldiers lent, returned, given or sent as help.</summary>
    Troops,
    /// <summary>Armies on the road, battles, recalls.</summary>
    March,
    /// <summary>Gold: gifts, aid, tribute, demands, stolen treasure.</summary>
    Gold,
    /// <summary>Goods and resources: trade shipments, resource aid.</summary>
    Goods,
    /// <summary>Colony expeditions, foundings, colonies handed over.</summary>
    Colony,
    /// <summary>Envoys, embassies, missionaries, spies.</summary>
    Mission,
    /// <summary>Pacts, alliances and trade agreements signed, ended or refused.</summary>
    Treaty,
    /// <summary>War declared, peace made, nations annexed.</summary>
    War,
    // New kinds are appended: kinds are saved as numbers, so existing saves keep their meaning.
    /// <summary>Assembly proposals against a state, their result, and the policy expiring.</summary>
    Assembly,
    /// <summary>Plain diplomatic acts that move nothing, such as an insult.</summary>
    Diplomacy
}

/// <summary>How far a movement has got.</summary>
public enum MovementStatus
{
    /// <summary>Finished: delivered, accepted, returned, resolved.</summary>
    Completed,
    /// <summary>Still on the road: a later record reports how it ended.</summary>
    UnderWay,
    /// <summary>Refused, recalled, caught or otherwise did not happen.</summary>
    Failed
}

/// <summary>
/// One movement between states (or from a state to an unclaimed region), saved with the game
/// and shown in the Movement Report. From/To follow the direction the thing moved.
/// </summary>
public sealed class MovementRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateOnly Date { get; set; }
    public MovementKind Kind { get; set; }
    public MovementStatus Status { get; set; }
    public string FromId { get; set; } = "";
    public string FromName { get; set; } = "";

    /// <summary>A nation id, or a frontier region id for colony expeditions.</summary>
    public string ToId { get; set; } = "";
    public string ToName { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>
    /// The movement concerns the player's country: the player is on one side of it, or it is a war or march that touches one of
    /// the player's allies, or it is the fall of a country the player was allied or at war with. Fixed when it was recorded.
    /// Records between other countries that have none of this are world news and stay out of the player's report.
    /// </summary>
    public bool PlayerLinked { get; set; }

    /// <summary>
    /// A transfer that is a result of war — gold and goods taken as spoils or tribute — and so counts as military news
    /// although its kind is Gold or Goods. (Wars, marches and soldiers lent are military by their kind.)
    /// </summary>
    public bool Military { get; set; }

    public bool Involves(string nationId) => FromId == nationId || ToId == nationId;
}
