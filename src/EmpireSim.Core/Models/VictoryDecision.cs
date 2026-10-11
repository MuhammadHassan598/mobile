namespace EmpireSim.Core.Models;

/// <summary>What the winner of a war does with the beaten country. Each choice has its own flow.</summary>
public enum VictoryChoice
{
    /// <summary>The whole country becomes the winner's: people, treasury, stocks, buildings and lands.</summary>
    Annex,
    /// <summary>The winner takes a share of the country's treasury and stocks; the country survives, resentful.</summary>
    Resources,
    /// <summary>The winner takes nothing: a white peace, and the country is grateful.</summary>
    Nothing
}

/// <summary>
/// The player has beaten a country in battle and has not yet decided what to do with it
/// (annex it, take its resources, or let it go). Saved with the game.
/// </summary>
public sealed class VictoryDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LoserId { get; set; } = "";
    public string LoserName { get; set; } = "";
    public DateOnly WonDate { get; set; }

    /// <summary>If the player has not chosen by this date the country is let go (the "nothing" choice).</summary>
    public DateOnly ExpiresDate { get; set; }

    public int DaysLeft(DateOnly today) => Math.Max(0, ExpiresDate.DayNumber - today.DayNumber);
}
