using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>Shared army helpers used by the simulation and by player/espionage actions.</summary>
public static class ArmyHelper
{
    /// <summary>Removes soldiers across stacks, draining the largest stacks first.</summary>
    public static void RemoveSoldiers(Nation nation, int count)
    {
        int remaining = count;
        foreach (var stack in nation.Units.OrderByDescending(u => u.Count).ToList())
        {
            if (remaining <= 0) break;
            int take = Math.Min(stack.Count, remaining);
            stack.Count -= take;
            remaining -= take;
        }
        nation.Units.RemoveAll(u => u.Count <= 0);
    }

    /// <summary>
    /// Extracts up to <paramref name="count"/> soldiers proportionally across
    /// stacks, removing them from the nation and returning them as a force.
    /// </summary>
    public static List<UnitStack> ExtractSoldiers(Nation nation, int count)
    {
        var force = new List<UnitStack>();
        int total = nation.Soldiers;
        if (total <= 0) return force;
        int remaining = Math.Min(count, total);
        foreach (var stack in nation.Units.OrderByDescending(u => u.Count).ToList())
        {
            if (remaining <= 0) break;
            int take = Math.Min(stack.Count, (int)Math.Ceiling((double)count * stack.Count / total));
            take = Math.Min(take, remaining);
            take = Math.Min(take, stack.Count);
            if (take <= 0) continue;
            stack.Count -= take;
            remaining -= take;
            force.Add(new UnitStack { Type = stack.Type, Count = take });
        }
        nation.Units.RemoveAll(u => u.Count <= 0);
        return force;
    }

    /// <summary>Merges a force back into the nation's stacks (survivors return).</summary>
    public static void MergeStacks(Nation nation, IEnumerable<UnitStack> force)
    {
        foreach (var s in force)
        {
            var existing = nation.Units.FirstOrDefault(u => u.Type == s.Type);
            if (existing is null) nation.Units.Add(new UnitStack { Type = s.Type, Count = s.Count });
            else existing.Count += s.Count;
        }
        nation.Units.RemoveAll(u => u.Count <= 0);
    }

    /// <summary>Battle power of a force: headcount weighted by unit strength.</summary>
    public static double ArmyPower(IEnumerable<UnitStack> force) =>
        force.Sum(s => s.Count * UnitCatalog.Get(s.Type).Strength);

    /// <summary>Removes a fraction of each stack in a force (battle casualties).</summary>
    public static void ApplyCasualties(List<UnitStack> force, double fraction, Random rng)
    {
        double jitter = 0.8 + rng.NextDouble() * 0.4; // ±20%
        foreach (var s in force)
            s.Count -= Math.Min(s.Count, (int)(s.Count * fraction * jitter));
        force.RemoveAll(s => s.Count <= 0);
    }
}
