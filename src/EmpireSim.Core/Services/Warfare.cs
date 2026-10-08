using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>Outcome of one invasion battle.</summary>
public sealed record InvasionOutcome(
    bool AttackerWon,
    List<UnitStack> AttackerSurvivors,
    int AttackerCommitted,
    int DefenderCasualties,
    string Summary);

/// <summary>
/// Real warfare: invasions, battles, province capture and elimination.
/// Used for player invasions (engine) and AI invasions (simulation).
/// </summary>
public static class Warfare
{
    /// <summary>
    /// Resolves one invasion. The defender fights with a garrison fraction
    /// of its army at a home-advantage multiplier. Casualties are applied;
    /// the caller merges survivors and transfers the province on victory.
    /// </summary>
    public static InvasionOutcome Invade(
        Nation attacker, Nation defender, Province province,
        int commitCount, double attackerStrengthMult, Random rng)
    {
        var force = ArmyHelper.ExtractSoldiers(attacker, commitCount);
        return ResolveBattle(force, attacker, defender, province, attackerStrengthMult, rng);
    }

    /// <summary>
    /// Resolves a battle with an already-committed force (marching armies).
    /// </summary>
    public static InvasionOutcome ResolveBattle(
        List<UnitStack> force, Nation attacker, Nation defender, Province province,
        double attackerStrengthMult, Random rng)
    {
        int committed = force.Sum(s => s.Count);

        double attackPower = ArmyHelper.ArmyPower(force) * attackerStrengthMult;
        double defensePower = ArmyHelper.ArmyPower(defender.Units)
            * Balance.GarrisonFraction * Balance.HomeAdvantageMult;

        bool won = attackPower > defensePower;
        int defenderCasualties;

        if (won)
        {
            double f = Balance.AttackerWinCasualtyMin + rng.NextDouble()
                * (Balance.AttackerWinCasualtyMax - Balance.AttackerWinCasualtyMin);
            ArmyHelper.ApplyCasualties(force, f, rng);
            int garrisonHeadcount = (int)(defender.Soldiers * Balance.GarrisonFraction);
            double df = Balance.DefenderWinCasualtyMin + rng.NextDouble()
                * (Balance.DefenderWinCasualtyMax - Balance.DefenderWinCasualtyMin);
            defenderCasualties = (int)(garrisonHeadcount * df);
        }
        else
        {
            double f = Balance.AttackerLossCasualtyMin + rng.NextDouble()
                * (Balance.AttackerLossCasualtyMax - Balance.AttackerLossCasualtyMin);
            ArmyHelper.ApplyCasualties(force, f, rng);
            int garrisonHeadcount = (int)(defender.Soldiers * Balance.GarrisonFraction);
            double df = Balance.DefenderLossCasualtyMin + rng.NextDouble()
                * (Balance.DefenderLossCasualtyMax - Balance.DefenderLossCasualtyMin);
            defenderCasualties = (int)(garrisonHeadcount * df);
        }

        ArmyHelper.RemoveSoldiers(defender, defenderCasualties);
        int survivors = force.Sum(s => s.Count);

        string summary = won
            ? $"Victory at {province.Name}! {committed:N0} attacked, {committed - survivors:N0} fallen; " +
              $"the enemy lost {defenderCasualties:N0}. {province.Name} is ours."
            : $"Defeat at {province.Name}. {committed:N0} attacked, {committed - survivors:N0} fallen; " +
              $"the enemy lost {defenderCasualties:N0}. Our survivors retreat.";

        return new InvasionOutcome(won, force, committed, defenderCasualties, summary);
    }

    /// <summary>
    /// Travel days for a march between two provinces, from map distance.
    /// </summary>
    public static int TravelDays(Province from, Province to)
    {
        double dist = Math.Sqrt(
            Math.Pow(from.LabelX - to.LabelX, 2) + Math.Pow(from.LabelY - to.LabelY, 2));
        int days = Balance.MarchBaseDays + (int)(dist / Balance.MarchDaysPerDistance);
        return Math.Clamp(days, Balance.MarchBaseDays, Balance.MarchMaxDays);
    }

    /// <summary>Moves a province from one nation to another after capture.</summary>
    public static void TransferProvince(Province province, Nation from, Nation to)
    {
        long before = province.Population;
        province.Population = (long)(before * (1 - Balance.BattlePopulationLoss));
        from.Provinces.Remove(province);
        to.Provinces.Add(province);
        from.Population = Math.Max(0, from.Population - before);
        to.Population += province.Population;
    }

    /// <summary>
    /// Eliminates a nation with no provinces left: wars end, the army
    /// disbands, diplomacy is closed.
    /// </summary>
    public static void EliminateNation(GameState state, Nation loser)
    {
        loser.IsEliminated = true;
        loser.AtWarWithPlayer = false;
        loser.HasTradePactWithPlayer = false;
        loser.Units.Clear();
        loser.Warships = 0;
        state.SpyNetworks.RemoveAll(s => s.TargetNationId == loser.Id);
        state.Log($"{loser.Name} has been eliminated!");
    }
}
