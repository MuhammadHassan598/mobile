using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>How an army gets from one capital to another.</summary>
/// <param name="Distance">Map units between the two capitals, or null if neither coordinates nor an estimate exist.</param>
/// <param name="Days">Travel days: the distance over the one movement speed (<see cref="Balance.MarchSpeedPerDay"/>), at least one day.</param>
/// <param name="UsedFallback">A capital's coordinates were missing, so the distance is an estimate (see <see cref="MapGeography.Capital"/>).</param>
public readonly record struct MarchRoute(double? Distance, int Days, bool UsedFallback);

/// <summary>
/// Where the capitals are and how long it takes to march between them. Every march, the player's or an AI country's, takes
/// its time from here: the straight-line distance between the two capitals' map coordinates divided by one movement speed.
/// Nobody arrives instantly and no distance is capped.
/// </summary>
public static class MapGeography
{
    /// <summary>Whether a country has capital coordinates on the map (0,0 means "never set").</summary>
    public static bool HasCapital(Nation n) =>
        double.IsFinite(n.MapX) && double.IsFinite(n.MapY) && !(n.MapX == 0 && n.MapY == 0);

    /// <summary>
    /// A country's capital coordinates. Fallback rule when they are missing: the centre of its territory polygons, if it has any;
    /// otherwise null. A country that needs the fallback is listed by <see cref="MissingCapitals"/>.
    /// </summary>
    public static (double X, double Y)? Capital(Nation n)
    {
        if (HasCapital(n)) return (n.MapX, n.MapY);
        var points = n.Territory.SelectMany(polygon => polygon).ToList();
        if (points.Count > 0) return (points.Average(p => p.X), points.Average(p => p.Y));
        return null;
    }

    /// <summary>
    /// The route between two capitals. If either has no coordinates and no territory to estimate from, the march takes
    /// <see cref="Balance.MarchFallbackDays"/> days (and <see cref="MarchRoute.Distance"/> is null).
    /// </summary>
    public static MarchRoute Route(Nation from, Nation to)
    {
        var a = Capital(from);
        var b = Capital(to);
        bool estimated = !HasCapital(from) || !HasCapital(to);
        if (a is null || b is null)
            return new MarchRoute(null, Balance.MarchFallbackDays, true);

        double distance = Math.Sqrt(Math.Pow(a.Value.X - b.Value.X, 2) + Math.Pow(a.Value.Y - b.Value.Y, 2));
        return new MarchRoute(distance, Warfare.TravelDays(a.Value.X, a.Value.Y, b.Value.X, b.Value.Y), estimated);
    }

    /// <summary>The countries that have no capital coordinates and so need geographical data (their marches use the fallback).</summary>
    public static List<Nation> MissingCapitals(GameState state) =>
        state.AllNations().Where(n => !HasCapital(n)).ToList();

    /// <summary>A line for the event log when a march had to use the fallback; null if it did not.</summary>
    public static string? FallbackNote(Nation from, Nation to, MarchRoute route)
    {
        if (!route.UsedFallback) return null;
        var lacking = new[] { from, to }.Where(n => !HasCapital(n)).Select(n => n.Name).Distinct();
        return $"Geography: no capital coordinates for {string.Join(" and ", lacking)}; the march uses " +
               (route.Distance is null ? $"the fallback of {Balance.MarchFallbackDays} days" : "an estimate from its territory") + ".";
    }
}
