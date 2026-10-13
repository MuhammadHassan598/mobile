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

        foreach (var name in NeutralNames)
        {
            var (x, y) = Label(name);
            var region = state.NeutralRegions.FirstOrDefault(r => r.Name == name);
            if (region is null) state.NeutralRegions.Add(region = new NeutralTerritory { Name = name });
            region.Polygons = Neutral(name);
            region.LabelX = x;
            region.LabelY = y;
        }

        foreach (var region in state.FrontierRegions)
        {
            if (!Frontiers.ContainsKey(region.Name)) continue;
            var (x, y) = Label(region.Name);
            region.Polygons = Frontier(region.Name);
            region.LabelX = x;
            region.LabelY = y;
        }

        state.MapVersion = Version;
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
