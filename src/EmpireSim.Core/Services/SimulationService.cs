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

    /// <summary>
    /// The AI world's own random stream, so AI countries living their lives never disturb the rolls the rest of the
    /// simulation makes (spy discovery, tribute refusals, AI invasions of the player...).
    /// </summary>
    private readonly Random _aiRng;

    /// <param name="seed">Fixed seed for deterministic tests. Null = time-based.</param>
    public SimulationService(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _aiRng = seed.HasValue ? new Random(unchecked(seed.Value * 7919 + 104729)) : new Random();
    }

    public bool RollChance(double probability) => _rng.NextDouble() < probability;
    public double NextDouble() => _rng.NextDouble();
    public int NextInt(int maxExclusive) => _rng.Next(maxExclusive);

    /// <summary>Shared RNG for battle resolution and other chance events.</summary>
    public Random Rng => _rng;

    public void AdvanceDay(GameState state)
    {
        state.ActiveWarnings.Clear();

        foreach (var nation in state.AllNations().ToList())
        {
            // A country that has been annexed is no longer a country: it has no treasury, army, diplomacy or decisions of its own.
            // Its land and people go on living and working as a territory of its master, whose tax collectors and rules govern it.
            if (nation.IsEliminated)
            {
                if (nation.AnnexedById is not null) AdvanceTerritory(state, nation);
                continue;
            }
            AdvanceNation(state, nation);
        }

        AdvanceDiplomacy(state);
        AiWorldService.Advance(state, _aiRng);
        VictoryService.Advance(state);
        PeaceService.Advance(state);
        ResearchService.Advance(state);
        AdvanceColonisation(state);
        AdvanceMarches(state);
        CheckDefeat(state);
    }

    /// <summary>
    /// One day of a country's local economy: raw materials, workshops, construction, births, tax, production, consumption and shortages.
    /// <paramref name="owner"/> is who governs it: the country itself, or the country that annexed it (a territory). The owner collects the
    /// tax (with its own tax multipliers) and is told of what is built and what runs short. Everything else is the same formulas for both.
    /// </summary>
    private void AdvanceEconomy(GameState state, Nation nation, Nation owner)
    {
        // Messages about a territory carry its name, so the owner can tell its territories apart.
        string About(string text) => owner == nation ? text : $"{nation.Name}: {text}";
        nation.MigrateGoodsInventory();

        // Products the Assembly has prohibited are not produced by anyone, in any building (read once for the day).
        var banned = AssemblyService.BannedProducts(state);

        // ---- Raw materials ----
        if (!AssemblyService.IsBanned(banned, "iron")) nation.Iron += nation.Mines * Balance.IronPerMinePerDay;
        if (!AssemblyService.IsBanned(banned, "wood")) nation.Wood += nation.Sawmills * Balance.WoodPerSawmillPerDay;

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
                Report(state, owner, InboxTopic.Production, About($"{spec.Name} completed."), $"{spec.Name} completed");
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
                Report(state, owner, InboxTopic.Production, About($"{BuildingCatalog.Get(project.Building).Name} completed."), $"{BuildingCatalog.Get(project.Building).Name} completed");
            }
            nation.ConstructionQueue.Remove(project);
        }

        // Population always grows; starvation comes only from item shortages (ConsumptionService).
        nation.Population += PopulationService.DailyBirths(nation);

        // ---- Treasury: taxation system ----
        nation.EnsureTaxationInitialized();
        double taxMult = CommandStaffService.TaxMult(owner);

        // Food no longer moves tax approval: neutral ratio until the item-based redesign.
        double foodRatio = TaxationService.NormalSupplyRatio;

        // Tax revenue
        double taxRevenue = TaxationService.TotalRevenue(nation.Workforce, nation.TaxRates) * taxMult * owner.TaxMult;
        // (a territory's tax is its owner's money; it has no crown domain of its own: those went to the owner when it was annexed)
        owner.Gold += taxRevenue + Balance.CrownDomainIncomePerDay * nation.CrownDomains;

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
            if (AssemblyService.IsBanned(banned, spec.Produces)) continue;   // prohibited by the Assembly: nothing is made
            double catMult = spec.Category == ProductionCategory.Food ? foodMult
                           : spec.Category == ProductionCategory.Minerals ? resMult : 1.0;
            // (A production law from an older save that named this country halves its whole output, as it did then.)
            double assemblyMult = AssemblyService.HasActivePolicy(state.ActiveAssemblyPolicies, AssemblyProposalTypes.ProductionBan, nation.Id, state.CurrentDate) ? 0.5 : 1.0;
            double dailyOutput = kvp.Value * spec.OutputPerDay * prodMult * catMult * genMult * assemblyMult;
            nation.AddProduct(spec.Produces, dailyOutput);
        }

        // ---- Population consumption: fixed per-person daily usage of each item ----
        var shortage = ConsumptionService.Apply(nation);
        ConsumptionService.ApplyShortageEffects(nation, shortage);
        if (shortage.ShortItems.Any())
            Warn(state, owner, $"Shortage in {nation.Name}: {string.Join(", ", shortage.ShortItems)} — " +
                $"{nation.LastShortageDeaths:N1} deaths/day, ruler rating -{nation.LastRatingDrop:0.######}/day.");

        // The inbox hears of a shortage when it begins or when the list of short items changes — not every day it goes on.
        if (owner.IsPlayer)
        {
            string condition = owner == nation ? "shortage" : $"shortage:{nation.Id}";
            if (shortage.ShortItems.Any())
            {
                string items = string.Join(", ", shortage.ShortItems);
                string where = owner == nation ? "" : $" in {nation.Name}";
                InboxService.NotifyCondition(state, InboxTopic.Shortage, condition, items,
                    $"Shortage of {items}{where}.",
                    title: owner != nation ? $"Shortage in {nation.Name}" : shortage.ShortItems.Count() == 1 ? $"Shortage of {shortage.ShortItems.First()}" : "Product shortages",
                    details: $"{(owner == nation ? "Your people lack" : $"The people of {nation.Name} lack")}: {items}.\nAt the time of this report the shortage costs {nation.LastShortageDeaths:N1} lives a day and lowers the ruler rating by {nation.LastRatingDrop:0.######} a day.");
            }
            else
                InboxService.ClearCondition(state, condition);
        }
    }

    /// <summary>
    /// A territory (an annexed country) lives on: its mines, mills, builders, people, tax and shortages run by exactly the formulas of every
    /// other economy, but it is not a country any more: its tax and any gold it digs belong to its master, which also hears of its shortages.
    /// Nothing of a country's own life (army, diplomacy, decisions, upkeep, trade, Assembly) runs for it.
    /// </summary>
    private void AdvanceTerritory(GameState state, Nation territory)
    {
        if (territory.IsPlayer) return;   // (the player's own fall ends the game)
        var owner = state.AllNations().FirstOrDefault(n => n.Id == territory.AnnexedById);
        if (owner is null || owner.IsEliminated) return;

        AdvanceEconomy(state, territory, owner);
        if (territory.Gold != 0) { owner.Gold += territory.Gold; territory.Gold = 0; }   // (gold mines pay the owner directly)
    }

    private void AdvanceNation(GameState state, Nation nation)
    {
        AdvanceEconomy(state, nation, nation);

        // ---- Military crafting: progress projects, add 10 units on completion ----
        var bannedItems = AssemblyService.BannedProducts(state);
        foreach (var proj in nation.MilitaryCraftQueue.ToList())
        {
            proj.DaysLeft -= 1;
            if (proj.DaysLeft > 0) continue;
            var recipe = MilitaryRecipes.Get(proj.RecipeId);
            if (AssemblyService.IsBanned(bannedItems, proj.RecipeId))
            {
                // The Assembly prohibits making this item: the batch comes to nothing.
                state.Log($"{nation.Name}: crafting {recipe.Name} is prohibited by the Assembly — the batch is lost.");
                Report(state, nation, InboxTopic.Production, $"Crafting {recipe.Name} is prohibited by the Assembly: the batch was lost.", $"{recipe.Name} prohibited");
                nation.MilitaryCraftQueue.Remove(proj);
                continue;
            }
            double craftAmount = 10 * LawService.MilitaryGoodsMult(nation);
            nation.AddMilitaryItem(proj.RecipeId, craftAmount);
            state.Log($"{nation.Name}: Crafted 10x {recipe.Name}.");
            Report(state, nation, InboxTopic.Production, $"Crafted {craftAmount:N0}x {recipe.Name}.", $"{recipe.Name} crafted");
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
                        seller.Gold += tc.TotalValue * LawService.ExportRevenueMult(seller) * CommandStaffService.ExportRevenueMult(seller) * agreementMult;
                    }
                }
                else
                {
                    // Buyer can't pay - cancel, return goods to player
                    if (seller is not null) seller.AdjustProductStock(tc.ProductId, tc.Quantity);
                    tc.Status = TradeStatus.Cancelled;
                    state.LogMovement(MovementKind.Goods, MovementStatus.Failed, tc.SellerId, seller?.Name ?? "unknown", buyer.Id, buyer.Name,
                        $"Trade failed: buyer could not pay for {product.Name}.", inbox: InboxTopic.Trade);
                    continue;
                }
            }
            tc.Status = TradeStatus.Delivered;
            tc.ActualDeliveryDate = state.CurrentDate;
            state.LogMovement(MovementKind.Goods, MovementStatus.Completed, tc.SellerId, seller?.Name ?? "unknown", buyer.Id, buyer.Name,
                $"Trade delivered: {tc.Quantity:N0} {product.Name} ({Currency.Cost(tc.TotalValue)}).", inbox: InboxTopic.Trade);
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
                Report(state, nation, InboxTopic.RulerRating, $"{def.Name} completed. Ruler rating +{def.RulerRatingEffect}.", $"{def.Name} completed");
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
                if (n.Id == prop.TargetId || n.IsEliminated) continue;   // the target does not vote, nor does an annexed country
                // Simple AI: vote based on relation to proposer
                int hash = (n.Id + prop.Id).GetHashCode();
                bool voteFor = (Math.Abs(hash) % 100) < 50;
                if (n.Id == prop.ProposerId) voteFor = true;
                prop.Votes[n.Id] = voteFor;
            }
            int forV = prop.VotesFor(allNations);
            int againstV = prop.VotesAgainst(allNations);
            var ptype = AssemblyProposalTypes.Get(prop.TypeId);
            string proposerName = TreatyService.NameOf(state, prop.ProposerId);
            var (partyId, targetName) = AssemblyService.Party(state, prop.TargetId);   // the country it is about, or the Assembly for a global law
            string what = AssemblyService.Describe(state, prop.TypeId, prop.TargetId, prop.ProductId);
            if (forV > againstV)
            {
                prop.Status = ProposalStatus.Approved;
                // Activate policy (carrying exactly the scope the proposal had: its country, or none for a global law, and its product)
                state.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy
                {
                    ProposalId = prop.Id,
                    TypeId = prop.TypeId,
                    TargetId = prop.TargetId,
                    ProductId = prop.ProductId,
                    ActivationDate = state.CurrentDate,
                    ExpirationDate = state.CurrentDate.AddDays(prop.EffectDurationDays)
                });
                prop.Status = ProposalStatus.Active;
                prop.ActiveUntil = state.CurrentDate.AddDays(prop.EffectDurationDays);
                state.LogMovement(MovementKind.Assembly, MovementStatus.Completed, prop.ProposerId, proposerName, partyId, targetName,
                    $"Assembly APPROVED: {what} ({forV} vs {againstV}).", inbox: InboxTopic.Assembly);
            }
            else
            {
                prop.Status = ProposalStatus.Rejected;
                state.LogMovement(MovementKind.Assembly, MovementStatus.Failed, prop.ProposerId, proposerName, partyId, targetName,
                    $"Assembly REJECTED: {what} ({forV} vs {againstV}).", inbox: InboxTopic.Assembly);
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
                string expiredBy = prop?.ProposerId ?? state.PlayerNation.Id;
                var (expiredPartyId, expiredPartyName) = AssemblyService.Party(state, pol.TargetId);
                state.LogMovement(MovementKind.Assembly, MovementStatus.Completed, expiredBy, TreatyService.NameOf(state, expiredBy),
                    expiredPartyId, expiredPartyName,
                    $"Assembly policy expired: {AssemblyService.Describe(state, pol.TypeId, pol.TargetId, pol.ProductId)}.", inbox: InboxTopic.Assembly);
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
                    Report(state, nation, InboxTopic.Religion, $"{nation.Name} converted to {newRel.Name}!", "Conversion complete");
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
            Report(state, nation, InboxTopic.Production, $"Recruited {rec.Count:N0} {spec.Name}.", "Recruitment complete");
            nation.RecruitmentQueue.Remove(rec);
        }

        double landUpkeepMult = CommandStaffService.LandUpkeepMult(nation);
        double navalUpkeepMult = CommandStaffService.NavalUpkeepMult(nation);

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
                Report(state, nation, InboxTopic.Upkeep,
                    $"Army maintenance is due: {Currency.Format(nation.UpkeepAccrued)} owed, the treasury holds {Currency.Format(nation.Gold)}. Pay within {Balance.GracePeriodDays} days or soldiers will desert.",
                    "Army maintenance due");
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
                Report(state, nation, InboxTopic.Upkeep, $"{deserters:N0} soldiers deserted because the army was not paid.", "Soldiers deserted");
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

    /// <summary>Reports an event to the player's inbox. Only the player's own country has an inbox; other countries' events are not reported.</summary>
    private static void Report(GameState state, Nation nation, InboxTopic topic, string summary, string? title = null)
    {
        if (nation.IsPlayer)
            InboxService.Notify(state, topic, summary, title: title);
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
            if (other.IsEliminated) continue;   // a country that no longer exists neither fights nor declares war

            if (other.AtWarWithPlayer)
            {
                other.RelationToPlayer = -100;

                // A country the player has just beaten is waiting to hear its fate: it fights, invades and pleads no more.
                if (VictoryService.Pending(state, other.Id) is not null) continue;

                // Border skirmishes bleed both armies.
                ArmyHelper.RemoveSoldiers(player, (int)(player.Soldiers * Balance.WarAttritionPerDay * CommandStaffService.AttritionMult(player)));
                ArmyHelper.RemoveSoldiers(other, (int)(other.Soldiers * Balance.WarAttritionPerDay * CommandStaffService.AttritionMult(other)));

                // A stronger AI presses the attack: it sends half of its home army marching on the player's capital.
                // The soldiers leave its home army at once (so they cannot be sent twice), they take as long as the distance
                // takes, and the battle is fought when they arrive (AdvanceMarches) — never on the day it decides to attack.
                if (!other.IsEliminated
                    && !player.IsEliminated
                    && other.Soldiers > player.Soldiers * Balance.AiInvasionArmyRatio
                    && !TreatyService.HasMarchOn(state, other.Id, player.Id)
                    && RollChance(Balance.AiInvasionChancePerDay))
                {
                    int commit = other.Soldiers / 2;
                    var march = commit >= Balance.MinInvasionForce ? TreatyService.LaunchMarch(state, other, player, commit) : null;
                    if (march is not null)
                    {
                        state.LogMovement(MovementKind.March, MovementStatus.UnderWay, other, player,
                            $"⚔ {other.Name} marches {march.Strength:N0} soldiers on {player.Name} — they arrive in {march.TotalDays} days ({march.ArrivalDate:dd-MM-yyyy})!",
                            inbox: InboxTopic.Invasion);
                        state.ActiveWarnings.Add($"⚠ {other.Name} is marching on you — arrival in {march.TotalDays} days!");
                    }
                }

                // (A country that has had enough does not end the war by itself: it puts terms to the player, see PeaceService.)
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
                // Relations drift back toward where they began: indifference, or the country's historical friendship or
                // quarrel with the player's country (BaselineRelation), which a year of peace does not make it forget.
                double baseline = other.BaselineRelation;
                if (other.RelationToPlayer > baseline)
                    other.RelationToPlayer = Math.Max(baseline,
                        other.RelationToPlayer - Balance.RelationDriftPerDay);
                else if (other.RelationToPlayer < baseline)
                    other.RelationToPlayer = Math.Min(baseline,
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
                && TreatyService.WarDeclarationBlock(state) is null
                && RollChance(Balance.AiDeclareWarChancePerDay))
            {
                AiWorldService.DeclareWarOnPlayer(state, other);
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
                $"A colony was founded in {region.Name}! Settlers and riches flow to the homeland.", inbox: InboxTopic.Colony);
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
            // The arrival date decides (so a saved and loaded army arrives on the same day); a march from an old save has none.
            if (march.HasDates) march.DaysLeft = march.ArrivalDate.DayNumber - state.CurrentDate.DayNumber;
            else march.DaysLeft--;
            if (march.DaysLeft > 0) continue;

            state.MarchingArmies.Remove(march);
            var attacker = state.AllNations().FirstOrDefault(n => n.Id == march.AttackerNationId);
            var defender = state.AllNations().FirstOrDefault(n => n.Id == march.TargetNationId);

            bool recalled = attacker is null || defender is null
                || attacker.IsEliminated || defender.IsEliminated
                || !AiWorldService.InWar(state, attacker, defender)
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
                        march.AttackerNationId, march.AttackerNationName, march.TargetNationId, march.TargetNationName, calledOff, inbox: InboxTopic.ArmyMovement);
                }
                continue;
            }

            var def = defender!;
            // A battle in the player's war is rolled on the main stream; a war between AI countries on the AI world's own,
            // so AI countries fighting never disturb the rolls the rest of the simulation makes.
            var battleRng = attacker!.IsPlayer || def.IsPlayer || def.AtWarWithPlayer ? _rng : _aiRng;
            var outcome = Warfare.ResolveBattle(march.Force, attacker, def,
                attacker.BattleStrengthMult, battleRng);
            if (!attacker.IsEliminated)
                ArmyHelper.MergeStacks(attacker, outcome.AttackerSurvivors);

            // The spoils of a won battle. The player chooses (annex, take resources, or nothing); an AI winner settles the war
            // by what it was fought for (conquest, humiliation or containment). An enemy army that beats the player's home
            // army takes the country (below).
            bool homeAttacked = def.IsPlayer;

            // How the player's war is going (what the other side weighs when it judges peace terms).
            int attackerLost = outcome.AttackerCommitted - outcome.AttackerSurvivors.Sum(s => s.Count);
            if (attacker.IsPlayer)
                PeaceService.RecordBattle(state, def, playerWon: outcome.AttackerWon, playerLosses: attackerLost, enemyLosses: outcome.DefenderCasualties);
            else if (homeAttacked)
                PeaceService.RecordBattle(state, attacker, playerWon: !outcome.AttackerWon, playerLosses: outcome.DefenderCasualties, enemyLosses: attackerLost);

            if (outcome.AttackerWon)
            {
                if (attacker.IsPlayer) VictoryService.Begin(state, attacker, def);
                else if (!homeAttacked) VictoryService.ResolveForAi(state, attacker, def);
            }

            if (attacker.IsPlayer)
            {
                state.LogMovement(MovementKind.March, MovementStatus.Completed, attacker, def, $"{def.Name}: {outcome.Summary}", inbox: InboxTopic.Battle);
                state.ActiveWarnings.Add(
                    $"⚔ Battle for {def.Name}: {(outcome.AttackerWon ? "victory — choose its fate (annex, take resources, or nothing)" : "defeat")}!");
            }
            else if (homeAttacked)
            {
                // An enemy army has reached the player's country: the battle is fought against the troops actually at home now.
                int fallen = outcome.AttackerCommitted - outcome.AttackerSurvivors.Sum(s => s.Count);
                string report = outcome.AttackerWon
                    ? $"⚠ {attacker.Name}'s army of {outcome.AttackerCommitted:N0} reached {def.Name} and broke the home army ({fallen:N0} of its men fell, the garrison lost {outcome.DefenderCasualties:N0})."
                    : $"🛡 {def.Name} repelled {attacker.Name}'s army: {outcome.AttackerCommitted:N0} attacked, {fallen:N0} fell; the garrison lost {outcome.DefenderCasualties:N0}.";
                state.LogMovement(MovementKind.March, MovementStatus.Completed, attacker, def, report, inbox: InboxTopic.Battle);
                if (outcome.AttackerWon && TreatyService.AnnexationBlock(state, def.Id) is { } cannotAnnex)
                {
                    // The battle is lost, but the country cannot be taken: the Assembly forbids annexation, so the war simply goes on.
                    state.LogMovement(MovementKind.War, MovementStatus.Failed, attacker, def,
                        $"{attacker.Name} broke the home army of {def.Name} but cannot annex it ({cannotAnnex}) — the war goes on.");
                    state.ActiveWarnings.Add($"⚠ {attacker.Name} beat your home army, but the Assembly forbids annexing you.");
                }
                else if (outcome.AttackerWon)
                {
                    Warfare.AnnexNation(state, attacker, def);
                    state.LogMovement(MovementKind.War, MovementStatus.Completed, attacker, def, $"{def.Name} has fallen to {attacker.Name}!");
                    state.ActiveWarnings.Add($"⚠ {def.Name} has fallen to {attacker.Name}!");
                }
                else
                    state.ActiveWarnings.Add($"🛡 {attacker.Name}'s army was beaten at your gates!");
            }
            else
            {
                // An ally fighting the player's enemy.
                state.LogMovement(MovementKind.March, MovementStatus.Completed, attacker, def, $"{attacker.Name} vs {def.Name}: {outcome.Summary}", inbox: InboxTopic.AllyAssistance);
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
            InboxService.Notify(state, InboxTopic.EmpireFallen, "Your empire has fallen. The dynasty is no more.");
        }
    }
}
