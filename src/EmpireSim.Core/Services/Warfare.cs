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
              $"the enemy lost {defenderCasualties:N0}. Its defence is broken."
            : $"Defeat against {defender.Name}. {committed:N0} attacked, {committed - survivors:N0} fallen; " +
              $"the enemy lost {defenderCasualties:N0}. Our survivors retreat.";

        return new InvasionOutcome(won, force, committed, defenderCasualties, summary);
    }

    /// <summary>
    /// Travel days for a march between two map anchors: the straight-line distance over the one movement speed
    /// (<see cref="Balance.MarchSpeedPerDay"/>), rounded up, never less than a day and never capped — a neighbour takes
    /// less time than a far country in proportion to the distance, and nobody arrives instantly.
    /// </summary>
    public static int TravelDays(double fromX, double fromY, double toX, double toY)
    {
        double dist = Math.Sqrt(Math.Pow(fromX - toX, 2) + Math.Pow(fromY - toY, 2));
        return Math.Max(1, (int)Math.Ceiling(dist / Balance.MarchSpeedPerDay));
    }

    /// <summary>
    /// Annexes a whole nation. The loser stops being a country (it is eliminated: no treasury, army, diplomacy, decisions or Assembly
    /// vote) and becomes a TERRITORY of the winner. The winner takes at once its treasury, its army's equipment, its crown domain, its
    /// lands on the map and its colonies. Its people, taxed workforce, stocks and buildings stay where they are, as the territory's own
    /// local economy, which goes on living under the winner's rule (see <see cref="AnnexedStateService"/>): counted once, taxed for the winner,
    /// and managed separately from the winner's own country and from every other territory.
    /// </summary>
    public static void AnnexNation(GameState state, Nation winner, Nation loser, bool byBattle = true)
    {
        if (loser.IsEliminated || winner.IsEliminated || winner == loser) return;   // (a country can only be annexed once)

        // The player cares when a friend or an enemy falls (its treaties and war end with it, so ask first).
        bool concernsPlayer = loser.AtWarWithPlayer || state.IsPlayerAlly(loser.Id);

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

        // What the winner takes at once, exactly once (each figure is added to the winner and zeroed on the loser): the treasury, the army's
        // equipment, the crown domain, the lands on the map, the colonies and the army target.
        winner.Gold += loser.Gold;
        winner.Helmets += loser.Helmets;
        winner.Daggers += loser.Daggers;
        winner.Pikes += loser.Pikes;
        winner.Shotguns += loser.Shotguns;
        winner.Arquebuses += loser.Arquebuses;
        winner.ShipParts += loser.ShipParts;
        loser.Gold = 0;
        loser.Helmets = loser.Daggers = loser.Pikes = loser.Shotguns = loser.Arquebuses = loser.ShipParts = 0;
        winner.CrownDomains += loser.CrownDomains;
        loser.CrownDomains = 0;

        // An AI winner's army target grows with what it absorbed.
        winner.BaseSoldiers += loser.BaseSoldiers;
        winner.BasePopulation = (winner.BasePopulation > 0 ? winner.BasePopulation : winner.HistoricalPopulation)
                              + (loser.BasePopulation > 0 ? loser.BasePopulation : loser.HistoricalPopulation);
        loser.BaseSoldiers = 0;
        loser.BasePopulation = 0;

        // Colonies change hands, with the people and buildings they added to the loser's totals (taken out of the territory's, so
        // nobody is counted in two places).
        foreach (var colony in state.Colonies.Where(c => c.OwnerId == loser.Id))
        {
            if (colony.IsTerritory) { colony.OwnerId = winner.Id; continue; }   // (a colony of its own is re-pointed to the winner below, with every other territory the loser ruled)
            long people = Math.Min(colony.Population, loser.Population);
            int farms = Math.Min(colony.Farms, loser.Farms), mines = Math.Min(colony.Mines, loser.Mines);
            loser.Population -= people; winner.Population += people;
            loser.Farms -= farms; winner.Farms += farms;
            loser.Mines -= mines; winner.Mines += mines;
            colony.OwnerId = winner.Id;
        }

        // What stays where it is is the annexed territory's own local economy, now run by the winner (see AnnexedStateService): its people
        // (less the war losses) and their taxed workforce and tax rates, its stocks of food, goods and minerals, and its farms, mines,
        // mills, workshops and building queue. They are not added to the winner's totals; the territory keeps them under its own name, so
        // they are counted once and can be managed separately. The army is gone.
        loser.EnsureTaxationInitialized();
        double kept = 1 - Balance.BattlePopulationLoss;
        loser.Population -= (long)(loser.Population * Balance.BattlePopulationLoss);
        loser.Workforce.Peasants = (long)(loser.Workforce.Peasants * kept);
        loser.Workforce.Craftsmen = (long)(loser.Workforce.Craftsmen * kept);
        loser.Workforce.Merchants = (long)(loser.Workforce.Merchants * kept);
        loser.Workforce.Spies = (long)(loser.Workforce.Spies * kept);
        loser.Workforce.Saboteurs = (long)(loser.Workforce.Saboteurs * kept);
        loser.Workforce.MilitaryPersonnel = 0;
        loser.Demographics = Demographics.FromPopulation(loser.Population);

        // Its lands become part of the winner's realm on the map (everything it held, including lands it had annexed itself).
        winner.AnnexedPolygonCount += loser.Territory.Count;
        winner.Territory.AddRange(loser.Territory);
        loser.Territory = new List<List<MapPoint>>();
        loser.AnnexedPolygonCount = 0;

        // From now on it is under the winner — and so is every country it had annexed.
        loser.AnnexedById = winner.Id;
        loser.AnnexedDate = state.CurrentDate;
        foreach (var earlier in state.AllNations().Where(n => n.AnnexedById == loser.Id))
            earlier.AnnexedById = winner.Id;

        // Shipments still on the road follow the countries: the dead country's side becomes the winner's. A shipment that now
        // runs from the winner to itself ends on the spot — goods (and, for a purchase, the gold already paid) come home.
        foreach (var tc in state.TradeContracts.Where(c => c.Status == TradeStatus.InTransit
                     && (c.BuyerId == loser.Id || c.SellerId == loser.Id)).ToList())
        {
            if (tc.BuyerId == loser.Id) tc.BuyerId = winner.Id;
            if (tc.SellerId == loser.Id) tc.SellerId = winner.Id;
            if (tc.BuyerId != tc.SellerId) continue;
            winner.AdjustProductStock(tc.ProductId, tc.Quantity);
            if (tc.IsPlayerBuyer) winner.Gold += tc.TotalValue;
            tc.Status = TradeStatus.Cancelled;
        }

        // Its army, officers, recruiting and the Assembly business about it go with it. (Its builders and mills keep working.)
        loser.Units.Clear();
        loser.Warships = 0;
        loser.Reserves = 0;
        loser.Commanders.Clear();
        loser.RecruitmentQueue.Clear();
        loser.MilitaryCraftQueue.Clear();
        loser.MercenaryContracts.Clear();
        state.ActiveAssemblyPolicies.RemoveAll(p => p.TargetId == loser.Id);
        state.AssemblyProposals.RemoveAll(p => p.Status == ProposalStatus.VotingOpen && p.TargetId == loser.Id);

        if (winner.IsPlayer)
        {
            if (byBattle) winner.BattlesWon++;   // a submission is not a battle won
            state.NationsAnnexedByPlayer++;
        }

        loser.IsEliminated = true;
        loser.AtWarWithPlayer = false;
        loser.HasTradePactWithPlayer = false;
        state.SpyNetworks.RemoveAll(s => s.TargetNationId == loser.Id);
        state.MarchingArmies.RemoveAll(m =>
            m.AttackerNationId == loser.Id || m.TargetNationId == loser.Id);
        state.LogMovement(MovementKind.War, MovementStatus.Completed, winner, loser, $"{loser.Name} has been annexed by {winner.Name}!", concernsPlayer,
            inbox: loser.IsPlayer ? null : InboxTopic.Annexation);   // (the fall of the player has its own message)
    }
}
