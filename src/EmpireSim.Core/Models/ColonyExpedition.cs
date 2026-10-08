namespace EmpireSim.Core.Models;

/// <summary>A colony expedition to an uncharted region. When the days run
/// out, the region becomes a province of the player's nation.</summary>
public sealed class ColonyExpedition
{
    public string RegionId { get; set; } = "";
    public string RegionName { get; set; } = "";
    public int DaysLeft { get; set; }
    public int TotalDays { get; set; }

    public double Progress => TotalDays <= 0 ? 1 : 1 - (double)DaysLeft / TotalDays;
}
