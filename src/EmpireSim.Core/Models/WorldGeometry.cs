using System.Globalization;

namespace EmpireSim.Core.Models;

/// <summary>
/// Code half of <see cref="WorldGeometry"/> (the polygons themselves are generated into WorldGeometry.Data.cs by tools/mapgen).
/// Hands out fresh copies of a realm's polygons, and brings the map of an older save up to date.
/// </summary>
public static partial class WorldGeometry
{
    /// <summary>Fresh copy of a country's own lands (islands and exclaves are separate polygons of the same list).</summary>
    public static List<List<MapPoint>> Nation(string id) => Parse(Nations[id]);

    public static List<List<MapPoint>> Neutral(string name) => Parse(Neutrals[name]);

    public static List<List<MapPoint>> Frontier(string name) => Parse(Frontiers[name]);

    public static bool HasNation(string id) => Nations.ContainsKey(id);

    /// <summary>Whether a country's lands are islands painted onto open sea (the terrain image shows no land there). Frontier regions always are.</summary>
    public static bool IsPaintedIsland(string nationId) => PaintedNations.Contains(nationId);

    /// <summary>Where a neutral or frontier region's name is written (map units).</summary>
    public static (double X, double Y) Label(string name) => Labels[name];

    public static NeutralTerritory NewNeutralRegion(string name)
    {
        var (x, y) = Label(name);
        return new NeutralTerritory { Name = name, LabelX = x, LabelY = y, Polygons = Neutral(name) };
    }

    /// <summary>A neutral territory per name of <see cref="NeutralNames"/>: the land nobody may settle.</summary>
    public static List<NeutralTerritory> NewNeutralRegions() => NeutralNames.Select(NewNeutralRegion).ToList();

    /// <summary>An unclaimed colony region (see <see cref="ColonyCatalog"/> for its character), with its stable id.</summary>
    public static FrontierRegion NewFrontierRegion(string name)
    {
        var (x, y) = Label(name);
        var site = ColonyCatalog.Get(name);
        return new FrontierRegion
        {
            Id = ColonyCatalog.IdOf(name), Name = name, LabelX = x, LabelY = y, Polygons = Frontier(name),
            Landmass = site?.Landmass ?? "", Terrain = site?.Terrain ?? ColonyTerrain.Archipelago,
        };
    }

    /// <summary>Every colony region, unclaimed, in the catalogue's order.</summary>
    public static List<FrontierRegion> NewFrontierRegions() => FrontierNames.Select(NewFrontierRegion).ToList();

    /// <summary>
    /// Brings the map of a save made under an older <see cref="GameState.MapVersion"/> up to date: the redrawn coastlines and borders replace the
    /// old polygons, while who owns what stays exactly as it was. A realm is its own lands plus the lands of every country it has annexed
    /// (those polygons live in the master's list, and the annexed country's own list is empty); neutral and frontier regions are matched by
    /// name, so a frontier region that was already colonised stays gone. A capital that the redraw moved is moved in the save too, unless
    /// the save has already moved it itself.
    /// </summary>
    public static void Upgrade(GameState state)
    {
        if (state.MapVersion >= Version) return;

        foreach (var n in state.AllNations())
        {
            if (!HasNation(n.Id)) continue;
            if (n.IsEliminated)
            {
                n.Territory = new List<List<MapPoint>>();
                n.AnnexedPolygonCount = 0;
                continue;
            }

            var realm = Nation(n.Id);
            int annexed = 0;
            foreach (var land in state.AnnexedBy(n.Id).Where(a => HasNation(a.Id)))
            {
                var lands = Nation(land.Id);
                realm.AddRange(lands);
                annexed += lands.Count;
            }
            n.Territory = realm;
            n.AnnexedPolygonCount = annexed;
        }

        foreach (var (id, capital) in Capitals)
        {
            var n = state.AllNations().FirstOrDefault(x => x.Id == id);
            if (n is null || Math.Abs(n.MapX - capital.WasX) > 0.5 || Math.Abs(n.MapY - capital.WasY) > 0.5) continue;
            n.MapX = capital.X;
            n.MapY = capital.Y;
            n.CapitalName = capital.Name;
        }

        // Neutral land: redrawn by name; a territory that was carved entirely into colony regions (Ireland, Iceland, Greenland) is gone.
        state.NeutralRegions.RemoveAll(r => !NeutralNames.Contains(r.Name));
        foreach (var name in NeutralNames)
        {
            var (x, y) = Label(name);
            var region = state.NeutralRegions.FirstOrDefault(r => r.Name == name);
            if (region is null) state.NeutralRegions.Add(region = new NeutralTerritory { Name = name });
            region.Polygons = Neutral(name);
            region.LabelX = x;
            region.LabelY = y;
        }

        UpgradeColonyRegions(state);
        state.MapVersion = Version;
    }

