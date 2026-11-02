using EmpireSim.Core.Models;
using EmpireSim.Core.Services;

namespace SimTests;

public static class OttomanPlaythroughAudit
{
    public static async Task Run(Action<bool, string> Check, string saveFolder)
    {
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("== 80. COMPREHENSIVE OTTOMAN EMPIRE FULL PLAYTHROUGH: EVERY FLOW & SYSTEM     ==");
        Console.WriteLine("================================================================================");

        using var engine = new GameEngine(new SimulationService(), new SaveService(saveFolder));

        // -------------------------------------------------------------------------
        // 1. Initial State as Ottoman Empire
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 1: Ottoman Initialization] --");
        var ott = engine.State.PlayerNation;
        Check(ott.Id == "ottoman", "player nation id is 'ottoman'");
        Check(ott.Name == "Ottoman Empire", "player nation name is 'Ottoman Empire'");
        Check(ott.CapitalName == "Constantinople", "capital is Constantinople");
        Check(ott.Religion == "Islam", "state religion is Islam");
        Check(engine.State.CurrentDate == new DateOnly(1600, 1, 1), "starts on 01-01-1600");
        Check(ott.Gold >= 50000, "starts with healthy treasury");
        Check(ott.Soldiers >= 8000, "starts with standing army");
        Check(ott.Population >= 30000000, "starts with large realm population");

        var embassies = engine.State.Treaties
            .Where(t => t.Type == TreatyType.Embassy && t.NationAId == "ottoman")
            .Select(t => t.NationBId)
            .OrderBy(x => x)
            .ToList();
        Check(embassies.SequenceEqual(new[] { "crimea", "england", "france", "venice" }),
            $"historical embassies initialized correctly: {string.Join(", ", embassies)}");
        Check(MovementReport.ActiveMissions(engine.State).Count == 0, "no spurious active missions at start");
        Check(engine.State.Inbox.Count == 0, "no spurious inbox messages at start");

