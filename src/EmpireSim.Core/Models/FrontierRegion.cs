namespace EmpireSim.Core.Models;

/// <summary>
/// A colonisable region of the map: unclaimed land that a country can settle with a colonisation mission (see
/// <see cref="Services.ColonizationService"/>). Each has its own territory, a stable id (the hyphenated name) and a status; once colonised it
/// stays in the list, owned by whoever founded the colony. The land that nobody may settle (deserts, plateaus, the Arctic) stays neutral.
/// </summary>
public sealed class FrontierRegion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>The neutral land it was carved from ("West Africa"); empty for islands and archipelagos in open sea.</summary>
    public string Landmass { get; set; } = "";

    public ColonyTerrain Terrain { get; set; } = ColonyTerrain.Archipelago;

    public ColonyStatus Status { get; set; } = ColonyStatus.Unclaimed;

    /// <summary>The mission that has claimed the region while <see cref="Status"/> is <see cref="ColonyStatus.MissionInProgress"/>.</summary>
    public string? MissionId { get; set; }

    /// <summary>The colony founded here (its <see cref="Colony.Id"/>) once <see cref="Status"/> is <see cref="ColonyStatus.Colonized"/>.</summary>
    public string? ColonyId { get; set; }

    /// <summary>Polygon outlines in map space (2200x1150).</summary>
    public List<List<MapPoint>> Polygons { get; set; } = new();

    /// <summary>Label anchor in map space.</summary>
    public double LabelX { get; set; }
    public double LabelY { get; set; }
}
