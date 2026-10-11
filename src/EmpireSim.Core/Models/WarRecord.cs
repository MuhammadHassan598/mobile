namespace EmpireSim.Core.Models;

/// <summary>
/// What a ruler went to war for. It is the motive that weighed most when the war was declared, and it decides what the
/// aggressor does with a victory: <see cref="Conquest"/> takes the country, <see cref="Humiliation"/> takes its wealth,
/// <see cref="Containment"/> only wants the threat beaten back and makes peace.
/// </summary>
public enum WarAim
{
    Conquest,
    Humiliation,
    Containment,
}

/// <summary>
/// A war between two AI countries (wars with the player are tracked by <see cref="Nation.AtWarWithPlayer"/>).
/// An ally that joins a war gets its own record against the original enemy. Saved with the game.
/// </summary>
public sealed class WarRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AggressorId { get; set; } = "";
    public string DefenderId { get; set; } = "";
    public DateOnly StartDate { get; set; }

    /// <summary>What the aggressor is fighting for, fixed when the war began. Null for a record that has none (the outcome is then judged by the winner's motives at the time).</summary>
    public WarAim? Aim { get; set; }

    public bool Involves(string nationId) => AggressorId == nationId || DefenderId == nationId;
    public bool Links(string a, string b) =>
        (AggressorId == a && DefenderId == b) || (AggressorId == b && DefenderId == a);
    public string Other(string nationId) => nationId == AggressorId ? DefenderId : AggressorId;
}
