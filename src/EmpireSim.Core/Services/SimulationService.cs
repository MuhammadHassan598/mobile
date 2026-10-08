using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// Applies one in-game day of simulation to every nation:
/// food production/consumption, taxes, army upkeep accrual, the 6-month
/// maintenance payday (with grace period and desertion), and population
/// growth or starvation. Warnings are only raised for the player nation.
/// </summary>
public sealed class SimulationService
{
    private readonly Random _rng;

    /// <param name="seed">Fixed seed for deterministic tests. Null = time-based.</param>
    public SimulationService(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public bool RollChance(double probability) => _rng.NextDouble() < probability;
    public double NextDouble() => _rng.NextDouble();
    public int NextInt(int maxExclusive) => _rng.Next(maxExclusive);

    /// <summary>Shared RNG for battle resolution and other chance events.</summary>
    public Random Rng => _rng;

    public void AdvanceDay(GameState state)
    {
        state.ActiveWarnings.Clear();

        foreach (var nation in state.AllNations())
            AdvanceNation(state, nation);

        AdvanceDiplomacy(state);
        AdvanceColonisation(state);
        AdvanceMarches(state);
        CheckDefeat(state);
    }

    private void AdvanceNation(GameState state, Nation nation)
    {
        // ---- Food ----
        double produced = nation.Farms * Balance.FoodPerFarmPerDay;
        double consumed = nation.Population * Balance.FoodPerPersonPerDay;
        nation.Food += produced - consumed;

        // ---- Raw materials ----
        nation.Iron += nation.Mines * Balance.IronPerMinePerDay;
        nation.Wood += nation.Sawmills * Balance.WoodPerSawmillPerDay;

        // ---- Workshop chain: wood + iron -> goods ----
        if (nation.Workshops > 0)
        {
            double runs = Math.Min(nation.Workshops, Math.Min(
                nation.Wood / Balance.WorkshopWoodConsumedPerDay,
                nation.Iron / Balance.WorkshopIronConsumedPerDay));
            nation.Wood -= runs * Balance.WorkshopWoodConsumedPerDay;
            nation.Iron -= runs * Balance.WorkshopIronConsumedPerDay;
            nation.Goods += runs * Balance.WorkshopGoodsProducedPerDay;
        }

        // ---- Construction ----
        foreach (var project in nation.ConstructionQueue.ToList())
        {
            project.DaysLeft--;
            if (project.DaysLeft > 0) continue;

            switch (project.Building)
            {
                case BuildingType.Farm: nation.Farms++; break;
                case BuildingType.Mine: nation.Mines++; break;
                case BuildingType.Sawmill: nation.Sawmills++; break;
                case BuildingType.Workshop: nation.Workshops++; break;
            }
            state.Log($"{nation.Name}: {BuildingCatalog.Get(project.Building).Name} completed.");
            nation.ConstructionQueue.Remove(project);
        }

        if (nation.Food < 0)
        {
            nation.Food = 0;
            long lost = (long)(nation.Population * Balance.StarvationDeclinePerDay);
            nation.Population = Math.Max(0, nation.Population - lost);
            Warn(state, nation, $"Starvation in {nation.Name}! {lost:N0} souls lost — build farms or buy food.");
        }
        else
        {
            nation.Population += (long)(nation.Population * Balance.GrowthPerDayWithSurplus * nation.GrowthMult);
        }

        // ---- Treasury: taxes in (silver), upkeep accrues towards the next payday ----
        double taxMult = nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCTaxMult : 1.0;
        nation.Silver += nation.Population * Balance.TaxPerPersonPerDay * taxMult * nation.TaxMult
                         + Balance.CrownDomainIncomePerDay;

        double landUpkeepMult = (nation.HasCommander(CommanderRole.LandCommander) ? Balance.LandCommanderUpkeepMult : 1.0)
                              * (nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCUpkeepMult : 1.0);
        double navalUpkeepMult = (nation.HasCommander(CommanderRole.FleetCommander) ? Balance.FleetCommanderUpkeepMult : 1.0)
                               * (nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCUpkeepMult : 1.0);

        double landUpkeep = nation.Units.Sum(u => u.Count * UnitCatalog.Get(u.Type).UpkeepPerDay) * landUpkeepMult * nation.UpkeepMultExtra;
        double navalUpkeep = nation.Warships * WarshipSpec.UpkeepPerDay * navalUpkeepMult;
        double wages = nation.Commanders.Sum(c => c.DailyWage);
        nation.UpkeepAccrued += landUpkeep + navalUpkeep + wages;

        // ---- 6-month payday ----
        if (state.CurrentDate >= nation.NextPayday && !nation.IsInGracePeriod)
        {
            if (nation.CanPay(nation.UpkeepAccrued))
            {
                nation.PaySilver(nation.UpkeepAccrued);
                state.Log($"{nation.Name} paid army maintenance: {Currency.Format(nation.UpkeepAccrued)}.");
                nation.UpkeepAccrued = 0;
                nation.NextPayday = nation.NextPayday.AddDays(Balance.PaydayIntervalDays);
            }
            else
            {
                nation.GraceDaysLeft = Balance.GracePeriodDays;
                Warn(state, nation,
                    $"ARMY MAINTENANCE DUE in {nation.Name}! Need {Currency.Format(nation.UpkeepAccrued)}, treasury holds {Currency.Format(nation.WealthInSilver)}. " +
                    $"Pay within {Balance.GracePeriodDays} days or soldiers will desert.");
            }
        }
        else if (nation.IsInGracePeriod)
        {
            nation.GraceDaysLeft--;
            if (nation.CanPay(nation.UpkeepAccrued))
            {
                // Paid during grace.
                nation.PaySilver(nation.UpkeepAccrued);
                state.Log($"{nation.Name} paid overdue army maintenance: {Currency.Format(nation.UpkeepAccrued)}.");
                nation.UpkeepAccrued = 0;
                nation.NextPayday = state.CurrentDate.AddDays(Balance.PaydayIntervalDays);
                nation.GraceDaysLeft = 0;
            }
            else if (nation.GraceDaysLeft <= 0)
            {
                int deserters = (int)(nation.Soldiers * Balance.DesertionFractionOnGraceExpiry);
                ArmyHelper.RemoveSoldiers(nation, deserters);
                nation.UpkeepAccrued = 0;
                nation.NextPayday = state.CurrentDate.AddDays(Balance.PaydayIntervalDays);
                Warn(state, nation, $"{deserters:N0} soldiers deserted {nation.Name} — the army was not paid!");
                state.Log($"{nation.Name}: {deserters:N0} soldiers deserted over unpaid maintenance.");
            }
        }

        if (nation.Silver < 0) nation.Silver = 0;
        if (nation.Gold < 0) nation.Gold = 0;
        if (nation.Food < 0) nation.Food = 0;
    }

    private static void Warn(GameState state, Nation nation, string message)
    {
        if (nation.IsPlayer)
            state.ActiveWarnings.Add(message);
    }

    /// <summary>
    /// Daily diplomacy: trade-pact income, relation drift, border attrition
    /// during wars, AI war declarations and AI peace offers, spy network growth.
    /// </summary>
    private void AdvanceDiplomacy(GameState state)
    {
        var player = state.PlayerNation;

        foreach (var other in state.OtherNations)
        {
            if (other.AtWarWithPlayer)
            {
                other.RelationToPlayer = -100;

                // Border skirmishes bleed both armies.
                ArmyHelper.RemoveSoldiers(player, (int)(player.Soldiers * Balance.WarAttritionPerDay));
                ArmyHelper.RemoveSoldiers(other, (int)(other.Soldiers * Balance.WarAttritionPerDay));

                // A stronger AI presses the attack and invades.
                if (!other.IsEliminated
                    && !player.IsEliminated
                    && other.Soldiers > player.Soldiers * Balance.AiInvasionArmyRatio
                    && RollChance(Balance.AiInvasionChancePerDay))
                {
                    var outcome = Warfare.Invade(other, player,
                        other.Soldiers / 2, 1.0, _rng);
                    ArmyHelper.MergeStacks(other, outcome.AttackerSurvivors);
                    state.Log($"{other.Name} invaded: {outcome.Summary}");
                    state.ActiveWarnings.Add($"⚠ {other.Name} is invading!");
                    if (outcome.AttackerWon)
                    {
                        Warfare.AnnexNation(state, other, player);
                        state.Log($"{player.Name} has fallen to {other.Name}!");
                        state.ActiveWarnings.Add($"⚠ {player.Name} has fallen to {other.Name}!");
                    }
                }

                // A clearly beaten AI sues for peace.
                if (other.Soldiers < player.Soldiers * 0.5
                    && RollChance(Balance.AiSueForPeaceChancePerDay))
                {
                    other.AtWarWithPlayer = false;
                    other.RelationToPlayer = -30;
                    state.Log($"{other.Name} sued for peace.");
                    state.ActiveWarnings.Add($"{other.Name} sued for peace — the war is over.");
                }
                continue;
            }

            if (other.HasTradePactWithPlayer)
            {
                player.Silver += Balance.TradePactDailyIncome * player.TradeIncomeMult;
                other.RelationToPlayer = Math.Min(100,
                    other.RelationToPlayer + Balance.TradePactRelationPerDay);
            }
            else
            {
                // Relations drift back toward indifference.
                if (other.RelationToPlayer > 0)
                    other.RelationToPlayer = Math.Max(0,
                        other.RelationToPlayer - Balance.RelationDriftPerDay);
                else if (other.RelationToPlayer < 0)
                    other.RelationToPlayer = Math.Min(0,
                        other.RelationToPlayer + Balance.RelationDriftPerDay);
                // A tolerant court warms relations faster.
                if (player.RelationDriftBonus > 0)
                    other.RelationToPlayer = Math.Min(100,
                        other.RelationToPlayer + player.RelationDriftBonus);
            }

            // A furious neighbour may declare war.
            if (other.RelationToPlayer <= Balance.AiWarRelationThreshold
                && RollChance(Balance.AiDeclareWarChancePerDay))
            {
                other.AtWarWithPlayer = true;
                other.HasTradePactWithPlayer = false;
                other.RelationToPlayer = -100;
                state.Log($"{other.Name} declared war on {player.Name}!");
                state.ActiveWarnings.Add($"⚠ {other.Name} has DECLARED WAR on you!");
            }
        }

        foreach (var net in state.SpyNetworks)
            net.Strength = Math.Min(Balance.MaxNetworkStrength,
                net.Strength + Balance.NetworkGrowthPerDay);
    }

    /// <summary>Colony expeditions sail on; arrivals found new provinces.</summary>
    private static void AdvanceColonisation(GameState state)
    {
        var expedition = state.ActiveExpedition;
        if (expedition is null) return;

        expedition.DaysLeft--;
        if (expedition.DaysLeft > 0) return;

        var region = state.FrontierRegions.FirstOrDefault(r => r.Id == expedition.RegionId);
        if (region is not null)
        {
            state.FrontierRegions.Remove(region);
            var home = state.PlayerNation;
            home.Population += Balance.ColonyStartPopulation;
            home.Farms += 8;
            home.Mines += 2;
            home.Food += 2000;
            home.ColoniesFounded++;
            state.Log($"A colony was founded in {region.Name}! Settlers and riches flow to the homeland.");
        }
        state.ActiveExpedition = null;
    }

    /// <summary>
    /// Marching armies close in on their targets; battles resolve on arrival.
    /// If the war ended or the target is gone, the army returns home.
    /// </summary>
    private void AdvanceMarches(GameState state)
    {
        foreach (var march in state.MarchingArmies.ToList())
        {
            march.DaysLeft--;
            if (march.DaysLeft > 0) continue;

            state.MarchingArmies.Remove(march);
            var attacker = state.AllNations().FirstOrDefault(n => n.Id == march.AttackerNationId);
            var defender = state.AllNations().FirstOrDefault(n => n.Id == march.TargetNationId);

            bool recalled = attacker is null || defender is null
                || attacker.IsEliminated || defender.IsEliminated
                || !defender.AtWarWithPlayer;
            if (recalled)
            {
                if (attacker is not null && !attacker.IsEliminated)
                {
                    ArmyHelper.MergeStacks(attacker, march.Force);
                    if (attacker.IsPlayer)
                        state.Log($"🏳 The march on {march.TargetNationName} was called off — the army returns home.");
                }
                continue;
            }

            var outcome = Warfare.ResolveBattle(march.Force, attacker!, defender!,
                attacker!.BattleStrengthMult, _rng);
            if (!attacker.IsEliminated)
                ArmyHelper.MergeStacks(attacker, outcome.AttackerSurvivors);

            if (outcome.AttackerWon)
            {
                Warfare.AnnexNation(state, attacker, defender);
                state.ActiveWarnings.Add($"🏳 {defender.Name} has been annexed by {attacker.Name}!");
            }

            if (attacker.IsPlayer)
            {
                state.Log($"{defender.Name}: {outcome.Summary}");
                state.ActiveWarnings.Add(
                    $"⚔ Battle for {defender.Name}: {(outcome.AttackerWon ? "victory — the country is ours" : "defeat")}!");
            }
        }
    }

    /// <summary>Defeat: the game is ongoing, there is no victory target —
    /// it ends only when your country is annexed.</summary>
    private static void CheckDefeat(GameState state)
    {
        var player = state.PlayerNation;

        if (!state.Defeated && player.IsEliminated)
        {
            state.Defeated = true;
            state.Log("Your empire has fallen. The dynasty is no more.");
            state.ActiveWarnings.Add("💀 Your empire has fallen.");
        }
    }
}