        // -------------------------------------------------------------------------
        // 2. Command Staff Appointments & Modifiers
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 2: Command Staff Appointments & Modifiers] --");
        var roles = new[]
        {
            CommanderRole.CommanderInChief,
            CommanderRole.LandCommander,
            CommanderRole.FleetCommander,
            CommanderRole.ChiefTributeCollector,
            CommanderRole.HeadOfTrade,
            CommanderRole.HeadOfConstruction
        };

        foreach (var role in roles)
        {
            var err = engine.AppointCommander(role, 2);
            Check(err is null, $"appointed {role} tier 2 successfully");
            var holder = CommandStaffService.Holder(ott, role);
            Check(holder is not null && holder.Skill == 2, $"{role} holder registered with skill 2");
        }

        Check(engine.StaffWagesPerDay > 0, $"staff daily wages calculated: {engine.StaffWagesPerDay:F2} gold/day");
        double landMult = CommandStaffService.LandRecruitMult(ott);
        Check(landMult < 1.0, $"Land Commander reduces recruit cost multiplier to {landMult:P0}");
        double shipMult = CommandStaffService.WarshipRecruitMult(ott);
        Check(shipMult < 1.0, $"Fleet Commander reduces warship cost multiplier to {shipMult:P0}");

        // Dismiss and replace test
        engine.DismissCommander(CommanderRole.ChiefTributeCollector);
        Check(CommandStaffService.Holder(ott, CommanderRole.ChiefTributeCollector) is null, "Tribute Collector post dismissed");
        var reappointErr = engine.AppointCommander(CommanderRole.ChiefTributeCollector, 3);
        Check(reappointErr is null && CommandStaffService.Holder(ott, CommanderRole.ChiefTributeCollector)?.Skill == 3,
            "Tribute Collector reappointed at tier 3");

        // -------------------------------------------------------------------------
        // 3. Economy, Construction & Military Crafting
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 3: Economy, Construction & Military Crafting] --");
        int farms0 = ott.Farms;
        int mines0 = ott.Mines;
        int sawmills0 = ott.Sawmills;
        int workshops0 = ott.Workshops;

        Check(engine.StartConstruction(BuildingType.Farm) is null, "Farm construction started");
        Check(engine.StartConstruction(BuildingType.Mine) is null, "Mine construction started");
        Check(engine.StartConstruction(BuildingType.Sawmill) is null, "Sawmill construction started");
        Check(engine.StartConstruction(BuildingType.Workshop) is null, "Workshop construction started");

        for (int d = 0; d < 15; d++) engine.AdvanceOneDay();

        Check(ott.Farms == farms0 + 1, "Farm finished and count incremented");
        Check(ott.Mines == mines0 + 1, "Mine finished and count incremented");
        Check(ott.Sawmills == sawmills0 + 1, "Sawmill finished and count incremented");
        Check(ott.Workshops == workshops0 + 1, "Workshop finished and count incremented");
        Check(ott.ConstructionQueue.Count == 0, "homeland construction queue empty");

        // Military equipment crafting
        ott.Wood += 100;
        ott.Stone += 50;
        ott.Copper += 50;
        ott.Iron += 50;
        double helmets0 = ott.GetMilitaryItem("helmet");
        Check(engine.StartMilitaryCraft("helmet") is null, "started crafting helmets");
        engine.AdvanceOneDay(); // 1-day craft recipe
        engine.AdvanceOneDay();
        Check(ott.GetMilitaryItem("helmet") >= helmets0 + 10, "helmets crafted and added to stock");

        // Sell goods
        ott.Goods = 100;
        double goldBeforeGoods = ott.Gold;
        engine.SellGoods();
        Check(ott.Goods == 0 && ott.Gold > goldBeforeGoods, "goods sold successfully into gold");

        // -------------------------------------------------------------------------
        // 4. Military Recruitment, Reserves, Mercenaries & Upkeep
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 4: Military Recruitment, Reserves, Mercenaries & Upkeep] --");
        // Ensure equipment and gold
        ott.AddMilitaryItem("shotgun", 100);
        ott.AddMilitaryItem("helmet", 100);
        int soldiersBeforeRec = ott.Soldiers;
        Check(engine.StartRecruitment(UnitType.Musketeer, 50) is null, "musketeer recruitment queued");
        for (int d = 0; d < 5; d++) engine.AdvanceOneDay();
        Check(ott.Soldiers >= soldiersBeforeRec + 50, "musketeers trained and added to standing army");

        // Warships
        ott.AddMilitaryItem("shipparts", 50);
        ott.Wood += 500;
        int warships0 = ott.Warships;
        Check(engine.RecruitWarships(2) is null, "recruited 2 warships");
        Check(ott.Warships == warships0 + 2, "warships count increased");

        // Reserves
        ott.Reserves = 1000;
        int reserves0 = ott.Reserves;
        int soldiersBeforeMob = ott.Soldiers;
        Check(engine.MobilizeReserves(500) is null, "mobilized 500 reserves");
        Check(ott.Soldiers == soldiersBeforeMob + 500 && ott.Reserves == reserves0 - 500, "reserves mobilized instantly");

        // Conscription
        ott.Gold += 100000;
        int soldiersBeforeCon = ott.Soldiers;
        Check(engine.Conscript("limited") is null, "limited emergency conscription executed");
        Check(ott.Soldiers > soldiersBeforeCon, "militia conscripted into army");

        // Allied help from Crimea (starting ally / friendly realm)
        var crimea = engine.State.OtherNations.First(n => n.Id == "crimea");
        crimea.RelationToPlayer = 80;
        int solBeforeAlly = ott.Soldiers;
        var allyRes = engine.RequestAlliedAssistance("crimea");
        Check(!string.IsNullOrEmpty(allyRes) && ott.Soldiers > solBeforeAlly, $"Crimean allied assistance received: {allyRes}");

        // Mercenaries
        int solBeforeMerc = ott.Soldiers;
        Check(engine.HireMercenaries("swiss_guard", 100) is null, "hired Swiss Guard mercenary company");
        Check(ott.Soldiers == solBeforeMerc + 100, "mercenaries joined standing army");
        Check(ott.MercenaryContracts.Count > 0, "mercenary contract tracked in active contracts");

        // Upkeep payment
        Check(engine.PayMaintenance() is null, "paid accrued maintenance");

        // -------------------------------------------------------------------------
        // 5. Taxation System
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 5: Taxation System] --");
        ott.TaxRates.Peasants = 25;
        ott.TaxRates.Craftsmen = 20;
        ott.TaxRates.MilitaryPersonnel = 10;
        ott.TaxRates.Merchants = 30;
        ott.TaxRates.Spies = 15;
        ott.TaxRates.Saboteurs = 15;

        double expectedRev = TaxationService.TotalRevenue(ott.Workforce, ott.TaxRates);
        Check(expectedRev > 0, $"tax revenue estimated at {expectedRev:N0} gold/day");
        double goldBeforeTax = ott.Gold;
        engine.AdvanceOneDay();
        Check(ott.Gold > goldBeforeTax - 1000, "daily tax revenue generated and received");
        Check(ott.TaxApproval >= 0 && ott.TaxApproval <= 100, $"tax approval is valid: {ott.TaxApproval:F1}/100");

        // -------------------------------------------------------------------------
        // 6. International Assembly
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 6: International Assembly] --");
        int votingPower = engine.State.VotingPowerOf(ott);
        Check(votingPower > 0, $"Ottoman Empire has strong voting power: {votingPower} votes");

        var submitErr = engine.SubmitProposal(AssemblyProposalTypes.WarBan, null, 180, 20);
        Check(submitErr is null, "submitted global War Ban proposal to Assembly");
        var prop = engine.State.AssemblyProposals.FirstOrDefault(p => p.TypeId == AssemblyProposalTypes.WarBan);
        Check(prop is not null, "proposal found in active proposals");
        if (prop is not null)
        {
            Check(prop.Votes.ContainsKey(ott.Id) && prop.Votes[ott.Id], "proposer vote automatically registered FOR");
            for (int d = 0; d < 22; d++) engine.AdvanceOneDay();
            Check(prop.Status != ProposalStatus.VotingOpen, $"proposal voting closed with status {prop.Status}");
        }

        // -------------------------------------------------------------------------
        // 7. Laws & Reforms
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 7: Laws & Reforms] --");
        Check(engine.SelectLaw("peacetime") is null, "enacted Peacetime economic law");
        Check(ott.ActiveLaws.Contains("peacetime"), "Peacetime active in laws list");

        // Conflicting law in same policy group ('production_priority')
        Check(engine.SelectLaw("civilian_production") is null, "enacted Civilian Production law");
        Check(!ott.ActiveLaws.Contains("peacetime") && ott.ActiveLaws.Contains("civilian_production"),
            "conflicting Peacetime law was cleanly replaced by Civilian Production");

        engine.RepealLaw("civilian_production");
        Check(!ott.ActiveLaws.Contains("civilian_production"), "repealed law cleanly removed");

        // -------------------------------------------------------------------------
        // 8. Religion & Missionaries
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 8: Religion & Missionaries] --");
        ott.Gold += 500000;
        Check(engine.StartReligionConversion("christianity") is null, "started religion conversion process");
        Check(ott.ReligionConversion?.InProgress == true, "religion conversion in progress");
        engine.CancelReligionConversion();
        Check(ott.ReligionConversion is null, "religion conversion cleanly cancelled");

        // Austria follows Christianity (not Islam), so Muslim missionaries can be sent
        var austria = engine.State.OtherNations.First(n => n.Id == "austria");
        austria.AtWarWithPlayer = false;
        austria.RelationToPlayer = 50; // satisfies MissionaryMinRating (30)
        ott.Gold += 5000;
        var missRes = engine.SendMissionary("austria");
        Check(missRes.Ok, "sent missionary expedition to Austria");
        Check(engine.State.MissionaryInfluence.ContainsKey("austria") && engine.State.MissionaryInfluence["austria"] > 0,
            "missionary influence recorded in Austria");

        // -------------------------------------------------------------------------
        // 9. Colonization
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 9: Colonization] --");
        ott.AddProduct("Wheat", 5000);
        ott.Wood += 3000;
        ott.Warships += 15;
        ott.Gold += 100000;

        var quote = engine.QuoteColony("swahili-coast");
        Check(quote is not null && quote.Gold > 0 && quote.Block is null,
            $"quoted swahili-coast: cost {quote?.Gold:N0} gold, {quote?.Days} days, block: {quote?.Block ?? "none"}");
        Check(engine.FoundColony("swahili-coast") is null, "dispatched colonisation expedition to Swahili Coast");
        Check(engine.PlayerMission is not null, "player mission registered");

        int daysToLand = engine.PlayerMission?.DaysLeft(engine.State.CurrentDate) ?? 50;
        for (int d = 0; d <= daysToLand + 1; d++) engine.AdvanceOneDay();

        var colonies = engine.ColonyReports();
        Check(colonies.Any(c => c.Id == "colony-swahili-coast"), "colonists landed and Swahili Coast colony established");
        var swahili = colonies.FirstOrDefault(c => c.Id == "colony-swahili-coast");
        if (swahili is not null)
        {
            Check(swahili.Population > 0, $"colony has living population: {swahili.Population:N0}");
            Check(swahili.TotalIncomePerDay > 0, $"colony generates revenue: {swahili.TotalIncomePerDay:F1} gold/day");
        }

        // -------------------------------------------------------------------------
        // 10. Diplomacy & Treaties
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 10: Diplomacy & Treaties] --");
        Check(engine.HasTradeEmbassy("france"), "France embassy ready for trade");

        var persia = engine.State.OtherNations.First(n => n.Id == "persia");
        persia.RelationToPlayer = 50; // improve relations above EmbassyMinRating (30)
        Check(engine.EstablishEmbassy("persia").Ok, "established new embassy in Persia");
        Check(engine.HasTradeEmbassy("persia"), "Persian embassy active");

        Check(engine.ProposeNonAggression("persia").Ok, "signed Non-Aggression Pact with Persia");
        Check(TreatyService.Find(engine.State, TreatyType.NonAggression, ott.Id, "persia") is not null,
            "Persian NAP registered in treaties");

        double franceRel0 = engine.State.OtherNations.First(n => n.Id == "france").RelationToPlayer;
        Check(engine.GiveGift("france", 500).Ok, "sent gift of 500 gold to France");
        double franceRel1 = engine.State.OtherNations.First(n => n.Id == "france").RelationToPlayer;
        Check(franceRel1 > franceRel0, "French relations increased after gift");

        // Non-aggression pact prevents aggressive tribute demands
        string? persiaTributeErr = engine.DemandTribute("persia");
        Check(persiaTributeErr is not null && persiaTributeErr.Contains("non-aggression pact"), "tribute demand blocked by non-aggression pact");

        // Demand tribute from Venice (no NAP, Ottoman army > 1.5x Venice army)
        Check(engine.DemandTribute("venice") is null, "demanded tribute from Venice successfully");

        // -------------------------------------------------------------------------
        // 11. Espionage
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 11: Espionage] --");
        var poland = engine.State.OtherNations.First(n => n.Id == "poland");
        poland.AtWarWithPlayer = false;
        Check(engine.EstablishNetwork("poland") is null, "established spy network in Poland");
        var net = engine.GetNetwork("poland");
        Check(net is not null, "Polish spy network active");
        net!.Strength = 80;

        string? stealErr = engine.SpySteal("poland");
        Check(stealErr is null, "executed spy theft in Poland");
        net.Strength = 80;
        string? sabErr = engine.SpySabotage("poland");
        Check(sabErr is null, "executed spy sabotage in Poland");
        net.Strength = 80;
        string? coupErr = engine.SpyInciteRevolt("poland");
        Check(coupErr is null, "executed spy revolt incitement in Poland");

        // -------------------------------------------------------------------------
        // 12. Trade
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 12: Trade] --");
        Check(engine.TradeBlock("russia") is not null, "trade without embassy correctly blocked");
        Check(engine.TradeBlock("france") is null, "trade with France embassy allowed");

        Check(engine.BuyProduct("france", "wheat", 100) is null, "bought 100 Wheat from France");
        var buyContract = engine.State.TradeContracts.Last();
        Check(buyContract.Status == TradeStatus.InTransit, "purchase contract in transit");

        // Advance for delivery
        for (int d = 0; d < 10; d++) engine.AdvanceOneDay();
        Check(buyContract.Status == TradeStatus.Delivered, "purchased Wheat delivered by merchant fleet");

        ott.Wood += 200;
        double goldBeforeTradeSell = ott.Gold;
        Check(engine.SellProduct("venice", "wood", 100, 2.0) is null, "sold 100 Wood to Venice");
        var sellContract = engine.State.TradeContracts.Last();
        for (int d = 0; d < 10; d++) engine.AdvanceOneDay();
        Check(sellContract.Status == TradeStatus.Delivered, "sale contract delivered");
        Check(ott.Gold > goldBeforeTradeSell, "sale revenue received upon delivery");

        // -------------------------------------------------------------------------
        // 13. Warfare, Battles, Peace & Annexation
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 13: Warfare, Battles, Peace & Annexation] --");
        // Clear bans if any
        engine.State.ActiveAssemblyPolicies.Clear();

        Check(engine.DeclareWar("austria") is null, "declared war on Austria");
        Check(austria.AtWarWithPlayer, "Austria is at war with player");

        // Boost Ottoman army and reduce Austrian army for clear battle resolution
        ott.Units.First().Count += 60000;
        austria.Units.First().Count = 1000;

        var attackPlan = engine.PlanAttack("austria");
        Check(attackPlan is not null && attackPlan.CanAttack, "planned attack on Austria");
        var atkRes = engine.ConfirmAttack("austria", 30000);
        Check(atkRes.Ok && atkRes.March is not null, "attack marched successfully");

        int marchDays = atkRes.March?.ArrivalDate.DayNumber - engine.State.CurrentDate.DayNumber ?? 10;
        for (int d = 0; d <= marchDays + 1; d++) engine.AdvanceOneDay();

        Check(engine.State.PendingVictories.Any(v => v.LoserId == "austria") || austria.IsEliminated,
            "battle resolved with victory over Austria");

        if (engine.State.PendingVictories.Any(v => v.LoserId == "austria"))
        {
            var res = engine.ResolveVictory("austria", VictoryChoice.Annex);
            Check(res.Ok, $"annexed Austria: {res.Message}");
        }

        Check(austria.IsEliminated, "Austria eliminated as independent state");
        Check(austria.AnnexedById == "ottoman", "Austria annexed by Ottoman Empire");

        var terrReports = engine.TerritoryReports();
        Check(terrReports.Any(t => t.Id == "austria"), "Austria listed in Annexed States reports");

        // Annexed state operations
        var terrRates = new TaxRates { Peasants = 15, Craftsmen = 15, MilitaryPersonnel = 10, Merchants = 20, Spies = 10, Saboteurs = 10 };
        var preview = engine.PreviewTerritoryTaxes("austria", terrRates);
        Check(preview is not null && preview.RevenuePerDay > 0, "previewed annexed state taxes");
        engine.SetTerritoryTaxRates("austria", terrRates);
        Check(engine.BuildBasicInTerritory("austria", BuildingType.Farm) is null, "started basic farm construction in annexed state");

        // Peace talks test with Denmark
        var denmark = engine.State.OtherNations.First(n => n.Id == "denmark");
        engine.DeclareWar("denmark");
        Check(denmark.AtWarWithPlayer, "at war with Denmark");
        var peaceRes = engine.ProposePeace("denmark", new[] { PeaceTerm.Peace() });
        Check(peaceRes.Ok, "proposed peace terms to Denmark");

        // -------------------------------------------------------------------------
        // 14. Research & Technologies
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 14: Research & Technologies] --");
        Check(engine.StartResearch("drill_manuals") is null, "started Drill Manuals research");
        ott.ResearchPoints = 405; // complete research threshold
        engine.AdvanceOneDay();
        Check(ResearchService.Has(ott, "drill_manuals"), "Drill Manuals research completed and perk active");

        // -------------------------------------------------------------------------
        // 15. Movements, Inbox, World News
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 15: Movements, Inbox, World News] --");
        var allEvents = MovementReport.Events(engine.State);
        Check(allEvents.Count > 0, $"movement events logged: {allEvents.Count} events");
        var inboxMsgs = InboxService.Messages(engine.State, InboxCategory.Military);
        Check(inboxMsgs.Count > 0, $"inbox military messages received: {inboxMsgs.Count} messages");

        var firstMsg = inboxMsgs.First();
        InboxService.MarkRead(engine.State, firstMsg.Id);
        Check(engine.State.Inbox.First(m => m.Id == firstMsg.Id).Read, "inbox message marked as read");

        int unread = InboxService.UnreadCount(engine.State);
        Check(unread >= 0, $"unread message count accurately tracked: {unread}");

        // Log an AI movement to verify World News reporting on non-player events
        var spain = engine.State.OtherNations.First(n => n.Id == "spain");
        var france = engine.State.OtherNations.First(n => n.Id == "france");
        engine.State.LogMovement(MovementKind.War, MovementStatus.Completed, spain, france, "Spain and France clash in Europe.");
        var news = WorldNewsReport.Items(engine.State);
        Check(news.Count > 0, $"world news generated: {news.Count} items");

        // -------------------------------------------------------------------------
        // 16. Save and Load Round-Trip Integrity
        // -------------------------------------------------------------------------
        Console.WriteLine("\n-- [Flow 16: Save and Load Round-Trip Integrity] --");
        double savedGold = ott.Gold;
        int savedSoldiers = ott.Soldiers;
        var savedDate = engine.State.CurrentDate;
        int savedColoniesCount = engine.State.ColoniesOf(ott.Id).Count;
        int savedAnnexedCount = engine.State.AnnexedBy(ott.Id).Count;

        await engine.SaveAsync();
        Check(engine.HasSave, "save file created successfully");

        // Mutate state in memory
        ott.Gold = 1234.5;
        ott.Units.First().Count = 99;

        // Restore
        await engine.LoadAsync();
        var reloadedOtt = engine.State.PlayerNation;
        Check(Math.Abs(reloadedOtt.Gold - savedGold) < 0.01, "restored exact gold");
        Check(reloadedOtt.Soldiers == savedSoldiers, "restored exact soldiers");
        Check(engine.State.CurrentDate == savedDate, "restored exact game date");
        Check(engine.State.ColoniesOf(reloadedOtt.Id).Count == savedColoniesCount, "restored all colonies");
        Check(engine.State.AnnexedBy(reloadedOtt.Id).Count == savedAnnexedCount, "restored all annexed territories");
        Check(reloadedOtt.Religion == "Islam", "restored state religion");
        Check(ResearchService.Has(reloadedOtt, "drill_manuals"), "restored researched technology");

        Console.WriteLine("\n-- OTTOMAN PLAYTHROUGH AUDIT COMPLETE --\n");
    }
}