    /// <summary>
    /// Colony regions of an older save: the expedition that was at sea becomes a mission (what it cost was paid long ago), every region of the
    /// catalogue exists with its stable id (one that was colonised under the old rules is there as a colony owned by whoever founded it), and
    /// the land of every colony is the region's.
    /// </summary>
    private static void UpgradeColonyRegions(GameState state)
    {
        // The expedition first, while its region still has the id it had when it sailed.
        var expedition = state.ActiveExpedition;
        FrontierRegion? sailing = expedition is null ? null
            : state.FrontierRegions.FirstOrDefault(r => r.Id == expedition.RegionId || r.Name == expedition.RegionName);

        foreach (var name in FrontierNames)
        {
            var region = state.FrontierRegions.FirstOrDefault(r => r.Name == name);
            if (region is null)
            {
                state.FrontierRegions.Add(region = NewFrontierRegion(name));
                // A region missing from an old save was colonised under the old rules: it is that colony's land now.
                var legacy = state.Colonies.FirstOrDefault(c => c.Name == name && c.RegionId.Length == 0);
                if (legacy is not null) { region.Status = ColonyStatus.Colonized; region.ColonyId = legacy.Id; }
                continue;
            }
            var (x, y) = Label(name);
            region.Id = ColonyCatalog.IdOf(name);
            region.Polygons = Frontier(name);
            region.LabelX = x;
            region.LabelY = y;
            var site = ColonyCatalog.Get(name);
            if (site is not null) { region.Landmass = site.Landmass; region.Terrain = site.Terrain; }
        }

        if (expedition is not null)
        {
            var region = sailing is null ? null : state.FrontierRegions.FirstOrDefault(r => r.Name == sailing.Name);
            if (region is not null && region.Status == ColonyStatus.Unclaimed)
            {
                var player = state.PlayerNation;
                var mission = new ColonizationMission
                {
                    RegionId = region.Id, RegionName = region.Name, OwnerId = player.Id, OwnerName = player.Name,
                    TotalDays = expedition.TotalDays, StartDate = state.CurrentDate.AddDays(-(expedition.TotalDays - expedition.DaysLeft)),
                    CompletionDate = state.CurrentDate.AddDays(expedition.DaysLeft),
                    Gold = 20, Wheat = 3000, Settlers = 2000, WarshipsRequired = 0,   // (what the old rules took, paid at the start)
                };
                state.ColonizationMissions.Add(mission);
                region.Status = ColonyStatus.MissionInProgress;
                region.MissionId = mission.Id;
            }
            state.ActiveExpedition = null;
        }

        foreach (var colony in state.AllNations().Where(n => n.IsColony))
            if (state.FrontierRegions.FirstOrDefault(r => r.Id == colony.ColonyRegionId) is { } land)
                colony.Territory = Frontier(land.Name);
    }

    private static List<List<MapPoint>> Parse(string rings)
    {
        var result = new List<List<MapPoint>>();
        if (rings.Length == 0) return result;
        foreach (var ring in rings.Split(';'))
        {
            var points = new List<MapPoint>();
            foreach (var pair in ring.Split(' '))
            {
                int comma = pair.IndexOf(',');
                points.Add(new MapPoint(double.Parse(pair.AsSpan(0, comma), CultureInfo.InvariantCulture),
                                        double.Parse(pair.AsSpan(comma + 1), CultureInfo.InvariantCulture)));
            }
            result.Add(points);
        }
        return result;
    }
}
