namespace EmpireSim.Core.Models;

/// <summary>
/// An uncharted frontier region on the map. Visual only — colonising it
/// enriches the homeland directly; there are no provinces anymore.
/// </summary>
public sealed class FrontierRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Polygon outlines in map space (2200x1150).</summary>
    public List<List<MapPoint>> Polygons { get; set; } = new();

    /// <summary>Label anchor in map space.</summary>
    public double LabelX { get; set; }
    public double LabelY { get; set; }
}
