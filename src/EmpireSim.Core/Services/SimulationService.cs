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
        nation.MigrateGoodsInventory();

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

            if (project.ProductionBuildingId is not null)
            {
                // Production building completed.
                var spec = ProductionCatalog.Get(project.ProductionBuildingId);
                if (!nation.ProductionBuildings.ContainsKey(spec.Id))
                    nation.ProductionBuildings[spec.Id] = 0;
                nation.ProductionBuildings[spec.Id]++;
                state.Log($"{nation.Name}: {spec.Name} completed.");
            }
            else
            {
                switch (project.Building)
                {
                    case BuildingType.Farm: nation.Farms++; break;
                    case BuildingType.Mine: nation.Mines++; break;
                    case BuildingType.Sawmill: nation.Sawmills++; break;
                    case BuildingType.Workshop: nation.Workshops++; break;
                }
                state.Log($"{nation.Name}: {BuildingCatalog.Get(project.Building).Name} completed.");
            }
            nation.ConstructionQueue.Remove(project);
        }

        // Population always grows; starvation comes only from item shortages (ConsumptionService).
        nation.Population += PopulationService.DailyBirths(nation);

        // ---- Treasury: taxation system ----
        nation.EnsureTaxationInitialized();
        double taxMult = nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCTaxMult : 1.0;

        // Food no longer moves tax approval: neutral ratio until the item-based redesign.
        double foodRatio = TaxationService.NormalSupplyRatio;

        // Tax revenue
        double taxRevenue = TaxationService.TotalRevenue(nation.Workforce, nation.TaxRates) * taxMult * nation.TaxMult;
        nation.Gold += taxRevenue + Balance.CrownDomainIncomePerDay;

        // Approval update (gradual)
        double burden = TaxationService.WeightedBurden(nation.Workforce, nation.TaxRates);
        double target = TaxationService.TargetApproval(burden, foodRatio);
        nation.TaxApproval += (target - nation.TaxApproval) * 0.1;
        nation.TaxApproval = Math.Clamp(nation.TaxApproval, 0, 100);

        // ---- Production buildings: daily output goes to goods inventory ----
        // Buddhism: +5% goods production speed; Laws: various modifiers
        double prodMult = ReligionService.ProductionSpeedMult(nation);
        double foodMult = LawService.FoodOutputMult(nation);
        double resMult = LawService.ResourceOutputMult(nation);
        double milMult = LawService.MilitaryGoodsMult(nation);
        double genMult = LawService.GeneralProdOutputMult(nation);
        foreach (var kvp in nation.ProductionBuildings)
        {
            var spec = ProductionCatalog.Get(kvp.Key);
            double catMult = spec.Category == ProductionCategory.Food ? foodMult
                           : spec.Category == ProductionCategory.Minerals ? resMult : 1.0;
            // Assembly: production ban reduces output by 50%
            double assemblyMult = AssemblyService.HasActivePolicy(state.ActiveAssemblyPolicies, "production_ban", nation.Id, state.CurrentDate) ? 0.5 : 1.0;
            double dailyOutput = kvp.Value * spec.OutputPerDay * prodMult * catMult * genMult * assemblyMult;
            nation.AddProduct(spec.Produces, dailyOutput);
        }

        // ---- Population consumption: fixed per-person daily usage of each item ----
        var shortage = ConsumptionService.Apply(nation);
        ConsumptionService.ApplyShortageEffects(nation, shortage);
        if (shortage.ShortItems.Any())
            Warn(state, nation, $"Shortage in {nation.Name}: {string.Join(", ", shortage.ShortItems)} — " +
                $"{nation.LastShortageDeaths:N1} deaths/day, ruler rating -{nation.LastRatingDrop:0.######}/day.");

        // ---- Military crafting: progress projects, add 10 units on completion ----
        foreach (var proj in nation.MilitaryCraftQueue.ToList())
        {
            proj.DaysLeft -= 1;
            if (proj.DaysLeft > 0) continue;
            var recipe = MilitaryRecipes.Get(proj.RecipeId);
            double craftAmount = 10 * LawService.MilitaryGoodsMult(nation);
            nation.AddMilitaryItem(proj.RecipeId, craftAmount);
            state.Log($"{nation.Name}: Crafted 10x {recipe.Name}.");
            nation.MilitaryCraftQueue.Remove(proj);
        }

        // ---- Trade contracts: deliver on delivery date ----
        foreach (var tc in state.TradeContracts.Where(c => c.Status == TradeStatus.InTransit).ToList())
        {
            if (state.CurrentDate < tc.DeliveryDate) continue;
            var buyer = state.AllNations().FirstOrDefault(n => n.Id == tc.BuyerId);
            var seller = state.AllNations().FirstOrDefault(n => n.Id == tc.SellerId);
            var product = TradeCatalog.Get(tc.ProductId);
            if (buyer is null || product is null) { tc.Status = TradeStatus.Cancelled; continue; }

            // Deliver goods to buyer
            buyer.AdjustProductStock(tc.ProductId, tc.Quantity);
            if (tc.IsPlayerBuyer)
            {
                // Player bought: gold already paid at confirmation, AI seller gets it now
                if (seller is not null) seller.Gold += tc.TotalValue;
            }
            else
            {
                // Player sold: deduct buyer gold, credit player gold (with export law bonus)
                if (buyer.Gold >= tc.TotalValue)
                {
                    buyer.Gold -= tc.TotalValue;
                    if (seller is not null)
                    {
                        // Trade agreement: partners pay more for what we export.
                        double agreementMult = TreatyService.Has(state, TreatyType.TradeAgreement, seller.Id, buyer.Id)
                            ? Balance.TradeAgreementExportMult : 1.0;
                        seller.Gold += tc.TotalValue * LawService.ExportRevenueMult(seller) * agreementMult;
                    }
                }
                else
                {
                    // Buyer can't pay - cancel, return goods to player
                    if (seller is not null) seller.AdjustProductStock(tc.ProductId, tc.Quantity);
                    tc.Status = TradeStatus.Cancelled;
                    state.LogMovement(MovementKind.Goods, MovementStatus.Failed, tc.SellerId, seller?.Name ?? "unknown", buyer.Id, buyer.Name,
                        $"Trade failed: buyer could not pay for {product.Name}.");
                    continue;
                }
            }
            tc.Status = TradeStatus.Delivered;
            tc.ActualDeliveryDate = state.CurrentDate;
            state.LogMovement(MovementKind.Goods, MovementStatus.Completed, tc.SellerId, seller?.Name ?? "unknown", buyer.Id, buyer.Name,
                $"Trade delivered: {tc.Quantity:N0} {product.Name} ({Currency.Cost(tc.TotalValue)}).");
        }

        // ---- National events: complete on date, apply ruler rating ----
        foreach (var evt in nation.NationalEvents.Where(e => e.Status == NationalEventStatus.InProgress).ToList())
        {
            if (state.CurrentDate < evt.ExpectedCompletion) continue;
            var def = NationalEventCatalog.Get(evt.EventTypeId);
            if (def is not null && !evt.CompletionApplied)
            {
                nation.RulerRating = Math.Clamp(nation.RulerRating + def.RulerRatingEffect, 0, 100);
                evt.CompletionApplied = true;
                state.Log($"{def.Name} completed. Ruler rating +{def.RulerRatingEffect}.");
            }
            evt.Status = NationalEventStatus.Completed;
            evt.ActualCompletion = state.CurrentDate;
        }

        // ---- Assembly: resolve voting deadlines, expire policies ----
        foreach (var prop in state.AssemblyProposals.Where(p => p.Status == ProposalStatus.VotingOpen).ToList())
        {
            if (state.CurrentDate < prop.VotingDeadline) continue;
            // AI votes (deterministic)
            var allNations = state.AllNations().ToList();
            foreach (var n in allNations)
            {
                if (prop.Votes.ContainsKey(n.Id)) continue;
                if (n.Id == prop.TargetId) continue;
                // Simple AI: vote based on relation to proposer
                int hash = (n.Id + prop.Id).GetHashCode();
                bool voteFor = (Math.Abs(hash) % 100) < 50;
                if (n.Id == prop.ProposerId) voteFor = true;
                prop.Votes[n.Id] = voteFor;
            }
            int forV = prop.VotesFor(allNations);
            int againstV = prop.VotesAgainst(allNations);
            var ptype = AssemblyProposalTypes.Get(prop.TypeId);
            if (forV > againstV)
            {
                prop.Status = ProposalStatus.Approved;
                // Activate policy
                state.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy
                {
                    ProposalId = prop.Id,
                    TypeId = prop.TypeId,
                    TargetId = prop.TargetId,
                    ActivationDate = state.CurrentDate,
                    ExpirationDate = state.CurrentDate.AddDays(prop.EffectDurationDays)
                });
                prop.Status = ProposalStatus.Active;
                prop.ActiveUntil = state.CurrentDate.AddDays(prop.EffectDurationDays);
                state.Log($"Assembly APPROVED: {ptype?.Name} against {prop.TargetId} ({forV} vs {againstV}).");
            }
            else
            {
                prop.Status = ProposalStatus.Rejected;
                state.Log($"Assembly REJECTED: {ptype?.Name} ({forV} vs {againstV}).");
            }
        }
        // Expire policies
        foreach (var pol in state.ActiveAssemblyPolicies.ToList())
        {
            if (state.CurrentDate >= pol.ExpirationDate)
            {
                state.ActiveAssemblyPolicies.Remove(pol);
                var prop = state.AssemblyProposals.FirstOrDefault(p => p.Id == pol.ProposalId);
                if (prop is not null) prop.Status = ProposalStatus.Expired;
                state.Log($"Assembly policy expired: {pol.TypeId}.");
            }
        }

        // ---- Religion conversion: advance, complete on date ----
        if (nation.ReligionConversion?.InProgress == true)
        {
            var conv = nation.ReligionConversion;
            if (state.CurrentDate >= conv.ExpectedCompletion)
            {
                var newRel = ReligionCatalog.Get(conv.TargetReligionId);
                if (newRel is not null)
                {
                    nation.Religion = newRel.Name;
                    state.Log($"{nation.Name} converted to {newRel.Name}!");
                }
                nation.ReligionConversion = null;
            }
        }

        // ---- Recruitment queue: progress, add soldiers on completion ----
        foreach (var rec in nation.RecruitmentQueue.ToList())
        {
            rec.DaysLeft -= 1;
            if (rec.DaysLeft > 0) continue;
            var spec = UnitCatalog.Get(rec.Type);
            var stack = nation.Units.FirstOrDefault(u => u.Type == rec.Type);
            if (stack is null) nation.Units.Add(new UnitStack { Type = rec.Type, Count = rec.Count });
            else stack.Count += rec.Count;
            state.Log($"{nation.Name}: Recruited {rec.Count:N0} {spec.Name}.");
            nation.RecruitmentQueue.Remove(rec);
        }

        double landUpkeepMult = (nation.HasCommander(CommanderRole.LandCommander) ? Balance.LandCommanderUpkeepMult : 1.0)
                              * (nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCUpkeepMult : 1.0);
        double navalUpkeepMult = (nation.HasCommander(CommanderRole.FleetCommander) ? Balance.FleetCommanderUpkeepMult : 1.0)
                               * (nation.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCUpkeepMult : 1.0);

        double landUpkeep = nation.Units.Sum(u => u.Count * UnitCatalog.Get(u.Type).UpkeepPerDay) * landUpkeepMult * nation.UpkeepMultExtra;
        double navalUpkeep = nation.Warships * WarshipSpec.UpkeepPerDay * navalUpkeepMult;
        double wages = nation.Commanders.Sum(c => c.DailyWage);
        // Laws: military maintenance modifier
        double maintMult = LawService.MilitaryMaintenanceMult(nation);
        nation.UpkeepAccrued += (landUpkeep + navalUpkeep + wages) * maintMult;

        // ---- 6-month payday ----
        if (state.CurrentDate >= nation.NextPayday && !nation.IsInGracePeriod)
        {
            if (nation.CanPay(nation.UpkeepAccrued))
            {
                nation.PayGold(nation.UpkeepAccrued);
                state.Log($"{nation.Name} paid army maintenance: {Currency.Format(nation.UpkeepAccrued)}.");
                nation.UpkeepAccrued = 0;
                nation.NextPayday = nation.NextPayday.AddDays(Balance.PaydayIntervalDays);
            }
            else
            {
                nation.GraceDaysLeft = Balance.GracePeriodDays;
                Warn(state, nation,
                    $"ARMY MAINTENANCE DUE in {nation.Name}! Need {Currency.Format(nation.UpkeepAccrued)}, treasury holds {Currency.Format(nation.Gold)}. " +
                    $"Pay within {Balance.GracePeriodDays} days or soldiers will desert.");
            }
        }
        else if (nation.IsInGracePeriod)
        {
            nation.GraceDaysLeft--;
            if (nation.CanPay(nation.UpkeepAccrued))
            {
                // Paid during grace.
                nation.PayGold(nation.UpkeepAccrued);
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

        if (nation.Gold < 0) nation.Gold = 0;
        if (nation.Gold < 0) nation.Gold = 0;
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
                    state.LogMovement(MovementKind.March, MovementStatus.Completed, other, player, $"{other.Name} invaded: {outcome.Summary}");
                    state.ActiveWarnings.Add($"⚠ {other.Name} is invading!");
                    if (outcome.AttackerWon)
                    {
                        Warfare.AnnexNation(state, other, player);
                        state.LogMovement(MovementKind.War, MovementStatus.Completed, other, player, $"{player.Name} has fallen to {other.Name}!");
                        state.ActiveWarnings.Add($"⚠ {player.Name} has fallen to {other.Name}!");
                    }
                }

                // A clearly beaten AI sues for peace.
                if (other.Soldiers < player.Soldiers * 0.5
                    && RollChance(Balance.AiSueForPeaceChancePerDay))
                {
                    other.AtWarWithPlayer = false;
                    other.RelationToPlayer = -30;
                    state.LogMovement(MovementKind.War, MovementStatus.Completed, other, player, $"{other.Name} sued for peace.");
                    state.ActiveWarnings.Add($"{other.Name} sued for peace — the war is over.");
                }
                continue;
            }

            if (other.HasTradePactWithPlayer)
            {
                player.Gold += Balance.TradePactDailyIncome * player.TradeIncomeMult;
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
            // (A non-aggression pact or alliance keeps even a furious neighbour from declaring war.)
            if (other.RelationToPlayer <= Balance.AiWarRelationThreshold
                && TreatyService.ForbidsAttack(state, other.Id, player.Id) is null
                && RollChance(Balance.AiDeclareWarChancePerDay))
            {
                other.AtWarWithPlayer = true;
                other.HasTradePactWithPlayer = false;
                other.RelationToPlayer = -100;
                TreatyService.OnWar(state, player, other);
                state.LogMovement(MovementKind.War, MovementStatus.Completed, other, player, $"{other.Name} declared war on {player.Name}!");
                state.ActiveWarnings.Add($"⚠ {other.Name} has DECLARED WAR on you!");
                TreatyService.AllianceDefence(state, other, player);
            }
        }

        foreach (var net in state.SpyNetworks)
            net.Strength = Math.Min(Balance.MaxNetworkStrength,
                net.Strength + Balance.NetworkGrowthPerDay);

        // Treaties lapse, loans come home, embassies and alliances warm relations.
        TreatyService.Advance(state);
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
            home.AddProduct("Wheat", 2000);
            home.ColoniesFounded++;
            // Remember what the colony added, so it can be owned (and presented to another nation).
            state.Colonies.Add(new Colony
            {
                Name = region.Name,
                OwnerId = home.Id,
                FoundedById = home.Id,
                FoundedDate = state.CurrentDate,
                Population = Balance.ColonyStartPopulation,
                Farms = 8,
                Mines = 2,
            });
            state.LogMovement(MovementKind.Colony, MovementStatus.Completed, home.Id, home.Name, region.Id, region.Name,
                $"A colony was founded in {region.Name}! Settlers and riches flow to the homeland.");
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
                || !defender.AtWarWithPlayer
                || TreatyService.ForbidsAttack(state, attacker.Id, defender.Id) is not null;
            if (recalled)
            {
                if (attacker is not null && !attacker.IsEliminated)
                {
                    ArmyHelper.MergeStacks(attacker, march.Force);
                    string calledOff = attacker.IsPlayer
                        ? $"🏳 The march on {march.TargetNationName} was called off — the army returns home."
                        : $"🏳 {attacker.Name}'s march on {march.TargetNationName} was called off — the army returns home.";
                    state.LogMovement(MovementKind.March, MovementStatus.Failed,
                        march.AttackerNationId, march.AttackerNationName, march.TargetNationId, march.TargetNationName, calledOff);
                }
                continue;
            }

            var def = defender!;
            var outcome = Warfare.ResolveBattle(march.Force, attacker!, def,
                attacker!.BattleStrengthMult, _rng);
            if (!attacker.IsEliminated)
                ArmyHelper.MergeStacks(attacker, outcome.AttackerSurvivors);

            if (outcome.AttackerWon)
            {
                Warfare.AnnexNation(state, attacker, def);
                state.ActiveWarnings.Add($"🏳 {def.Name} has been annexed by {attacker.Name}!");
            }

            if (attacker.IsPlayer)
            {
                state.LogMovement(MovementKind.March, MovementStatus.Completed, attacker, def, $"{def.Name}: {outcome.Summary}");
                state.ActiveWarnings.Add(
                    $"⚔ Battle for {def.Name}: {(outcome.AttackerWon ? "victory — the country is ours" : "defeat")}!");
            }
            else
            {
                // An ally fighting the player's enemy.
                state.LogMovement(MovementKind.March, MovementStatus.Completed, attacker, def, $"{attacker.Name} vs {def.Name}: {outcome.Summary}");
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
