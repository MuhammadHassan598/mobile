namespace EmpireSim.Core.Models;

/// <summary>
/// A neutral territory drawn on the map, owned by no crown. Visual only.
/// </summary>
public sealed class NeutralTerritory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Polygon outlines in map space (2200x1150).</summary>
    public List<List<MapPoint>> Polygons { get; set; } = new();

    /// <summary>Label anchor in map space.</summary>
    public double LabelX { get; set; }
    public double LabelY { get; set; }
}
