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
/// Real warfare: invasions, battles and whole-nation annexation.
/// There are no provinces: the country is the atomic unit, and the winner
/// of an invasion takes the whole country.
/// Used for player invasions (engine) and AI invasions (simulation).
/// </summary>
public static class Warfare
{
    /// <summary>
    /// Resolves one invasion instantly (AI use). The defender fights with a
    /// garrison fraction of its army at a home-advantage multiplier.
    /// Casualties are applied; the caller annexes on victory.
    /// </summary>
    public static InvasionOutcome Invade(
        Nation attacker, Nation defender,
        int commitCount, double attackerStrengthMult, Random rng)
    {
        var force = ArmyHelper.ExtractSoldiers(attacker, commitCount);
        return ResolveBattle(force, attacker, defender, attackerStrengthMult, rng);
    }

    /// <summary>
    /// Resolves a battle with an already-committed force (marching armies).
    /// </summary>
    public static InvasionOutcome ResolveBattle(
        List<UnitStack> force, Nation attacker, Nation defender,
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
            ? $"Victory over {defender.Name}! {committed:N0} attacked, {committed - survivors:N0} fallen; " +
              $"the enemy lost {defenderCasualties:N0}. The whole country is ours."
            : $"Defeat against {defender.Name}. {committed:N0} attacked, {committed - survivors:N0} fallen; " +
              $"the enemy lost {defenderCasualties:N0}. Our survivors retreat.";

        return new InvasionOutcome(won, force, committed, defenderCasualties, summary);
    }

    /// <summary>
    /// Travel days for a march between two map anchors, from map distance.
    /// </summary>
    public static int TravelDays(double fromX, double fromY, double toX, double toY)
    {
        double dist = Math.Sqrt(Math.Pow(fromX - toX, 2) + Math.Pow(fromY - toY, 2));
        int days = Balance.MarchBaseDays + (int)(dist / Balance.MarchDaysPerDistance);
        return Math.Clamp(days, Balance.MarchBaseDays, Balance.MarchMaxDays);
    }

    /// <summary>
    /// Annexes a whole nation: the winner absorbs its people, treasury,
    /// resources, buildings and territory; the loser is eliminated.
    /// </summary>
    public static void AnnexNation(GameState state, Nation winner, Nation loser)
    {
        // Treaties end and hosted loan soldiers go home before the loser's army is wiped.
        TreatyService.OnEliminated(state, loser);

        // Armies still marching on the loser turn back: their soldiers rejoin their own nation
        // instead of vanishing with the march.
        foreach (var m in state.MarchingArmies.Where(m => m.TargetNationId == loser.Id).ToList())
        {
            var owner = state.AllNations().FirstOrDefault(n => n.Id == m.AttackerNationId);
            if (owner is not null && !owner.IsEliminated && owner.Id != loser.Id)
                ArmyHelper.MergeStacks(owner, m.Force);
            // Emptied so a march still sitting in today's snapshot can never hand the same soldiers back twice.
            m.Force = new List<UnitStack>();
        }

        long popLoss = (long)(loser.Population * Balance.BattlePopulationLoss);
        winner.Population += Math.Max(0, loser.Population - popLoss);

        winner.Gold += loser.Gold;
        winner.Wood += loser.Wood;
        winner.Stone += loser.Stone;
        winner.Iron += loser.Iron;
        winner.Copper += loser.Copper;
        winner.Lead += loser.Lead;
        winner.Goods += loser.Goods;

        // Food/goods items (one account per item) and the mills that make them.
        foreach (var (item, amount) in loser.GoodsInventory)
            winner.AddProduct(item, amount);
        foreach (var (millId, count) in loser.ProductionBuildings)
            winner.ProductionBuildings[millId] = winner.GetProductionBuilding(millId) + count;

        winner.Farms += loser.Farms;
        winner.Mines += loser.Mines;
        winner.Sawmills += loser.Sawmills;
        winner.Workshops += loser.Workshops;
        winner.Territory.AddRange(loser.Territory);

        if (winner.IsPlayer)
        {
            winner.BattlesWon++;
            state.NationsAnnexedByPlayer++;
        }

        loser.IsEliminated = true;
        loser.AtWarWithPlayer = false;
        loser.HasTradePactWithPlayer = false;
        loser.Units.Clear();
        loser.Warships = 0;
        loser.Territory.Clear();
        state.SpyNetworks.RemoveAll(s => s.TargetNationId == loser.Id);
        state.MarchingArmies.RemoveAll(m =>
            m.AttackerNationId == loser.Id || m.TargetNationId == loser.Id);
        state.Log($"{loser.Name} has been annexed by {winner.Name}!");
    }
}
