namespace EmpireSim.Core.Models;

/// <summary>The player's spy network inside a foreign nation. Strength 0-100;
/// grows over time and is spent on operations.</summary>
public sealed class SpyNetwork
{
    public string TargetNationId { get; set; } = "";
    public string TargetNationName { get; set; } = "";
    public int Strength { get; set; }
}
