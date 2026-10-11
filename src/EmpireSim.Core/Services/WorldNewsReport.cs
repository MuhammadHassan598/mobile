using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// Read side of the World news section: what other countries do among themselves, kept apart from the player's own
/// Movement Report (<see cref="MovementReport"/>). It reads the same saved movement records, but only those that do not
/// concern the player's country (<see cref="GameState.ConcernsPlayer"/>), so a record is in one section or the other, never both.
/// The news is split into military news and other news.
/// </summary>
public static class WorldNewsReport
{
    /// <summary>Whether a kind of movement is military news (wars, armies, soldiers lent) as opposed to everything else (treaties, gold, goods, colonies, missions, Assembly, diplomacy).</summary>
    public static bool IsMilitary(MovementKind kind) => kind is MovementKind.War or MovementKind.March or MovementKind.Troops;

    /// <summary>A record is military news by its kind, or because it is a result of war (spoils and tribute taken).</summary>
    public static bool IsMilitary(MovementRecord m) => IsMilitary(m.Kind) || m.Military;

    /// <summary>
    /// World news, newest first. <paramref name="military"/> picks the military news (true), the other news (false), or both (null);
    /// with a state id, only news that involves that state.
    /// </summary>
    public static List<MovementRecord> Items(GameState state, bool? military = null, string? stateId = null) =>
        state.Movements
            .Where(m => !state.ConcernsPlayer(m))
            .Where(m => military is null || IsMilitary(m) == military)
            .Where(m => stateId is null || m.Involves(stateId))
            .Reverse()   // records are appended in time order, so reversing keeps same-day events newest-first
            .OrderByDescending(m => m.Date)
            .ToList();

    /// <summary>World news grouped under the state that acted; groups with the latest news first, items newest first.</summary>
    public static List<StateGroup<MovementRecord>> ByState(GameState state, bool? military = null, string? stateId = null) =>
        Items(state, military, stateId)
            .GroupBy(m => (m.FromId, m.FromName))
            .Select(g => new StateGroup<MovementRecord>(g.Key.FromId, g.Key.FromName, g.ToList()))
            .ToList();
}
