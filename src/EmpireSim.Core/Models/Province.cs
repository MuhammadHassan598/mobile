namespace EmpireSim.Core.Models;

/// <summary>
/// A single province inside a nation. Provinces are the atomic unit of
/// the economy: farms and mines here drive the daily production tick.
/// </summary>
public sealed class Province
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public long Population { get; set; }
    public int Farms { get; set; }
    public int Mines { get; set; }
    public int Sawmills { get; set; }
    public int Workshops { get; set; }

    /// <summary>Polygon outline in map space (1000x700 viewBox). Empty = not on the map yet.</summary>
    public List<MapPoint> Polygon { get; set; } = new();

    /// <summary>Label anchor in map space.</summary>
    public double LabelX { get; set; }
    public double LabelY { get; set; }
}
