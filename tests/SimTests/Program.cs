using System.Text.Json;
using System.Text.Json.Nodes;
using EmpireSim.Core.Models;
using EmpireSim.Core.Services;

// Headless verification of the game engine: no UI, no device needed.
// Run: dotnet run --project tests/SimTests

int failures = 0;
void Check(bool condition, string name)
{
    Console.WriteLine((condition ? "  PASS " : "  FAIL ") + name);
    if (!condition) failures++;
}

var saveFolder = Path.Combine(Path.GetTempPath(), "empiresim-test");
if (Directory.Exists(saveFolder)) Directory.Delete(saveFolder, recursive: true);

Console.WriteLine("== 1. New game starts 01-01-1600 ==");
using (var engine = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    Check(engine.State.CurrentDate == new DateOnly(1600, 1, 1), "starts 01-01-1600");
    Check(engine.State.PlayerNation.Name == "Ottoman Empire", "player nation set");
    Check(engine.State.PlayerNation.Territory.Count >= 1, "player nation has territory shapes");
    Check(engine.Clock.Speed == GameSpeed.Paused, "clock paused initially");

    Console.WriteLine("== 2. 200-day simulation (covers the 6-month payday on 01-07-1600) ==");
    var n0 = engine.State.PlayerNation;
    long pop0 = n0.Population; double tre0 = n0.Gold; int sol0 = n0.Soldiers;
    for (int i = 0; i < 200; i++) engine.AdvanceOneDay();
    var n = engine.State.PlayerNation;
    Console.WriteLine($"  day 0:   date=01-01-1600 pop={pop0:N0} treasury={tre0:N0} soldiers={sol0:N0}");
    Console.WriteLine($"  day 200: date={engine.State.CurrentDate:dd-MM-yyyy} pop={n.Population:N0} treasury={n.Gold:N0} wheat={n.GetGood("Wheat"):N0} soldiers={n.Soldiers:N0}");
    Check(engine.State.CurrentDate == new DateOnly(1600, 7, 19), "date advanced to 19-07-1600");
    Check(n.Gold >= 0 && n.GetGood("Wheat") >= 0 && n.Population > 0, "no negative stocks, nation survives");
    Check(double.IsFinite(n.Gold) && double.IsFinite(n.GetGood("Wheat")), "stocks are finite numbers");
    Check(n.Population > pop0, "population grows with food surplus");
    Check(engine.State.EventLog.Any(e => e.Contains("paid army maintenance")), "first payday was paid and logged");

    Console.WriteLine("== 3. Save / load round-trip ==");
    double savedGold = engine.State.PlayerNation.Gold;
    await engine.SaveAsync();
    Check(engine.HasSave, "save file exists");
    engine.State.PlayerNation.Gold = 1; // mutate...
    await engine.LoadAsync();             // ...then restore
    Check(Math.Abs(engine.State.PlayerNation.Gold - savedGold) < 0.01, "load restores saved gold");

    Console.WriteLine("== 4. Missed payday -> grace warning -> desertion ==");
    engine.NewGame();
    var p = engine.State.PlayerNation;
    int soldiersBefore = p.Soldiers;
    bool sawWarning = false;
    // Keep the nation broke all the way past the payday (day 182) + grace period.
    // Zero tax rates so no revenue interferes with the broke simulation.
    p.TaxRates.Peasants = 0; p.TaxRates.Craftsmen = 0; p.TaxRates.MilitaryPersonnel = 0;
    p.TaxRates.Merchants = 0; p.TaxRates.Spies = 0; p.TaxRates.Saboteurs = 0;
    for (int i = 0; i < 196; i++)
    {
        p.Gold = 0;
        engine.AdvanceOneDay();
        if (engine.State.ActiveWarnings.Any(w => w.Contains("MAINTENANCE DUE"))) sawWarning = true;
    }
    Console.WriteLine($"  soldiers before={soldiersBefore:N0} after={p.Soldiers:N0}");
    Check(sawWarning, "maintenance-due warning raised during grace period");
    Check(p.Soldiers < soldiersBefore, "unpaid soldiers desert after grace expiry");
}

Console.WriteLine("== 5. Map data sanity ==");
using (var engine2 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var allTerr = engine2.State.AllNations().SelectMany(n => n.Territory).ToList();
    Check(allTerr.Count >= 70 && engine2.State.AllNations().All(n => n.Territory.Count >= 1), "every one of the 41 nations owns land on the map (70+ polygons: islands and exclaves are polygons of their own)");
    Check(engine2.State.AllNations().Count() == 41, "41 nations on the 1600 map");
    Check(allTerr.All(t => t.Count >= 3), "every territory polygon has >= 3 points");
    Check(allTerr.All(t => t.All(pt => pt.X >= 0 && pt.X <= 2200 && pt.Y >= 0 && pt.Y <= 1150)),
        "all polygon points inside the 2200x1150 viewBox");
    Check(engine2.State.AllNations().All(n => n.MapX > 0 && n.MapY > 0), "every nation has a map anchor");
    Check(engine2.State.AllNations().All(n => !string.IsNullOrEmpty(n.CapitalName)), "every nation has a capital");
    Check(engine2.State.AllNations().All(n => n.PinX > 0 && n.PinX < 100 && n.PinY > 0 && n.PinY < 100),
        "every nation has a capital pin on the parchment map");
    Check(engine2.State.NeutralRegions.Count > 0, "neutral territories drawn on the map");
}

Console.WriteLine("== 6. Economy: construction, chains, trade ==");
using (var engine3 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n3 = engine3.State.PlayerNation;
    int farmsBefore = n3.Farms;
    double goldBefore = n3.Gold;

    // 6a. Build a farm: cost deducted upfront, completes after 5 days.
    string? err = engine3.StartConstruction(BuildingType.Farm);
    Check(err is null, "farm construction accepted");
    Check(Math.Abs(n3.Gold - (goldBefore - 1)) < 0.01, "farm cost deducted upfront");
    Check(n3.ConstructionQueue.Count == 1, "project queued");
    for (int i = 0; i < 5; i++) engine3.AdvanceOneDay();
    Check(n3.Farms == farmsBefore + 1, "farm completed after 5 days");
    Check(n3.ConstructionQueue.Count == 0, "queue empty after completion");
    Check(engine3.State.EventLog.Any(e => e.Contains("Farm completed")), "completion logged");

    // 6b. Unaffordable build is rejected.
    n3.Gold = 0;
    string? err2 = engine3.StartConstruction(BuildingType.Mine);
    Check(err2 is not null, "broke build rejected with error");
    Check(n3.ConstructionQueue.Count == 0, "nothing queued when broke");

    // 6c. Workshop chain: wood + iron -> goods.
    n3.Gold = 50; n3.Wood = 50; n3.Iron = 30;
    string? err3 = engine3.StartConstruction(BuildingType.Workshop);
    Check(err3 is null, "workshop construction accepted (had wood+iron)");
    for (int i = 0; i < 12; i++) engine3.AdvanceOneDay();
    Check(n3.Workshops == 1, "workshop completed after 12 days");
    // Isolate the chain: remove mines/sawmills so stocks move only via the workshop.
    n3.Mines = 0; n3.Sawmills = 0; n3.ProductionBuildings.Clear();
    n3.Population = 0; // isolate from population consumption
    n3.Wood = 50; n3.Iron = 30;
    double woodBefore = n3.Wood, ironBefore = n3.Iron, goodsBefore = n3.Goods;
    for (int i = 0; i < 3; i++) engine3.AdvanceOneDay();
    Check(n3.Goods > goodsBefore, "workshop produced goods");
    Check(Math.Abs(n3.Wood - (woodBefore - 6)) < 0.01 && Math.Abs(n3.Iron - (ironBefore - 3)) < 0.01,
        "workshop consumed 2 wood + 1 iron per day");

    // 6d. Selling goods converts to gold.
    n3.Goods = 10;
    double tBefore = n3.Gold;
    engine3.SellGoods();
    Check(n3.Goods == 0, "goods stockpile emptied by sale");
    Check(Math.Abs(n3.Gold - (tBefore + 150)) < 0.01, "sold 10 goods for 150 Gold");

    // 6e. 60-day economy run: stocks stay sane.
    for (int i = 0; i < 60; i++) engine3.AdvanceOneDay();
    Check(n3.Wood >= 0 && n3.Iron >= 0 && n3.Goods >= 0 && n3.Gold >= 0, "no negative stocks after 60 days");
}

Console.WriteLine("== 7. Military: recruitment, commanders, maintenance ==");
using (var engine4 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n4 = engine4.State.PlayerNation;

    // 7a. Recruit musketeers: gold deducted, stack grows.
    int muskBefore = n4.Units.First(u => u.Type == UnitType.Musketeer).Count;
    double goldBefore = n4.Gold;
    string? rerr = engine4.Recruit(UnitType.Musketeer, 100);
    Check(rerr is null, "recruit 100 musketeers accepted");
    Check(n4.Units.First(u => u.Type == UnitType.Musketeer).Count == muskBefore + 100, "musketeer stack grew by 100");
    Check(Math.Abs(n4.Gold - (goldBefore - 20)) < 0.01, "recruit cost deducted");

    // 7b. Cannon costs iron too; broke recruit rejected.
    n4.Iron = 100;
    string? rerr2 = engine4.Recruit(UnitType.Cannon, 10);
    Check(rerr2 is null && Math.Abs(n4.Iron - 50) < 0.01, "10 cannons consumed 50 iron");
    n4.Gold = 0;
    string? rerr3 = engine4.Recruit(UnitType.Musketeer, 100);
    Check(rerr3 is not null, "broke recruit rejected");

    // 7c. Upkeep accrues per unit type (fresh engine, 10 days, no commanders).
    using (var eng = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
    {
        for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
        // 4400*0.0003 + 2400*0.00025 + 1040*0.0006 + 160*0.0015 (land) + 25*0.005 (naval), per day x10
        double expected = (1.32 + 0.6 + 0.624 + 0.24 + 0.125) * 10;
        Check(Math.Abs(eng.State.PlayerNation.UpkeepAccrued - expected) < 0.5, $"upkeep accrued ~{expected:N2} over 10 days");
    }

    // 7d. PayMaintenance resets the cycle.
    for (int i = 0; i < 30; i++) engine4.AdvanceOneDay();
    n4.Gold = 1000;
    double accrued = n4.UpkeepAccrued;
    string? perr = engine4.PayMaintenance();
    Check(perr is null, "early maintenance payment accepted");
    Check(n4.UpkeepAccrued == 0, "accrual reset after payment");
    Check(n4.NextPayday == engine4.State.CurrentDate.AddDays(180), "payday pushed 180 days out");
    Check(Math.Abs(n4.Gold - (1000 - accrued)) < 0.01, "treasury reduced by accrued amount");

    // 7e. Hire land commander: cheaper upkeep afterwards.
    n4.Gold = 1000;
    string? herr = engine4.HireCommander(CommanderRole.LandCommander);
    Check(herr is null && n4.HasCommander(CommanderRole.LandCommander), "land commander hired");
    Check(Math.Abs(n4.Gold - (1000 - 12)) < 0.01, "hire cost deducted");
    n4.UpkeepAccrued = 0;
    for (int i = 0; i < 10; i++) engine4.AdvanceOneDay();
    double withCommander = n4.UpkeepAccrued; // (2.784*0.85 + 0.125) * 10 + wages 0.02*10
    Check(withCommander < 29.09, $"commander reduces upkeep ({withCommander:N2} < 29.09)");
    engine4.DismissCommander(CommanderRole.LandCommander);
    Check(!n4.HasCommander(CommanderRole.LandCommander), "commander dismissed");

    // 7f. Warships cost wood + iron.
    n4.Gold = 1000; n4.Wood = 500; n4.Iron = 200;
    int shipsBefore = n4.Warships;
    string? werr = engine4.RecruitWarships(10);
    Check(werr is null && n4.Warships == shipsBefore + 10, "10 warships launched");
    Check(Math.Abs(n4.Wood - 300) < 0.01 && Math.Abs(n4.Iron - 100) < 0.01, "warships consumed 200 wood + 100 iron");

    // 7g. Desertion drains stacks consistently.
    engine4.NewGame();
    var p4 = engine4.State.PlayerNation;
    int totalBefore = p4.Soldiers;
    p4.TaxRates.Peasants = 0; p4.TaxRates.Craftsmen = 0; p4.TaxRates.MilitaryPersonnel = 0;
    p4.TaxRates.Merchants = 0; p4.TaxRates.Spies = 0; p4.TaxRates.Saboteurs = 0;
    for (int i = 0; i < 196; i++) { p4.Gold = 0; engine4.AdvanceOneDay(); }
    Check(p4.Soldiers < totalBefore, "unpaid army shrinks");
    Check(p4.Units.Sum(u => u.Count) == p4.Soldiers, "stacks sum to Soldiers total");
    Check(p4.Units.All(u => u.Count > 0), "no empty stacks left behind");
}

Console.WriteLine("== 8. Diplomacy & espionage ==");
using (var engine5 = new GameEngine(new SimulationService(seed: 42), new SaveService(saveFolder)))
{
    var player5 = engine5.State.PlayerNation;
    var persia = engine5.State.OtherNations.First(n => n.Name == "Iran");
    var mughal = engine5.State.OtherNations.First(n => n.Name == "Mughal Empire");
    Neutral(engine5);   // (this section tests the plain mechanics from neutral relations, not the 1600 start)

    // 8a. Gift improves relations, costs gold.
    double g0 = player5.Gold;
    Check(engine5.SendGift(persia.Id) is null, "gift accepted");
    Check(Math.Abs(persia.RelationToPlayer - 10) < 0.01, "gift +10 relations");
    Check(Math.Abs(player5.Gold - (g0 - 5)) < 0.01, "gift cost deducted");

    // 8b. Trade pact pays daily income; war breaks it.
    Check(engine5.SignTradePact(persia.Id) is null, "trade pact signed");
    double t0 = player5.Gold;
    engine5.AdvanceOneDay();
    Check(player5.Gold > t0, "pact pays daily income");
    Check(engine5.DeclareWar(persia.Id) is null, "war declared");
    Check(persia.AtWarWithPlayer && !persia.HasTradePactWithPlayer, "war breaks the pact");
    Check(persia.RelationToPlayer == -100, "war sets relations to -100");

    // 8c. Peace is negotiated: terms are proposed, and nothing changes until the other side accepts.
    player5.Gold = 100_000;
    double goldBeforePeace = player5.Gold;
    Check(engine5.ProposePeace(persia.Id, new[] { PeaceTerm.Peace() }).Ok, "peace terms proposed");
    Check(persia.AtWarWithPlayer && player5.Gold == goldBeforePeace && persia.RelationToPlayer == -100, "proposing changes nothing: the war goes on and nothing is paid");
    var peaceOffer5 = engine5.PendingPeaceOffer(persia.Id)!;
    Check(PeaceService.Accept(engine5.State, peaceOffer5).Ok && !persia.AtWarWithPlayer, "once accepted, the terms end the war");

    // 8d. Tribute: paid when strong, refused when weak.
    player5.Gold = 100_000; engine5.Recruit(UnitType.Musketeer, 1000); // ~7000 soldiers vs Persia's 4500
    double pt0 = player5.Gold, et0 = persia.Gold;
    Check(engine5.DemandTribute(persia.Id) is null, "tribute demanded");
    Check(player5.Gold > pt0 && persia.Gold < et0, "tribute transferred to player");
    double mrel = mughal.RelationToPlayer;
    engine5.DemandTribute(mughal.Id); // 7000 vs 7000 -> refused
    Check(mughal.RelationToPlayer < mrel, "refused demand hurts relations");

    // 8e. Espionage: establish, grow, steal.
    Check(engine5.EstablishNetwork(persia.Id) is null, "spy network established");
    var net = engine5.GetNetwork(persia.Id)!;
    Check(net.Strength == 20, "network starts at strength 20");
    for (int i = 0; i < 15; i++) engine5.AdvanceOneDay();
    Check(net.Strength == 35, "network grows +1/day");
    double pg0 = player5.Gold, eg1 = persia.Gold;
    Check(engine5.SpySteal(persia.Id) is null, "steal executed");
    double stolen = player5.Gold - pg0;
    Check(stolen >= eg1 * 0.049 && stolen <= eg1 * 0.151, "stole 5-15% of target treasury");
    Check(net.Strength == 15 || net.Strength == 7, "steal spent 20 strength (halved if caught)");
    if (net.Strength == 7)
        Check(engine5.State.ActiveWarnings.Any(w => w.Contains("caught")), "discovery raises warning");

    // 8f. Sabotage destroys a building.
    for (int i = 0; i < 35; i++) engine5.AdvanceOneDay();
    int buildingsBefore = persia.Farms + persia.Mines + persia.Sawmills + persia.Workshops;
    Check(engine5.SpySabotage(persia.Id) is null, "sabotage executed");
    int buildingsAfter = persia.Farms + persia.Mines + persia.Sawmills + persia.Workshops;
    Check(buildingsAfter == buildingsBefore - 1, "sabotage destroyed one building");

    // 8g. Incite revolt causes desertion.
    for (int i = 0; i < 60; i++) engine5.AdvanceOneDay();
    int soldiersBefore = persia.Soldiers;
    Check(engine5.SpyInciteRevolt(persia.Id) is null, "incite executed");
    Check(persia.Soldiers == soldiersBefore - (int)(soldiersBefore * 0.05), "5% of target army deserted");
}

// 8h. A furious AI declares war on its own.
using (var engine6 = new GameEngine(new SimulationService(seed: 7), new SaveService(saveFolder)))
{
    foreach (var n in engine6.State.OtherNations) n.RelationToPlayer = -100;
    bool warDeclared = false;
    for (int i = 0; i < 300 && !warDeclared; i++)
    {
        engine6.State.PlayerNation.Gold = 1_000_000; // stay solvent, isolate the war logic
        engine6.AdvanceOneDay();
        warDeclared = engine6.State.OtherNations.Any(n => n.AtWarWithPlayer);
    }
    Check(warDeclared, "furious AI declares war by itself");
    Check(engine6.State.ActiveWarnings.Any(w => w.Contains("DECLARED WAR")), "war declaration warns the player");
}

Console.WriteLine("== 9. Gold-only currency ==");
using (var engine7 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n7 = engine7.State.PlayerNation;
    Check(n7.Gold > 0, "starting gold is positive");

    // Pay and earn gold.
    double before = n7.Gold;
    Check(n7.PayGold(10), "pay 10 gold succeeds");
    Check(Math.Abs(n7.Gold - (before - 10)) < 0.01, "gold deducted");
    n7.EarnGold(5);
    Check(Math.Abs(n7.Gold - (before - 5)) < 0.01, "gold earned");

    // Cannot overpay.
    Check(!n7.PayGold(n7.Gold + 1000), "overpay fails");

    // Display forms.
    Check(Currency.Format(5230) == "5,230 Gold", "wealth formats as Gold");
    Check(Currency.Format(99) == "99 Gold", "small amounts show as Gold");
    Check(Currency.Cost(1200) == "1,200 Gold", "costs show as Gold");
}

Console.WriteLine("== 10. Warfare: marches, battles, whole-country annexation ==");
using (var engine8 = new GameEngine(new SimulationService(seed: 11), new SaveService(saveFolder)))
{
    var player8 = engine8.State.PlayerNation;
    var kazakh = engine8.State.OtherNations.First(n => n.Name == "Kazakh Khanate");

    var (ok0, _) = engine8.LaunchInvasion(kazakh.Id, 1000);
    Check(!ok0, "invasion refused without a declaration of war");

    engine8.DeclareWar(kazakh.Id);
    player8.Gold = 1_000_000;
    engine8.Recruit(UnitType.Musketeer, 20000);
    // Keep Kazakh strong enough to neither sue for peace nor invade back mid-march.
    kazakh.Units = UnitCatalog.SeedArmy(4000);
    long popBefore = player8.Population;
    int farmsBefore = player8.Farms;
    int soldiersBefore = player8.Soldiers;

    var (ok1, msg1) = engine8.LaunchInvasion(kazakh.Id, 20000);
    Check(ok1 && msg1.Contains("march"), "launch creates a march, not an instant battle");
    Check(engine8.State.MarchingArmies.Count == 1, "march is tracked in state");
    Check(player8.Soldiers == soldiersBefore - 20000, "marching force leaves the army at once");
    var march1 = engine8.State.MarchingArmies[0];
    Check(march1.TotalDays >= 1 && march1.ArrivalDate == march1.StartDate.AddDays(march1.TotalDays), "march takes travel days based on distance, with a start and an arrival date");
    Check(march1.Progress == 0, "march starts at 0% travelled");

    for (int i = 0; i < march1.TotalDays - 1; i++) engine8.AdvanceOneDay();
    Check(engine8.State.MarchingArmies.Count == 1, "march still under way before arrival");
    Check(engine8.State.MarchingArmies[0].Progress > 0, "march progress grows each day");
    engine8.AdvanceOneDay();
    Check(engine8.State.MarchingArmies.Count == 0, "march resolves on arrival");
    Check(engine8.State.EventLog.Any(e => e.Contains("Victory")), "overwhelming invasion wins");
    // Winning no longer annexes at once: the player chooses (annex / take resources / nothing).
    Check(!kazakh.IsEliminated && engine8.PendingVictory(kazakh.Id) is not null, "the victory waits for the player's decision");
    Check(engine8.ResolveVictory(kazakh.Id, VictoryChoice.Annex).Ok, "the player chooses to annex");
    Check(kazakh.IsEliminated, "defeated nation is annexed whole");
    Check(!kazakh.AtWarWithPlayer, "annexation ends the war");
    Check(engine8.State.NationsAnnexedByPlayer == 1, "annexation is counted");
    Check(kazakh.Population > 0 && engine8.State.TotalPopulation(player8) > popBefore, "the annexed country's people live on as a territory of the winner, counted once in its realm");
    Check(kazakh.Farms > 0 && kazakh.AnnexedById == player8.Id, "its farms stay in the territory, under the winner");

    // A 500-strong force loses to a garrison (fresh enemy: Nepal).
    var nepal = engine8.State.OtherNations.First(n => n.Id == "nepal");
    engine8.DeclareWar(nepal.Id);
    int soldiersMid = player8.Soldiers;
    var (ok2, msg2) = engine8.LaunchInvasion(nepal.Id, 500);
    Check(ok2 && msg2.Contains("march"), "a 500-strong force can also march");
    int days2 = engine8.State.MarchingArmies[0].TotalDays;
    for (int i = 0; i < days2; i++) engine8.AdvanceOneDay();
    Check(engine8.State.EventLog.Any(e => e.Contains("Defeat")), "a 500-strong force loses to the garrison");
    Check(player8.Soldiers < soldiersMid, "failed invasion costs soldiers");
    Check(!nepal.IsEliminated, "the defender survives a failed invasion");
}

Console.WriteLine("== 10b. Nation select + march recall ==");
using (var engine10b = new GameEngine(new SimulationService(seed: 5), new SaveService(saveFolder)))
{
    Check(!engine10b.CampaignChosen, "campaign starts unchosen (nation select shows)");
    engine10b.StartCampaign("persia");
    Check(engine10b.CampaignChosen, "choosing a nation starts the campaign");
    Check(engine10b.State.PlayerNation.Id == "persia", "player rules Persia");
    Check(engine10b.State.PlayerNation.IsPlayer, "chosen nation flagged as player");
    Check(engine10b.State.OtherNations.Any(n => n.Id == "ottoman" && !n.IsPlayer),
        "the Ottomans become an AI nation");
    engine10b.AbandonToMenu();
    Check(!engine10b.CampaignChosen, "abandon returns to nation select");
    engine10b.StartCampaign("mughal");
    Check(engine10b.State.PlayerNation.Id == "mughal", "a different nation can be chosen next");
}

using (var engine10c = new GameEngine(new SimulationService(seed: 7), new SaveService(saveFolder)))
{
    var playerC = engine10c.State.PlayerNation;
    var persiaC = engine10c.State.OtherNations.First(n => n.Name == "Iran");
    engine10c.DeclareWar(persiaC.Id);
    var (okC, _) = engine10c.LaunchInvasion(persiaC.Id, 1000);
    Check(okC, "invasion launches while at war");
    PeaceService.Accept(engine10c.State, PeaceService.Propose(engine10c.State, playerC.Id, persiaC, new[] { PeaceTerm.Peace() }).Offer!);   // peace is agreed while the army is on the road
    int daysC = engine10c.State.MarchingArmies[0].TotalDays;
    for (int i = 0; i < daysC; i++) engine10c.AdvanceOneDay();
    Check(engine10c.State.MarchingArmies.Count == 0, "recalled march leaves the list");
    Check(!persiaC.IsEliminated, "recalled march annexes nothing");
    Check(engine10c.State.EventLog.Any(e => e.Contains("called off")),
        "recalled march is reported in the log");
}
using (var engine9 = new GameEngine(new SimulationService(seed: 23), new SaveService(saveFolder)))
{
    var persia9 = engine9.State.OtherNations.First(n => n.Name == "Iran");
    persia9.Units = UnitCatalog.SeedArmy(30000);
    engine9.DeclareWar(persia9.Id);
    bool invaded = false;
    for (int i = 0; i < 400 && !invaded; i++)
    {
        engine9.State.PlayerNation.Gold = 1_000_000;
        engine9.AdvanceOneDay();
        invaded = engine9.State.ActiveWarnings.Any(w => w.Contains("marching on you"));
    }
    Check(invaded && engine9.State.MarchingArmies.Any(m => m.AttackerNationId == persia9.Id && m.TargetNationId == engine9.State.PlayerNation.Id), "a stronger AI sends an army marching on the player by itself (it does not invade instantly)");
}

Console.WriteLine("== 11. Laws & religion ==");
using (var engine10 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n10 = engine10.State.PlayerNation;
    n10.Gold = 100_000;

    Check(engine10.ToggleEdict(EdictType.WarTaxes) is null, "war taxes enacted");
    Check(n10.HasEdict(EdictType.WarTaxes), "edict is active");
    Check(Math.Abs(n10.TaxMult - 1.25) < 0.001, "war taxes: +25% tax");
    Check(Math.Abs(n10.GrowthMult - 0.5) < 0.001, "war taxes: growth halved");
    Check(engine10.ToggleEdict(EdictType.WarTaxes) is null, "repeal accepted");
    Check(!n10.HasEdict(EdictType.WarTaxes), "edict repealed");

    Check(engine10.SetStance(ReligiousStance.Devout) is null, "devout stance adopted");
    Check(Math.Abs(n10.GrowthMult - 1.2) < 0.001, "devout: +20% growth");
    Check(Math.Abs(n10.TaxMult - 0.9) < 0.001, "devout: -10% tax");
    Check(engine10.SetStance(ReligiousStance.Tolerant) is null, "tolerant stance adopted");
    Check(Math.Abs(n10.TradeIncomeMult - 1.2) < 0.001, "tolerant: +20% trade income");

    engine10.ToggleEdict(EdictType.GrainDole);
    long popBefore = n10.Population;
    for (int i = 0; i < 30; i++) engine10.AdvanceOneDay();
    Check(n10.Population - popBefore > popBefore * 0.005, "grain dole visibly boosts growth");
}

Console.WriteLine("== 12. Colonisation ==");
using (var engine11 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n11 = engine11.State.PlayerNation;
    Check(engine11.State.FrontierRegions.Count == 3, "three uncharted regions at start");

    n11.Warships = 0;
    Check(engine11.FoundColony(engine11.State.FrontierRegions[0].Id) is not null,
        "colony refused without warships");
    n11.Warships = 5;
    n11.Gold = 100_000;
    n11.GoodsInventory["Wheat"] = 100_000;
    var region = engine11.State.FrontierRegions[0];
    Check(engine11.FoundColony(region.Id) is null, "colony expedition launched");
    Check(engine11.State.ActiveExpedition is not null, "expedition at sea");
    Check(engine11.FoundColony(engine11.State.FrontierRegions[1].Id) is not null,
        "second expedition blocked while one is at sea");

    long popBefore = n11.Population;
    int farmsBefore = n11.Farms;
    for (int i = 0; i < 30; i++) engine11.AdvanceOneDay();
    Check(engine11.State.FrontierRegions.Count == 2, "region removed from the frontier");
    Check(engine11.State.ActiveExpedition is null, "expedition completes");
    Check(n11.Population > popBefore, "colony settlers join the homeland");
    Check(n11.Farms > farmsBefore, "colony adds farms to the homeland");
    Check(n11.ColoniesFounded == 1, "colony counter increments");
}

Console.WriteLine("== 13. Balance pass: 5-year autoplay + defeat ==");
{
    // A passive player (who does nothing for five years) keeps their country. A single world is chaotic — anything that shifts the
    // timing of the AI world's wars, such as a faster march, reshuffles which countries attack — so this guards a sample of worlds:
    // most must be survived, and in every survived world the country must be sane.
    const int worlds = 12;
    int survived = 0; bool sane = true;
    for (int seed = 90; seed < 90 + worlds; seed++)
    {
        using var e = new GameEngine(new SimulationService(seed: seed), new SaveService(saveFolder));
        var n = e.State.PlayerNation;
        for (int i = 0; i < 1825; i++) e.AdvanceOneDay();
        if (n.IsEliminated) continue;
        survived++;
        sane &= n.Population > 0 && double.IsFinite(n.Gold) && n.Gold >= 0 && n.Soldiers > 0 && !e.State.Defeated;
    }
    Check(survived * 100 >= worlds * 60, $"a passive player keeps their country for 5 years in most worlds ({survived} of {worlds})");
    Check(sane, "...and in every world they survive, population, wealth and army are sane and there is no accidental defeat");
}

using (var engine13 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var p13 = engine13.State.PlayerNation;
    foreach (var other in engine13.State.OtherNations.Where(n => !n.IsEliminated).Take(8).ToList())
        Warfare.AnnexNation(engine13.State, p13, other);
    engine13.AdvanceOneDay();
    Check(!engine13.State.Defeated, "no victory screen: the game goes on after annexations");
    Check(engine13.State.NationsAnnexedByPlayer == 8, "annexations are counted as a statistic");

    Warfare.AnnexNation(engine13.State, engine13.State.OtherNations.First(n => !n.IsEliminated), p13);
    engine13.AdvanceOneDay();
    Check(engine13.State.Defeated, "losing the whole country triggers defeat");
}

Console.WriteLine("== 14. Nation-select data: religion, income estimate, stat counters ==");
using (var engine14 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var all = new[] { engine14.State.PlayerNation }.Concat(engine14.State.OtherNations).ToList();
    Check(all.Count == 41, "41 playable nations");
    Check(all.All(n => !string.IsNullOrWhiteSpace(n.Religion)), "every nation has a religion");
    Check(all.All(n => GameEngine.EstimateDailyIncome(n) > 0), "income estimate positive for all");
    Check(all.All(n => !string.IsNullOrWhiteSpace(n.ColorHex)), "every nation has a crest color");
    Check(all.All(n => !string.IsNullOrWhiteSpace(n.Emblem)), "every nation has an emblem");
    Check(all.All(n => n.HistoricalPopulation > 0), "every nation has a historical population");
    Check(all.All(n => n.Stone >= 0 && n.Lead >= 0 && n.Copper >= 0), "mineral stocks non-negative");
    Check(all.All(n => n.HistoricalPopulation >= n.Population), "historical pop >= sim pop");
    Check(engine14.State.PlayerNation.ColoniesFounded == 0, "colonies counter starts at 0");
    Check(engine14.State.PlayerNation.BattlesWon == 0, "battles-won counter starts at 0");
}

Console.WriteLine("== 15. Per-item stock: mineral output feeds the single mineral stock ==");
using (var engine15 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n15 = engine15.State.PlayerNation;
    n15.Population = 0; // isolate from population consumption
    n15.ProductionBuildings["sawmill"] = 2;
    n15.ProductionBuildings["farm"] = 1;
    n15.GoodsInventory["Iron"] = 7; // legacy-save entry
    double wood0 = n15.Wood, iron0 = n15.Iron;
    engine15.AdvanceOneDay();
    Check(n15.Wood > wood0, "built sawmill adds to Wood stock");
    Check(n15.Iron >= iron0 + 7, "legacy GoodsInventory Iron migrated into Iron stock");
    Check(!n15.GoodsInventory.ContainsKey("Wood") && !n15.GoodsInventory.ContainsKey("Iron"),
          "no duplicate mineral keys in GoodsInventory");
    Check(n15.GetGood("Wheat") > 0, "farm output tracked as its own Wheat stock");
}

Console.WriteLine("== 16. Population-driven item consumption ==");
using (var engine16 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n16 = engine16.State.PlayerNation;
    n16.ProductionBuildings.Clear();
    n16.Population = 1_000_000;
    n16.GoodsInventory["Wheat"] = 1000;
    n16.GoodsInventory["Salt"] = 0;
    n16.Wood = 500;
    var wheat = ConsumptionCatalog.All.First(c => c.Item == "Wheat");
    Check(Math.Abs(ConsumptionService.DailyNeed(wheat, 1_000_000) - 46.46) < 0.01, "wheat need = 46.46/day per 1M people");
    Check(Math.Abs(ConsumptionService.DailyNeed(wheat, 2_000_000) - 92.92) < 0.01, "need doubles with population");
    ConsumptionService.Apply(n16);
    Check(Math.Abs(n16.GetGood("Wheat") - (1000 - 46.46)) < 0.01, "wheat stock reduced by daily need");
    Check(n16.GetGood("Salt") == 0, "empty stock stays at 0 (no negative)");
    Check(Math.Abs(n16.Wood - (500 - 40)) < 0.01, "wood usage drawn from the Wood stockpile");
    n16.GoodsInventory["Wheat"] = 10;
    ConsumptionService.Apply(n16);
    Check(n16.GetGood("Wheat") == 0, "consumption clamps at available stock");
}

Console.WriteLine("== 17. Starting stock = 1 week of need ==");
using (var engine17 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    bool allSeeded = engine17.State.AllNations().All(nat =>
        ConsumptionCatalog.All.All(spec =>
            Math.Abs(nat.GetProduct(spec.Item) - ConsumptionService.DailyNeed(spec, nat.Population) * 7) < 0.001));
    Check(allSeeded, "every nation holds exactly 7 days of need of every item");
    var p17 = engine17.State.PlayerNation;
    for (int i = 0; i < 6; i++) engine17.AdvanceOneDay();
    Check(ConsumptionCatalog.All.All(s => p17.GetProduct(s.Item) > 0), "after 6 days nothing has run out yet");
}

Console.WriteLine("== 18. Shortage effects: deaths + ruler rating ==");
using (var engine18 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n18 = engine18.State.PlayerNation;
    ShortageReport Report(double foodPct, double mineralPct)
    {
        var r = new ShortageReport();
        foreach (var s in ConsumptionCatalog.All)
            r.UnmetPct[s.Item] = s.Group == ItemGroup.Food ? foodPct : mineralPct;
        return r;
    }

    // (a) one food item 50% short, 1M people
    n18.Population = 1_000_000;
    foreach (var s in ConsumptionCatalog.All) n18.AddProduct(s.Item, ConsumptionService.DailyNeed(s, n18.Population) * 3 - n18.GetProduct(s.Item));
    var wheat18 = ConsumptionCatalog.All.First(c => c.Item == "Wheat");
    n18.AddProduct("Wheat", ConsumptionService.DailyNeed(wheat18, n18.Population) * 0.5 - n18.GetProduct("Wheat") );
    var repA = ConsumptionService.Apply(n18);
    Check(Math.Abs(repA.UnmetPct["Wheat"] - 50) < 0.001, "wheat at half its need is 50% short");
    Check(Math.Abs(ConsumptionService.Deaths(repA) - 65) < 0.001, "50% food shortage = 65 deaths/day (1.3 per 1%)");
    Check(Math.Abs(ConsumptionService.RatingDrop(repA) - 0.00005) < 1e-12, "50% food shortage = 0.00005 rating drop");

    // (b) every item 2% short: food 12 x 2 x 0.0001 + minerals 5 x 2 x 0.00003
    var rep2 = Report(2, 2);
    // 12 food x 2 x 0.000001 = 0.000024; 5 minerals x 2 x 0.0000003 = 0.000003 (user's 6-mineral example: 0.0000036 -> 0.0000276)
    Check(Math.Abs(ConsumptionService.RatingDrop(rep2) - (0.000024 + 0.000003)) < 1e-12, "all items 2% short = 0.000027 rating drop");
    Check(Math.Abs((12 * 2 * Balance.ShortageRatingDropFoodPerPct + 6 * 2 * Balance.ShortageRatingDropMineralPerPct) - 0.0000276) < 1e-12,
          "rates reproduce the 18-item example: 0.0000276/day");
    // 12 food x 2 x 1.3 = 31.2; 5 minerals x 2 x 0.55 = 5.5
    Check(Math.Abs(ConsumptionService.Deaths(rep2) - (31.2 + 5.5)) < 1e-9, "all items 2% short = 36.7 deaths/day");
    // The Ottoman screenshot case: 33.5% food / 33% mineral shortage
    Check(Math.Abs(ConsumptionService.Deaths(Report(33.5, 33)) - (12 * 33.5 * 1.3 + 5 * 33 * 0.55)) < 1e-9
          && Math.Abs(ConsumptionService.Deaths(Report(33.5, 33)) - 613.35) < 1e-9, "33.5% food / 33% mineral shortage = 613.35 deaths/day");

    // (c) fractional deaths accumulate: one mineral 1% short = 0.55/day (day 1: 0.55 -> nobody, day 2: 1.1 -> one person)
    var oneMineral = new ShortageReport();
    foreach (var s in ConsumptionCatalog.All) oneMineral.UnmetPct[s.Item] = s.Item == "Wood" ? 1 : 0;
    n18.Population = 1_000_000; n18.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n18, oneMineral);
    Check(n18.Population == 1_000_000, "0.55 death/day: nobody dies on day 1");
    ConsumptionService.ApplyShortageEffects(n18, oneMineral);
    Check(n18.Population == 999_999, "0.55 death/day: one person dies after 2 days");

    // (d) no shortage = no effect
    n18.RulerRating = 50; n18.Population = 1_000_000; n18.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n18, Report(0, 0));
    Check(n18.RulerRating == 50 && n18.Population == 1_000_000, "no shortage: no deaths, no rating drop");

    // (e) rating clamps at 0, population never negative
    n18.RulerRating = 0.001; n18.Population = 10;
    ConsumptionService.ApplyShortageEffects(n18, Report(100, 100));
    Check(n18.RulerRating == 0, "ruler rating clamps at 0");
    Check(n18.Population == 0, "population never goes negative");

    // (f) end-to-end: first shortage appears after the 7-day starting stock
    using var engine18b = new GameEngine(new SimulationService(), new SaveService(saveFolder));
    var p18 = engine18b.State.PlayerNation;
    p18.ProductionBuildings.Clear();
    for (int i = 0; i < 6; i++) engine18b.AdvanceOneDay();
    Check(p18.LastRatingDrop == 0, "no shortage during the first 6 days (7 days of stock; population growth eats the margin on day 7)");
    for (int i = 0; i < 3; i++) engine18b.AdvanceOneDay();
    Check(p18.LastRatingDrop > 0 && p18.RulerRating < 50, "shortage cuts ruler rating once the week of stock is gone");
}

Console.WriteLine("== 19. Production buildings: output x2, build cost x1501 ==");
{
    var farm = ProductionCatalog.Get("farm");     // base: 1 gold, 0 wood, 0 stone, 10/day
    Check(farm.OutputPerDay == 20, "farm output doubled (10 -> 20)");
    Check(ProductionCatalog.Get("saltmine").OutputPerDay == 10, "salt mine output doubled (5 -> 10)");
    Check(farm.GoldCost == 1501, "farm gold cost 1 -> 1,501");
    var gm = ProductionCatalog.Get("goldmine");   // base: 5 gold, 3 wood, 8 stone
    Check(gm.GoldCost == 7505 && gm.WoodCost == 4503 && gm.StoneCost == 12008, "gold mine costs x1501 on gold, wood and stone");
    Check(farm.WoodCost == 0 && farm.StoneCost == 0 && farm.IronCost == 0, "zero costs stay zero");
    Check(ProductionCatalog.All.Count == 18 && ProductionCatalog.All.All(s => s.BuildDays > 0), "18 buildings, build times unchanged");
}

Console.WriteLine("== 20. Starting mills: 30/15/10% shortage by nation size ==");
using (var engine20 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var all20 = engine20.State.AllNations().ToList();
    Nation ById(string id) => all20.First(x => x.Id == id);
    Check(ConsumptionService.StartShortage(ById("ottoman").Population) == 0.30, "Ottomans (30M) are big: 30%");
    Check(ConsumptionService.StartShortage(ById("england").Population) == 0.15, "England (6.1M) is mid: 15%");
    Check(ConsumptionService.StartShortage(ById("sweden").Population) == 0.10, "Sweden (1M) is small: 10%");
    Check(ConsumptionService.StartShortage(10_000_000) == 0.30 && ConsumptionService.StartShortage(2_000_000) == 0.15
          && ConsumptionService.StartShortage(1_999_999) == 0.10, "tier edges: 10M big, 2M mid, below 2M small");

    var ott = ById("ottoman");
    Check(ott.GetProductionBuilding("saltmine") == 25, "Ottoman salt mines = 25");
    Check(ott.GetProductionBuilding("farm") == 49, "Ottoman farms = 49");
    Check(ott.GetProductionBuilding("goldmine") == 0, "gold mine is not seeded");

    // The chosen country (the player) starts in shortage: every item's output is the closest mill count to need x (1 - tier shortage).
    // (Every OTHER country feeds itself: see section 48.)
    bool closest = true; string firstBad = "";
    foreach (var nat in all20.Where(x => x.IsPlayer))
    foreach (var spec in ConsumptionCatalog.All)
    {
        var mill = ProductionCatalog.All.First(b => b.Produces == spec.Item);
        double perMill = ConsumptionService.OutputPerMill(nat, mill);
        double target = ConsumptionService.DailyNeed(spec, nat.Population) * (1 - ConsumptionService.StartShortage(nat.Population));
        double produced = nat.GetProductionBuilding(mill.Id) * perMill;
        if (Math.Abs(produced - target) > perMill / 2 + 1e-9) { closest = false; firstBad = $"{nat.Id}/{spec.Item}"; }
    }
    Check(closest, "every item's starting output is the closest mill count to its target " + firstBad);

    // Production covers 70% of need, so the 7-day stock drains at 30%/day and lasts ~23 days.
    // Once it is gone the Ottomans are roughly 30% short.
    var p20 = engine20.State.PlayerNation;
    for (int i = 0; i < 10; i++) engine20.AdvanceOneDay();
    Check(p20.LastRatingDrop == 0, "no shortage effects while the starting stock lasts (day 10)");
    for (int i = 0; i < 30; i++) engine20.AdvanceOneDay();
    double wheatShort = p20.ShortagePct.TryGetValue("Wheat", out var ws) ? ws : -1;
    Check(wheatShort > 25 && wheatShort < 35, $"Ottoman wheat about 30% short once stock is gone (got {wheatShort:N1}%)");
}

Console.WriteLine("== 21. One account per item: lookup + conquest ==");
using (var engine21 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var win = engine21.State.PlayerNation;
    var lose = engine21.State.OtherNations.First();
    win.Wood = 123; win.Gold = 777;
    Check(win.GetProduct("Wood") == 123 && win.GetGood("Wood") == 0, "Wood lives in the Wood stockpile, not the goods dictionary");
    Check(win.GetProduct("Gold") == 777, "Gold lookup is the treasury");

    win.GoodsInventory.Clear(); win.GoodsInventory["Wheat"] = 10; win.ProductionBuildings.Clear(); win.ProductionBuildings["farm"] = 5;
    lose.GoodsInventory.Clear(); lose.GoodsInventory["Wheat"] = 40; lose.GoodsInventory["Bread"] = 7;
    lose.ProductionBuildings.Clear(); lose.ProductionBuildings["farm"] = 3; lose.ProductionBuildings["bakery"] = 2;
    double woodBefore = win.Wood, loseWood = lose.Wood;
    Warfare.AnnexNation(engine21.State, win, lose);
    Check(win.GetGood("Wheat") == 10 && lose.GetGood("Wheat") == 40 && lose.GetGood("Bread") == 7, "annexation leaves the loser's food items in its territory (not added to the winner's)");
    Check(win.GetProductionBuilding("farm") == 5 && lose.GetProductionBuilding("farm") == 3 && lose.GetProductionBuilding("bakery") == 2, "annexation leaves the loser's mills in its territory");
    Check(Math.Abs(win.Wood - woodBefore) < 0.001 && Math.Abs(lose.Wood - loseWood) < 0.001, "loser's wood counted once, in the territory");
    Check(!win.GoodsInventory.ContainsKey("Wood"), "no mineral keys in goods dictionary after annexation");
}

Console.WriteLine("== 22. Old Food number removed: Wheat pays for events/colonies, growth always applies ==");
using (var engine22 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n22 = engine22.State.PlayerNation;
    Check(typeof(Nation).GetProperty("Food") is null, "Nation.Food no longer exists");

    // Feast costs 1000 Wheat (+ gold)
    n22.Gold = 1_000_000;
    n22.GoodsInventory["Wheat"] = 500;
    Check(engine22.StartNationalEvent("feast") is { } err22 && err22.Contains("Wheat"), "feast refused without enough Wheat");
    n22.GoodsInventory["Wheat"] = 5_000;
    Check(engine22.StartNationalEvent("feast") is null, "feast accepted with enough Wheat");
    Check(Math.Abs(n22.GetGood("Wheat") - 4_000) < 0.001, "feast spent 1000 Wheat");

    // Growth applies on a day with total shortage: no second starvation system.
    n22.ProductionBuildings.Clear();
    foreach (var s in ConsumptionCatalog.All) n22.AddProduct(s.Item, -n22.GetProduct(s.Item));
    n22.Population = 20_000_000;
    n22.ShortageDeathCarry = 0;
    long before22 = n22.Population;
    engine22.AdvanceOneDay();
    long expectedGrowth = (long)(before22 * (Balance.GrowthPerDayWithSurplus * n22.GrowthMult + ReligionService.PopulationGrowthBonus(n22) / 100.0));
    long deaths22 = (long)Math.Floor(n22.LastShortageDeaths);
    Check(n22.Population >= before22 + expectedGrowth - deaths22 - 1 && n22.Population <= before22 + expectedGrowth - deaths22 + 1,
          "full shortage day = normal growth minus only the shortage deaths");
}

Console.WriteLine("== 23. Per-item trade: each food item buyable/sellable with its own price ==");
using (var engine23 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var foodItems = ConsumptionCatalog.All.Where(c => c.Group == ItemGroup.Food).Select(c => c.Item).ToList();
    var foodProducts = TradeCatalog.All.Where(p => p.Category == "FoodGoods").ToList();
    Check(foodItems.All(i => foodProducts.Any(p => p.Name == i)), "all 12 food items are trade products");
    Check(foodProducts.All(p => MarketPricing.PricePer1000(p.Id, "ottoman") > 0), "every food item has a price");
    Check(TradeCatalog.Get("food") is null, "generic Food product is gone");

    var me = engine23.State.PlayerNation;
    var other = engine23.State.OtherNations.First();
    me.Population = 0; other.Population = 0;       // isolate from consumption
    me.ProductionBuildings.Clear(); other.ProductionBuildings.Clear(); // ...and from mill output
    me.Gold = 1_000_000; other.Gold = 1_000_000;
    other.GoodsInventory["Wheat"] = 10_000;
    TreatyService.Add(engine23.State, TreatyType.Embassy, me, other);   // trading needs an embassy first
    double wheat0 = me.GetGood("Wheat"), gold0 = me.Gold;

    Check(engine23.BuyProduct(other.Id, "wheat", 6_000) is not null, "cannot buy more than 50% of the seller's stock");
    Check(engine23.BuyProduct(other.Id, "wheat", 1_000) is null, "bought 1,000 Wheat");
    double price = MarketPricing.PricePer1000("wheat", other.Id);
    Check(Math.Abs((gold0 - me.Gold) - MarketPricing.TotalValue(price, 1_000)) < 0.01, "gold paid at the Wheat price");
    Check(Math.Abs(other.GetGood("Wheat") - 9_000) < 0.001, "seller stock reserved immediately");
    for (int i = 0; i < 10; i++) engine23.AdvanceOneDay();
    Check(Math.Abs(me.GetGood("Wheat") - (wheat0 + 1_000)) < 0.001, "Wheat delivered to the 'Wheat' stock");
    Check(!me.GoodsInventory.ContainsKey("wheat"), "no lowercase duplicate stock key");

    me.GoodsInventory["Bread"] = 500; other.GoodsInventory["Bread"] = 0;
    double sellPrice = MarketPricing.PricePer1000("bread", me.Id);
    Check(engine23.SellProduct(other.Id, "bread", 200, sellPrice) is null, "sold 200 Bread");
    Check(Math.Abs(me.GetGood("Bread") - 300) < 0.001, "Bread deducted at sale");
    for (int i = 0; i < 10; i++) engine23.AdvanceOneDay();
    Check(Math.Abs(other.GetGood("Bread") - 200) < 0.001, "Bread delivered to the buyer's 'Bread' stock");
    Check(engine23.State.TradeContracts.Last().Status == TradeStatus.Delivered, "sale contract completed and paid");
}

Console.WriteLine("== 24. Realistic tax income + 50,000 starting gold ==");
using (var engine24 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    Check(engine24.State.AllNations().All(n => n.Gold == 50_000), "every nation starts with 50,000 gold");
    Check(TaxationService.PeasantIncome == 0.00002 && TaxationService.CraftsmanIncome == 0.0001
          && TaxationService.MilitaryIncome == 0.001 && TaxationService.MerchantIncome == 0.00013
          && TaxationService.SpyIncome == 0.0013 && TaxationService.SaboteurIncome == 0.0013, "max tax per head per day as specified");
    var w = new Workforce { Peasants = 1_000_000, Craftsmen = 1_000_000, MilitaryPersonnel = 1_000, Merchants = 1_000_000, Spies = 1_000, Saboteurs = 1_000 };
    var full = new TaxRates { Peasants = 100, Craftsmen = 100, MilitaryPersonnel = 100, Merchants = 100, Spies = 100, Saboteurs = 100 };
    double expectedFull = 1_000_000 * 0.00002 + 1_000_000 * 0.0001 + 1_000 * 0.001 + 1_000_000 * 0.00013 + 1_000 * 0.0013 + 1_000 * 0.0013;
    Check(Math.Abs(TaxationService.TotalRevenue(w, full) - expectedFull) < 1e-6, "100% tax pays exactly the max per head");
    var half = new TaxRates { Peasants = 50, Craftsmen = 50, MilitaryPersonnel = 50, Merchants = 50, Spies = 50, Saboteurs = 50 };
    Check(Math.Abs(TaxationService.TotalRevenue(w, half) - expectedFull / 2) < 1e-6, "50% tax pays half");
    Check(TaxationService.TotalRevenue(w, new TaxRates { Peasants = 0, Craftsmen = 0, MilitaryPersonnel = 0, Merchants = 0, Spies = 0, Saboteurs = 0 }) == 0, "0% tax pays nothing");

    var ott = engine24.State.PlayerNation;
    ott.EnsureTaxationInitialized();
    double goldBefore = ott.Gold;
    long est = GameEngine.EstimateDailyIncome(ott);
    engine24.AdvanceOneDay();
    Check(Math.Abs((ott.Gold - goldBefore) - est) < est * 0.01 + 5, $"top-bar income estimate matches the real daily gain (est {est:N0}, got {ott.Gold - goldBefore:N0})");
}

Console.WriteLine("== 25. Net population change per day = births - shortage deaths ==");
using (var engine25 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n25 = engine25.State.PlayerNation;
    // (a) fully supplied: one day's gain equals the shown births
    n25.Population = 30_000_000;
    foreach (var s in ConsumptionCatalog.All) n25.AddProduct(s.Item, DailyNeedBig(s) - n25.GetProduct(s.Item));
    double DailyNeedBig(ConsumptionSpec s) => ConsumptionService.DailyNeed(s, 40_000_000) * 5;
    long births25 = PopulationService.DailyBirths(n25);
    long pop25 = n25.Population;
    engine25.AdvanceOneDay();
    Check(births25 > 0 && n25.Population - pop25 == births25 && n25.LastShortageDeaths == 0, "no shortage: real daily gain = shown births");

    // (b) everything short: real change = births - deaths (shown net), here negative or smaller
    foreach (var s in ConsumptionCatalog.All) n25.AddProduct(s.Item, -n25.GetProduct(s.Item));
    n25.ProductionBuildings.Clear();
    n25.Population = 1_000_000; n25.ShortageDeathCarry = 0;
    long b25 = PopulationService.DailyBirths(n25);
    long p25 = n25.Population;
    engine25.AdvanceOneDay();
    double shownNet = b25 - n25.LastShortageDeaths;
    Check(Math.Abs((n25.Population - p25) - shownNet) <= 1, $"shortage day: real change {n25.Population - p25:N0} matches shown net {shownNet:N1}");
    Check(shownNet < 0, "a fully short nation shows a negative net change");
}

Console.WriteLine("== 26. No births while any item is short ==");
using (var engine26 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n26 = engine26.State.PlayerNation;
    n26.Population = 30_000_000;
    n26.ShortagePct = new Dictionary<string, double>();
    Check(PopulationService.DailyBirths(n26) > 0, "no shortage: births > 0");
    n26.ShortagePct = ConsumptionCatalog.All.ToDictionary(c => c.Item, c => c.Item == "Salt" ? 0.5 : 0.0);
    Check(PopulationService.DailyBirths(n26) == 0, "one item (Salt) 0.5% short: births = 0");
    n26.ShortagePct = ConsumptionCatalog.All.ToDictionary(c => c.Item, c => c.Item == "Wood" ? 5.0 : 0.0);
    Check(PopulationService.DailyBirths(n26) == 0, "a mineral (Wood) short also stops births");

    // Real tick: starved nation's population only falls by the shortage deaths.
    foreach (var s in ConsumptionCatalog.All) n26.AddProduct(s.Item, -n26.GetProduct(s.Item));
    n26.ProductionBuildings.Clear();
    n26.ShortageDeathCarry = 0;
    n26.Population = 1_000_000;
    engine26.AdvanceOneDay();                    // day 1: shortage is detected (births still use the previous day)
    long popShort = n26.Population;
    engine26.AdvanceOneDay();                    // day 2: births must be 0
    long deaths26 = (long)Math.Floor(n26.LastShortageDeaths + 0.999);
    Check(popShort - n26.Population >= (long)n26.LastShortageDeaths && popShort - n26.Population <= deaths26 + 1,
          "while short, population change = -deaths only (no births)");
    // Once supplied again, births resume.
    foreach (var s in ConsumptionCatalog.All) n26.AddProduct(s.Item, ConsumptionService.DailyNeed(s, 2_000_000) * 10 - n26.GetProduct(s.Item));
    engine26.AdvanceOneDay();                    // consumption fully met -> shortage cleared
    long popBeforeResume = n26.Population;
    engine26.AdvanceOneDay();
    Check(n26.Population > popBeforeResume, "supplied again: births resume");
}

Console.WriteLine("== 27. Dynamic safe tax threshold + tax death increase ==");
using (var engine27 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n27 = engine27.State.PlayerNation;
    n27.Population = 1_000_000;
    ShortageReport Rep(double pct) { var r = new ShortageReport(); foreach (var s in ConsumptionCatalog.All) r.UnmetPct[s.Item] = pct; return r; }

    // give every item mills producing `mult` x its need
    void SetOutput(double mult)
    {
        n27.ProductionBuildings.Clear();
        foreach (var s in ConsumptionCatalog.All)
        {
            var mill = ProductionCatalog.All.First(b => b.Produces == s.Item);
            n27.ProductionBuildings[mill.Id] = (int)Math.Ceiling(ConsumptionService.DailyNeed(s, n27.Population) * mult / ConsumptionService.OutputPerMill(n27, mill));
        }
    }

    // thresholds (no surplus: tiny output)
    SetOutput(0.1);
    Check(!ConsumptionService.HasLargeSurplus(n27), "no surplus when output is far below need");
    ShortageReport OneShort(string item, double pct) { var r = new ShortageReport(); foreach (var s in ConsumptionCatalog.All) r.UnmetPct[s.Item] = s.Item == item ? pct : 0; return r; }
    Check(ConsumptionService.SafeTaxThreshold(n27, Rep(0)) == 60, "no shortage, no large surplus -> safe threshold 60");
    Check(ConsumptionService.SafeTaxThreshold(n27, OneShort("Salt", 0.1)) == 30, "one item 0.1% short -> 30");
    Check(ConsumptionService.SafeTaxThreshold(n27, OneShort("Iron", 5)) == 30, "a mineral 5% short -> 30");
    Check(ConsumptionService.SafeTaxThreshold(n27, Rep(5)) == 30 && ConsumptionService.SafeTaxThreshold(n27, Rep(15)) == 30
          && ConsumptionService.SafeTaxThreshold(n27, Rep(33)) == 30 && ConsumptionService.SafeTaxThreshold(n27, Rep(100)) == 30,
          "any shortage (5%, 15%, 33%, 100%) -> 30, no matter how big");

    // surplus: all 17 items above 150% of need -> 100, and it beats the shortage rule
    SetOutput(1.6);
    Check(ConsumptionService.HasLargeSurplus(n27), "all items at 160% output = large surplus");
    Check(ConsumptionService.SafeTaxThreshold(n27, Rep(0)) == 100, "large surplus -> safe threshold 100");
    Check(ConsumptionService.SafeTaxThreshold(n27, Rep(50)) == 100, "surplus takes priority over shortage");
    var wheatMill = ProductionCatalog.All.First(b => b.Produces == "Wheat");
    n27.ProductionBuildings[wheatMill.Id] = (int)(ConsumptionService.DailyNeed(ConsumptionCatalog.All.First(c => c.Item == "Wheat"), n27.Population) * 1.4 / ConsumptionService.OutputPerMill(n27, wheatMill));
    Check(!ConsumptionService.HasLargeSurplus(n27), "one item below 150% cancels the surplus rule");
    Check(ConsumptionService.SafeTaxThreshold(n27, Rep(0)) == 60, "...and with no shortage the threshold falls back to 60");

    // tax death increase = sum over six types of max(0, rate - threshold) x 0.15
    var all100 = new TaxRates { Peasants = 100, Craftsmen = 100, MilitaryPersonnel = 100, Merchants = 100, Spies = 100, Saboteurs = 100 };
    Check(Math.Abs(ConsumptionService.TaxDeathIncreasePct(all100, 30) - 63.0) < 1e-9, "all six at 100% with threshold 30 = +63%");
    Check(ConsumptionService.TaxDeathIncreasePct(new TaxRates(), 30) == 0, "default tax rates are below 30: no increase");
    Check(ConsumptionService.TaxDeathIncreasePct(all100, 100) == 0, "threshold 100: no increase");
    Check(Math.Abs(ConsumptionService.TaxDeathIncreasePct(new TaxRates { Peasants = 60, Craftsmen = 0, MilitaryPersonnel = 0, Merchants = 0, Spies = 0, Saboteurs = 0 }, 50) - 1.5) < 1e-9,
          "one tax 10 points over the threshold = +1.5%");

    // final deaths: base x (1 + increase/100). All items 30% short -> base 550.5; all six taxes 100% -> x1.63
    SetOutput(0.1);
    n27.TaxRates = all100; n27.ShortageDeathCarry = 0; n27.Population = 50_000_000;
    ConsumptionService.ApplyShortageEffects(n27, Rep(30));
    Check(Math.Abs(n27.LastShortageDeaths - 550.5 * 1.63) < 1e-6, $"final deaths = 550.5 x 1.63 = {550.5 * 1.63:N3} (got {n27.LastShortageDeaths:N3})");
    Check(n27.LastSafeTaxThreshold == 30 && Math.Abs(n27.LastTaxDeathIncreasePct - 63) < 1e-9, "threshold and increase stored for the UI");

    // same shortage, default taxes: no increase
    n27.TaxRates = new TaxRates(); n27.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n27, Rep(30));
    Check(Math.Abs(n27.LastShortageDeaths - 550.5) < 1e-6, "default taxes: deaths stay at 550.5");

    // Danger-zone tax -> flat 0.05 rating per month (0.05/30 per day), whatever the level
    var shortOnly = ConsumptionService.RatingDrop(Rep(30));
    n27.RulerRating = 50; n27.ShortageDeathCarry = 0;
    n27.TaxRates = all100;
    ConsumptionService.ApplyShortageEffects(n27, Rep(30));
    Check(Math.Abs(n27.LastRatingDrop - (shortOnly + 0.05 / 30)) < 1e-12, "tax in danger zone adds 0.05/30 rating drop per day");
    n27.TaxRates = new TaxRates { Peasants = 31, Craftsmen = 0, MilitaryPersonnel = 0, Merchants = 0, Spies = 0, Saboteurs = 0 };
    ConsumptionService.ApplyShortageEffects(n27, Rep(30));
    Check(Math.Abs(n27.LastRatingDrop - (shortOnly + 0.05 / 30)) < 1e-12, "same flat drop for one tax just 1 point over (level doesn't matter)");
    n27.TaxRates = new TaxRates();
    ConsumptionService.ApplyShortageEffects(n27, Rep(30));
    Check(Math.Abs(n27.LastRatingDrop - shortOnly) < 1e-12, "taxes within the safe zone: no extra rating drop");
    n27.RulerRating = 50; n27.TaxRates = all100;
    for (int d = 0; d < 30; d++) ConsumptionService.ApplyShortageEffects(n27, Rep(0));
    Check(Math.Abs((50 - n27.RulerRating) - 0.05) < 1e-9, "30 days in the danger zone = exactly 0.05 rating lost");

    // Small shortage (5%) still uses threshold 30: taxes 100% -> 6 x 70 x 0.15 = +63%
    n27.TaxRates = all100; n27.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n27, Rep(5));
    double base5 = 12 * 5 * 1.3 + 5 * 5 * 0.55;
    Check(n27.LastSafeTaxThreshold == 30 && Math.Abs(n27.LastShortageDeaths - base5 * 1.63) < 1e-6, "5% short, taxes 100%: threshold 30 -> +63% deaths");

    // No shortage, no surplus: threshold 60 -> taxes 100% = 6 x 40 x 0.15 = +36% (deaths themselves are 0)
    n27.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n27, Rep(0));
    Check(n27.LastSafeTaxThreshold == 60 && Math.Abs(n27.LastTaxDeathIncreasePct - 36) < 1e-9, "no shortage: threshold 60, taxes 100% = +36%");
    // ...and taxes at 60 or below are safe then
    n27.TaxRates = new TaxRates { Peasants = 60, Craftsmen = 60, MilitaryPersonnel = 60, Merchants = 60, Spies = 60, Saboteurs = 60 };
    ConsumptionService.ApplyShortageEffects(n27, Rep(0));
    Check(n27.LastTaxDeathIncreasePct == 0, "no shortage: taxes up to 60 are safe");
}

// ---------------- Diplomatic actions (sections 28-38) ----------------
// The mechanics tests start from a clean slate: no embassies and every regard at neutral, as before the 1600 diplomatic start (section 62 tests that start).
void Neutral(GameEngine e) { e.State.Treaties.RemoveAll(t => t.Type == TreatyType.Embassy); foreach (var n in e.State.OtherNations) { n.RelationToPlayer = 0; n.BaselineRelation = 0; } }
GameEngine NewDipEngine(int seed = 1) { var e = new GameEngine(new SimulationService(seed), new SaveService(saveFolder)); Neutral(e); return e; }
Nation Ai(GameEngine eng, string id) => eng.State.OtherNations.First(x => x.Id == id);
void SetRating(Nation n, int rating) => n.RelationToPlayer = DiplomacyService.FromDisplayRating(rating);
int Rate(Nation n) => DiplomacyService.ToDisplayRating(n.RelationToPlayer);
int CountOf(UnitType type, Nation n) => n.Units.Where(u => u.Type == type).Sum(u => u.Count);
bool HasLog(GameEngine eng, string text) => eng.State.EventLog.Any(x => x.Contains(text));
void Embassy(GameEngine e, params Nation[] nations) { foreach (var n in nations) if (!e.HasTradeEmbassy(n.Id)) TreatyService.Add(e.State, TreatyType.Embassy, e.State.PlayerNation, n); }   // (trading needs an embassy first)

Console.WriteLine("== 28. Diplomatic actions: catalogue and availability ==");
using (var eng = NewDipEngine())
{
    var all = DiplomaticActionCatalog.All;
    Check(all.Count == 14, "14 diplomatic actions are defined");
    Check(all.Select(x => x.Id).Distinct().Count() == 14, "action ids are unique");
    Check(all.Count(x => x.Tab == DiplomaticTab.Treaties) == 7 && all.Count(x => x.Tab == DiplomaticTab.Relations) == 7,
        "7 actions on each of the two diplomacy pages");
    Check(all.All(x => x.Icon.Length > 0 && x.Name.Length > 0 && x.Requirements.Length > 0 && x.Effects.Length > 0),
        "every action has an icon, a label, requirements and effects");

    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    var persia = Ai(eng, "persia");
    bool Can(string action, Nation n) => eng.CheckDiplomaticAction(action, n.Id).Available;
    string Why(string action, Nation n) => eng.CheckDiplomaticAction(action, n.Id).Reason;

    Check(Can("embassy", france), "embassy is available at neutral relations");
    Check(!Can("nap", france) && Why("nap", france).Contains("embassy"), "non-aggression pact needs an embassy first");
    Check(!Can("alliance", france) && Why("alliance", france).Contains("embassy"), "alliance needs an embassy first");
    Check(!Can("trade", france) && Why("trade", france).Contains("embassy"), "trade agreement needs an embassy first");
    Check(!Can("sendtroops", france) && Why("sendtroops", france).Contains("embassy"), "sending troops needs an embassy first");
    Check(!Can("calltoarms", france) && Why("calltoarms", france).Contains("not your defensive ally"), "call to arms needs a defensive ally");
    Check(Can("givearmy", france) && Can("gift", france) && Can("improve", france) && Can("aid", france),
        "give army, gift, improve relations and aid are available at peace");
    Check(!Can("colony", france) && Why("colony", france).Contains("no colony"), "present a colony needs a colony you own");
    Check(Can("missionary", france), "missionaries can go to a country of another faith");
    Check(!Can("missionary", persia) && Why("missionary", persia).Contains("already follows"), "no missionaries to a country of your own faith");

    // Research Contract and Support Sovereignty are real now: like the other pacts they need an embassy first.
    Check(all.All(x => x.BlockedReason is null), "no action is blocked on a missing system any more");
    Check(!Can("research", france) && Why("research", france).Contains("embassy"), "research contract needs an embassy first");
    Check(!Can("sovereignty", france) && Why("sovereignty", france).Contains("embassy"), "support sovereignty needs an embassy first");
    double goldBefore = p.Gold;
    Check(eng.ProposeResearchContract(france.Id).Outcome == DipOutcome.Invalid && eng.ProposeSovereigntyGuarantee(france.Id).Outcome == DipOutcome.Invalid
          && p.Gold == goldBefore, "refused for lack of an embassy: nothing is spent");
    Check(DiplomaticActionCatalog.HostileActions.Select(x => x.Id).SequenceEqual(new[] { "askattack", "annex" })
          && DiplomaticActionCatalog.Get("annex") is not null && all.Count == 14, "Ask Attack and Annex have sheets without changing the 14 diplomacy actions");
    Check(!eng.CheckDiplomaticAction("bogus", france.Id).Available, "unknown action is unavailable");
    Check(!eng.CheckDiplomaticAction("embassy", "nowhere").Available, "unknown country is unavailable");

    // War closes every friendly action.
    eng.DeclareWar(persia.Id);
    Check(!Can("embassy", persia) && !Can("gift", persia) && !Can("givearmy", persia) && !Can("improve", persia),
        "at war the friendly actions are disabled");
    Check(Why("gift", persia).Contains("at war"), "the reason names the war");
}

Console.WriteLine("== 29. Embassy ==");
using (var eng = NewDipEngine())
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    var spain = Ai(eng, "spain");
    double g0 = p.Gold;
    var r = eng.EstablishEmbassy(france.Id);
    Check(r.Ok, "embassy accepted at neutral relations");
    Check(Math.Abs(p.Gold - (g0 - Balance.EmbassyCost)) < 0.01, "embassy costs its price");
    Check(TreatyService.HasEmbassy(eng.State, p.Id, france.Id), "the embassy is tracked");
    Check(!TreatyService.HasEmbassy(eng.State, france.Id, p.Id), "an embassy is one-way: theirs in our land does not exist");
    var dup = eng.EstablishEmbassy(france.Id);
    Check(dup.Outcome == DipOutcome.Invalid && Math.Abs(p.Gold - (g0 - Balance.EmbassyCost)) < 0.01, "a second embassy is refused and costs nothing");

    for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
    Check(france.RelationToPlayer > 1.0, "an embassy warms relations every day (beats the daily drift)");
    Check(spain.RelationToPlayer <= 0.0001, "countries without an embassy do not warm");

    var cancel = eng.CancelTreaty(TreatyType.Embassy, france.Id);
    int ratingBefore = Rate(france);
    Check(cancel.Ok && !TreatyService.HasEmbassy(eng.State, p.Id, france.Id), "an embassy can be closed");
    Check(eng.CancelTreaty(TreatyType.Embassy, france.Id).Outcome == DipOutcome.Invalid, "closing it again is invalid");
    Check(Rate(france) <= ratingBefore, "closing an embassy never improves relations");

    // Refusal: eligible (>=40) but the AI's attitude (45) is not reached.
    SetRating(spain, 42);
    double g1 = p.Gold;
    var refused = eng.EstablishEmbassy(spain.Id);
    Check(refused.Outcome == DipOutcome.Rejected && refused.Message.Contains("declines"), "a cool country declines the embassy");
    Check(p.Gold == g1, "a refused embassy costs nothing");
    Check(Rate(spain) == 41, "a refusal stings relations by a point");
    Check(!TreatyService.HasEmbassy(eng.State, p.Id, spain.Id), "no embassy after a refusal");
    var again = eng.EstablishEmbassy(spain.Id);
    Check(again.Outcome == DipOutcome.Invalid && again.Message.Contains("Available again"), "asking again straight away is on cooldown");

    // Rules that stop it before anyone is asked.
    var mughal = Ai(eng, "mughal");
    SetRating(mughal, 39);
    Check(eng.EstablishEmbassy(mughal.Id).Message.Contains("regards you too poorly"), "relations under 40: no embassy");
    var persia = Ai(eng, "persia");
    p.Gold = Balance.EmbassyCost - 1;
    Check(eng.EstablishEmbassy(persia.Id).Message.Contains("Needs"), "cannot afford the embassy");
    p.Gold = 100_000;

    // War closes the embassy.
    eng.EstablishEmbassy(persia.Id);
    Check(TreatyService.HasEmbassy(eng.State, p.Id, persia.Id), "embassy in Persia");
    eng.DeclareWar(persia.Id);
    Check(!TreatyService.HasEmbassy(eng.State, p.Id, persia.Id), "war closes the embassy");
}

Console.WriteLine("== 30. Non-aggression pact: signing and enforcement ==");
using (var eng = NewDipEngine(3))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    Check(eng.ProposeNonAggression(france.Id).Outcome == DipOutcome.Invalid, "no pact without an embassy");
    eng.EstablishEmbassy(france.Id);
    double g0 = p.Gold;
    var start = eng.State.CurrentDate;
    var r = eng.ProposeNonAggression(france.Id);
    Check(r.Ok, "pact signed (embassy 50 + 5 = 55 reaches the 55 asked)");
    Check(Math.Abs(p.Gold - (g0 - Balance.NapCost)) < 0.01, "pact costs its price");
    var pact = eng.State.Treaties.First(x => x.Type == TreatyType.NonAggression);
    Check(pact.ExpiresDate == start.AddDays(Balance.NapDays), "the pact has a defined duration");
    Check(eng.ProposeNonAggression(france.Id).Outcome == DipOutcome.Invalid, "no second pact while one runs");

    // Enforcement: player side.
    var war = eng.DeclareWar(france.Id);
    Check(war is not null && war.Contains("non-aggression"), "the player cannot declare war under the pact");
    Check(!france.AtWarWithPlayer && Rate(france) > 0, "a blocked war changes nothing");
    var tribute = eng.DemandTribute(france.Id);
    Check(tribute is not null && tribute.Contains("non-aggression"), "tribute demands are barred too");
    Check(eng.EstablishNetwork(france.Id) is null, "setting up a spy network is still allowed");
    int strength = eng.GetNetwork(france.Id)!.Strength;
    string?[] spy = { eng.SpySteal(france.Id), eng.SpySabotage(france.Id), eng.SpyInciteRevolt(france.Id) };
    Check(spy.All(s => s is not null && s.Contains("non-aggression")), "hostile spy operations are barred");
    Check(eng.GetNetwork(france.Id)!.Strength == strength, "a barred spy operation spends nothing");
    var (okInv, _) = eng.LaunchInvasion(france.Id, 1000);
    Check(!okInv, "no invasion under the pact");

    // Enforcement: AI side — a furious AI still cannot declare war while the pact runs.
    for (int i = 0; i < 150; i++)
    {
        france.RelationToPlayer = -100;
        p.Gold = 1_000_000;
        eng.AdvanceOneDay();
    }
    Check(!france.AtWarWithPlayer, "a furious AI does not declare war under the pact");

    // Control: break the pact and the same AI does declare war.
    var cancel = eng.CancelTreaty(TreatyType.NonAggression, france.Id);
    Check(cancel.Ok && !TreatyService.Has(eng.State, TreatyType.NonAggression, p.Id, france.Id), "the pact can be cancelled");
    for (int i = 0; i < 600 && !france.AtWarWithPlayer; i++)
    {
        france.RelationToPlayer = -100;
        p.Gold = 1_000_000;
        eng.AdvanceOneDay();
    }
    Check(france.AtWarWithPlayer, "control: without the pact the same furious AI declares war");
    Check(!TreatyService.HasEmbassy(eng.State, p.Id, france.Id), "and that war closes the embassy");
}

using (var eng = NewDipEngine(4))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    eng.EstablishEmbassy(france.Id);
    eng.ProposeNonAggression(france.Id);
    int ratingBeforeBreak = Rate(france);
    var br = eng.CancelTreaty(TreatyType.NonAggression, france.Id);
    Check(br.Ok && Rate(france) == ratingBeforeBreak - Balance.BreakPactRatingPenalty, "breaking a pact costs relations");
    Check(eng.DeclareWar(france.Id) is null, "after breaking the pact war may be declared");

    // Expiry: the pact lapses after its term; the embassy stays.
    var persia = Ai(eng, "persia");
    eng.EstablishEmbassy(persia.Id);
    eng.ProposeNonAggression(persia.Id);
    for (int i = 0; i < Balance.NapDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(!TreatyService.Has(eng.State, TreatyType.NonAggression, p.Id, persia.Id), "the pact expires after its term");
    Check(HasLog(eng, "non-aggression pact with Iran has expired"), "the expiry is logged");
    Check(TreatyService.HasEmbassy(eng.State, p.Id, persia.Id), "the embassy outlives the pact");
    Check(eng.DeclareWar(persia.Id) is null, "war is possible again once the pact has expired");

    // An expired-but-not-yet-purged treaty never counts.
    var expired = new Treaty { Type = TreatyType.NonAggression, NationAId = p.Id, NationBId = "x", ExpiresDate = eng.State.CurrentDate };
    Check(!expired.IsActiveOn(eng.State.CurrentDate) && expired.IsActiveOn(eng.State.CurrentDate.AddDays(-1)), "a treaty ends on its expiry date");
}

using (var eng = NewDipEngine(6))
{
    // A march already under way is recalled if a pact now forbids the attack (the belt-and-braces check).
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    TreatyService.Add(eng.State, TreatyType.NonAggression, p, persia, 365);
    persia.AtWarWithPlayer = true;
    var march = TreatyService.LaunchMarch(eng.State, p, persia, 1000)!;
    int afterLaunch = p.Soldiers;
    for (int i = 0; i < march.TotalDays; i++) eng.AdvanceOneDay();
    Check(eng.State.MarchingArmies.Count == 0 && !persia.IsEliminated, "a march against a pact partner annexes nothing");
    // (+1,000 comes home; daily war attrition shaves a few percent off the rest.)
    Check(p.Soldiers > afterLaunch + 500, "the recalled force comes home");
    Check(HasLog(eng, "called off"), "the recall is logged");
}

Console.WriteLine("== 31. Defensive alliance: signing, enforcement, allied defence ==");
using (var eng = NewDipEngine(5))
{
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain");
    var persia = Ai(eng, "persia");
    Check(eng.ProposeAlliance(spain.Id).Outcome == DipOutcome.Invalid, "no alliance without an embassy");
    eng.EstablishEmbassy(spain.Id);
    Check(eng.ProposeAlliance(spain.Id).Message.Contains("regards you too poorly"), "no alliance below relations 60");

    // Eligible (62) but the AI's attitude (62 + embassy 5 = 67) misses the 70 asked.
    SetRating(spain, 62);
    double g0 = p.Gold;
    var refused = eng.ProposeAlliance(spain.Id);
    Check(refused.Outcome == DipOutcome.Rejected && p.Gold == g0, "a lukewarm country declines the alliance at no cost");
    Check(Rate(spain) == 61, "and a refusal stings relations by a point");
    Check(eng.ProposeAlliance(spain.Id).Message.Contains("Available again"), "asking again is on cooldown");

    p.DiplomacyCooldowns.Clear();
    SetRating(spain, 66);
    var accepted = eng.ProposeAlliance(spain.Id);
    Check(accepted.Ok, "a warm country (66 + 5 = 71) accepts");
    Check(Math.Abs(p.Gold - (g0 - Balance.AllianceCost)) < 0.01, "the alliance costs its price");
    Check(TreatyService.Has(eng.State, TreatyType.DefensiveAlliance, p.Id, spain.Id), "the alliance is tracked");
    var warOnAlly = eng.DeclareWar(spain.Id);
    Check(warOnAlly is not null && warOnAlly.Contains("allied"), "you cannot attack an ally");
    Check(eng.ProposeAlliance(spain.Id).Outcome == DipOutcome.Invalid, "no second alliance");
    double rel = spain.RelationToPlayer;
    eng.AdvanceOneDay();
    Check(spain.RelationToPlayer > rel, "an alliance keeps warming relations");

    // Allied defence: the ally marches a share of ITS OWN army on the aggressor.
    int spainBefore = spain.Soldiers, playerBefore = p.Soldiers;
    int marched = TreatyService.AllianceDefence(eng.State, persia, p);
    Check(marched == 1, "one ally honours the alliance");
    var def = eng.State.MarchingArmies.First(x => x.AttackerNationId == spain.Id && x.TargetNationId == persia.Id);
    int commit = (int)(spainBefore * Balance.AllianceAidFraction);
    Check(def.Strength == commit && spain.Soldiers == spainBefore - commit, "the ally's soldiers leave its army exactly as they march");
    Check(spain.Soldiers + def.Strength == spainBefore && p.Soldiers == playerBefore, "no soldiers are created or copied");
    Check(eng.State.ActiveWarnings.Any(x => x.Contains("marches to defend you")), "the player is told");

    // Cases where the ally does not come.
    eng.State.MarchingArmies.Clear();
    spain.Units = UnitCatalog.SeedArmy(1000);
    Check(TreatyService.AllianceDefence(eng.State, persia, p) == 0 && HasLog(eng, "too weak"), "a weak ally cannot march");
    spain.Units = UnitCatalog.SeedArmy(9000);
    SetRating(spain, 30);
    Check(TreatyService.AllianceDefence(eng.State, persia, p) == 0 && HasLog(eng, "ignores the call"), "an ally that has soured ignores the call");
    SetRating(spain, 80);
    Check(TreatyService.AllianceDefence(eng.State, spain, p) == 0, "an ally never marches on itself");
    spain.AtWarWithPlayer = true;
    Check(TreatyService.AllianceDefence(eng.State, persia, p) == 0, "an ally at war with you does not come");
}

using (var eng = NewDipEngine(21))
{
    // Integration: an AI that declares war triggers the defence by itself.
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain");
    var persia = Ai(eng, "persia");
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    for (int i = 0; i < 400 && !persia.AtWarWithPlayer; i++)
    {
        persia.RelationToPlayer = -100;
        p.Gold = 1_000_000;
        eng.AdvanceOneDay();
    }
    Check(persia.AtWarWithPlayer, "the furious AI declared war");
    Check(eng.State.MarchingArmies.Any(x => x.AttackerNationId == spain.Id && x.TargetNationId == persia.Id),
        "the ally marched on the aggressor the same day");
    Check(HasLog(eng, "honours the alliance"), "the alliance is logged");
}

using (var eng = NewDipEngine(22))
{
    // A furious ally cannot declare war on the player either.
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain");
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    for (int i = 0; i < 150; i++) { spain.RelationToPlayer = -100; p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(!spain.AtWarWithPlayer, "an ally never declares war on the player");
}

Console.WriteLine("== 32. Trade agreement: tracked, cheaper imports, richer exports ==");
using (var eng = NewDipEngine(8))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    Check(eng.ProposeTradeAgreement(france.Id).Outcome == DipOutcome.Invalid, "no trade agreement without an embassy");
    eng.EstablishEmbassy(france.Id);
    double g0 = p.Gold;
    var r = eng.ProposeTradeAgreement(france.Id);
    Check(r.Ok && Math.Abs(p.Gold - (g0 - Balance.TradeAgreementCost)) < 0.01, "agreement signed for its price");
    Check(TreatyService.Has(eng.State, TreatyType.TradeAgreement, p.Id, france.Id) && france.HasTradePactWithPlayer,
        "the agreement is tracked and drives the existing pact income");
    Check(eng.ProposeTradeAgreement(france.Id).Outcome == DipOutcome.Invalid, "no second agreement");

    // Cheaper imports.
    Check(eng.BuyProduct(france.Id, "wheat", 1000) is null, "buy wheat from the partner");
    double withPrice = eng.State.TradeContracts.Last().PricePer1000;
    var cancelled = eng.CancelTreaty(TreatyType.TradeAgreement, france.Id);
    Check(cancelled.Ok && !france.HasTradePactWithPlayer, "cancelling ends the agreement and clears the pact flag");
    Check(eng.BuyProduct(france.Id, "wheat", 1000) is null, "buy wheat again without the agreement");
    double withoutPrice = eng.State.TradeContracts.Last().PricePer1000;
    Check(Math.Abs(withPrice / withoutPrice - Balance.TradeAgreementImportMult) < 0.01, "imports from a partner cost 10% less");

    // Walking away stung relations below what a new agreement needs; warm them up and sign again, then war ends it.
    Check(eng.ProposeTradeAgreement(france.Id).Message.Contains("regards you too poorly"), "cancelling cost enough relations to block re-signing");
    SetRating(france, 60);
    Check(eng.ProposeTradeAgreement(france.Id).Ok, "after a warm-up the agreement can be signed again");
    eng.DeclareWar(france.Id);
    Check(!france.HasTradePactWithPlayer && !TreatyService.Has(eng.State, TreatyType.TradeAgreement, p.Id, france.Id),
        "war ends the trade agreement");
}

// Richer exports: two identical worlds, A with the agreement and B without, sell the same goods and compare treasuries.
using (var engA = NewDipEngine(8))
using (var engB = NewDipEngine(8))
{
    foreach (var eng in new[] { engA, engB }) eng.EstablishEmbassy(Ai(eng, "france").Id);
    Check(engA.ProposeTradeAgreement(Ai(engA, "france").Id).Ok, "world A signs the agreement");
    foreach (var eng in new[] { engA, engB })
    {
        eng.State.PlayerNation.GoodsInventory["Wheat"] = 2_000_000;
        Ai(eng, "france").Gold = 5_000_000;
        Check(eng.SellProduct("france", "wheat", 1_000_000, 1000) is null, "sell a million wheat");
    }
    for (int i = 0; i < 8; i++) { engA.AdvanceOneDay(); engB.AdvanceOneDay(); }
    Check(engA.State.TradeContracts.Any(x => x.Status == TradeStatus.Delivered && !x.IsPlayerBuyer)
          && engB.State.TradeContracts.Any(x => x.Status == TradeStatus.Delivered && !x.IsPlayerBuyer), "both sales were delivered");
    double diff = engA.State.PlayerNation.Gold - engB.State.PlayerNation.Gold;
    // +10% of a million, minus what A paid for its agreement (1,000), plus the small daily pact income.
    Check(diff > 98_500 && diff < 99_500, $"exports to a partner pay 10% more (treasury difference {diff:N0})");
}

using (var eng = NewDipEngine(9))
{
    // Legacy pact API stays consistent with treaties; annexation clears everything.
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var spain = Ai(eng, "spain");
    Check(eng.SignTradePact(persia.Id) is null, "legacy trade pact still signs");
    Check(TreatyService.Has(eng.State, TreatyType.TradeAgreement, p.Id, persia.Id), "the legacy pact is also a tracked treaty");
    eng.CancelTradePact(persia.Id);
    Check(!TreatyService.Has(eng.State, TreatyType.TradeAgreement, p.Id, persia.Id), "legacy cancel removes the treaty");

    eng.EstablishEmbassy(persia.Id);
    eng.SignTradePact(persia.Id);
    Warfare.AnnexNation(eng.State, spain, persia);
    Check(!eng.State.Treaties.Any(x => x.Involves(persia.Id)) && !persia.HasTradePactWithPlayer, "annexing a country ends all its treaties");
}

Console.WriteLine("== 33. Send troops: loans that never duplicate soldiers ==");
using (var eng = NewDipEngine(10))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    Check(eng.SendTroops(persia.Id, 1000).Outcome == DipOutcome.Invalid, "no troops abroad without an embassy");
    eng.EstablishEmbassy(persia.Id);
    Check(eng.SendTroops(persia.Id, 0).Outcome == DipOutcome.Invalid, "must send at least one soldier");
    Check(eng.SendTroops(persia.Id, p.Soldiers).Message.Contains("at most"), "cannot send the home guard");

    int pBefore = p.Soldiers, hBefore = persia.Soldiers;
    var typeBefore = Enum.GetValues<UnitType>().ToDictionary(t => t, t => CountOf(t, p) + CountOf(t, persia));
    int ratingBefore = Rate(persia);
    var r = eng.SendTroops(persia.Id, 2000);
    Check(r.Ok, "troops sent");
    Check(p.Soldiers == pBefore - 2000 && persia.Soldiers == hBefore + 2000, "soldiers move from one army to the other");
    Check(Enum.GetValues<UnitType>().All(t => CountOf(t, p) + CountOf(t, persia) == typeBefore[t]), "every unit type is conserved: nothing duplicated");
    Check(Rate(persia) == ratingBefore + 2, "lending 2,000 soldiers earns +2 relations");
    var loan = eng.State.TroopLoans.Single();
    Check(loan.Soldiers == 2000 && loan.ReturnDate == eng.State.CurrentDate.AddDays(Balance.TroopLoanDays), "the loan is recorded with a return date");
    Check(!ReferenceEquals(loan.Force, p.Units) && loan.Force.All(f => !p.Units.Contains(f) && !persia.Units.Contains(f)),
        "the loan record shares no objects with either army");

    for (int i = 0; i < Balance.TroopLoanDays; i++) eng.AdvanceOneDay();
    Check(eng.State.TroopLoans.Count == 0, "the loan ends on its date");
    Check(p.Soldiers == pBefore && persia.Soldiers == hBefore, "all soldiers come home");
    Check(HasLog(eng, "loaned soldiers returned"), "the return is logged");
}

using (var eng = NewDipEngine(11))
{
    // Casualties abroad are real: what is left of the lent unit type comes back, no more.
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    eng.EstablishEmbassy(persia.Id);
    eng.SendTroops(persia.Id, 2000);
    int lentMusk = eng.State.TroopLoans.Single().Force.Where(f => f.Type == UnitType.Musketeer).Sum(f => f.Count);
    persia.Units.First(u => u.Type == UnitType.Musketeer).Count = 300;   // the host lost its musketeers in battle
    int pAfterLend = p.Soldiers;
    for (int i = 0; i < Balance.TroopLoanDays; i++) eng.AdvanceOneDay();
    Check(CountOf(UnitType.Musketeer, p) == 4400 - lentMusk + 300, "only the surviving musketeers come back");
    Check(p.Soldiers < 8000 && p.Soldiers > pAfterLend, "the owner gets back less than it lent");
    Check(CountOf(UnitType.Musketeer, persia) == 0, "the host keeps nothing it does not have");
    Check(eng.State.TroopLoans.Count == 0, "the loan is closed");
}

using (var eng = NewDipEngine(12))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var spain = Ai(eng, "spain");
    var france = Ai(eng, "france");
    eng.EstablishEmbassy(persia.Id);
    eng.SendTroops(persia.Id, 2000);
    eng.DeclareWar(persia.Id);
    Check(p.Soldiers == 8000 && eng.State.TroopLoans.Count == 0, "war recalls loaned soldiers at once");

    // The host is annexed: its guests go home before its army is wiped.
    var persia2 = Ai(eng, "mughal");
    eng.EstablishEmbassy(persia2.Id);
    eng.SendTroops(persia2.Id, 1500);
    int pMid = p.Soldiers;
    Warfare.AnnexNation(eng.State, spain, persia2);
    Check(p.Soldiers == pMid + 1500 && eng.State.TroopLoans.Count == 0, "a fallen host returns its guests");

    // The owner is annexed: the loan record ends, nothing crashes, the host keeps the soldiers it holds.
    eng.EstablishEmbassy(france.Id);
    eng.SendTroops(france.Id, 1000);
    int franceArmy = france.Soldiers;
    Warfare.AnnexNation(eng.State, spain, p);
    Check(p.IsEliminated && eng.State.TroopLoans.Count == 0 && france.Soldiers == franceArmy, "a fallen owner's loan simply ends");
}

using (var eng = NewDipEngine(13))
{
    // The AI decides: a country that does not trust us enough declines, and nothing moves.
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    eng.EstablishEmbassy(france.Id);
    SetRating(france, 41);   // can ask (>=40) but 41 + embassy 5 = 46 < 50
    int before = p.Soldiers;
    var r = eng.SendTroops(france.Id, 1000);
    Check(r.Outcome == DipOutcome.Rejected && p.Soldiers == before && eng.State.TroopLoans.Count == 0 && france.Soldiers == 9000,
        "a distrustful country declines the troops and nothing is moved");
    SetRating(france, 39);
    Check(eng.SendTroops(france.Id, 1000).Outcome == DipOutcome.Invalid, "below relations 40 you cannot even offer");
}

Console.WriteLine("== 34. Give army and call to arms ==");
using (var eng = NewDipEngine(14))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    int pBefore = p.Soldiers, hBefore = persia.Soldiers;
    var typeBefore = Enum.GetValues<UnitType>().ToDictionary(t => t, t => CountOf(t, p) + CountOf(t, persia));
    int ratingBefore = Rate(persia);
    var r = eng.GiveArmy(persia.Id, 1000);
    Check(r.Ok, "army given");
    Check(p.Soldiers == pBefore - 1000 && persia.Soldiers == hBefore + 1000, "the soldiers change owner");
    Check(Enum.GetValues<UnitType>().All(t => CountOf(t, p) + CountOf(t, persia) == typeBefore[t]), "every unit type is conserved: nothing duplicated");
    Check(Rate(persia) > ratingBefore, "a gift of soldiers improves relations");
    Check(eng.State.TroopLoans.Count == 0, "a gift is permanent: no loan record");
    Check(eng.GiveArmy(persia.Id, 0).Outcome == DipOutcome.Invalid, "must give at least one soldier");

    var big = eng.GiveArmy(persia.Id, eng.SpareSoldiers + 1);
    Check(big.Outcome == DipOutcome.Invalid && big.Message.Contains("at most"), "cannot give more than the spare soldiers");
    int spare = eng.SpareSoldiers;
    Check(eng.GiveArmy(persia.Id, spare).Ok && p.Soldiers == Balance.MinHomeGuard, "the home guard is always kept");
    Check(eng.GiveArmy(persia.Id, 1).Outcome == DipOutcome.Invalid, "nothing more can be given once only the guard remains");
}

using (var eng = NewDipEngine(15))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var france = Ai(eng, "france");
    SetRating(persia, 29);
    Check(eng.GiveArmy(persia.Id, 100).Outcome == DipOutcome.Invalid, "a hostile country will not take an army");
    SetRating(persia, 50);
    int before = p.Soldiers;
    eng.DeclareWar(france.Id);
    Check(eng.GiveArmy(france.Id, 100).Outcome == DipOutcome.Invalid && p.Soldiers == before, "never arm a country you are at war with");
}

using (var eng = NewDipEngine(16))
{
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain");
    var persia = Ai(eng, "persia");
    var france = Ai(eng, "france");
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("not your defensive ally"), "only a defensive ally can be called");
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("not at war"), "nothing to join while you are at peace");
    eng.DeclareWar(persia.Id);
    Check(eng.CallToArms(spain.Id, france.Id).Message.Contains("at war with"), "the enemy must be a country you are at war with");

    // The AI says no: distrust.
    SetRating(spain, 40);   // 40 + alliance 10 = 50 < 60
    int spainBefore = spain.Soldiers;
    var no = eng.CallToArms(spain.Id, persia.Id);
    Check(no.Outcome == DipOutcome.Rejected && spain.Soldiers == spainBefore && eng.State.MarchingArmies.Count == 0,
        "a distrustful ally refuses and keeps its army home");
    p.DiplomacyCooldowns.Clear();

    // The AI says no: the enemy is too strong.
    SetRating(spain, 50);
    var persiaArmy = persia.Units;
    persia.Units = UnitCatalog.SeedArmy(100_000);
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("too strong"), "an ally will not face an enemy that dwarfs it");
    persia.Units = persiaArmy;
    p.DiplomacyCooldowns.Clear();

    // Too few soldiers to send an army at all.
    spain.Units = UnitCatalog.SeedArmy(1000);
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("too few"), "an ally with a tiny army cannot send one");
    spain.Units = UnitCatalog.SeedArmy(9000);

    // Accepted: the ally commits its OWN soldiers.
    int playerBefore = p.Soldiers;
    var yes = eng.CallToArms(spain.Id, persia.Id);
    int commit = (int)(9000 * Balance.CallToArmsFraction);
    Check(yes.Ok, "the ally joins the war");
    Check(spain.Soldiers == 9000 - commit && p.Soldiers == playerBefore, "the ally's soldiers march; the player's army is untouched");
    var march = eng.State.MarchingArmies.Single(x => x.AttackerNationId == spain.Id);
    Check(march.TargetNationId == persia.Id && march.Strength == commit, "the march is a normal tracked march on the enemy");
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("Available again"), "the ally cannot be called twice in a row");
    p.DiplomacyCooldowns.Clear();
    Check(eng.CallToArms(spain.Id, persia.Id).Message.Contains("already has an army marching"), "no second army while one is on the road");
}

using (var eng = NewDipEngine(17))
{
    // The ally annexes the enemy; the player's own army marching on the same enemy comes home exactly once.
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain");
    var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(2000);
    persia.Units = UnitCatalog.SeedArmy(1200);
    // The ally covets the enemy's lands (a populous country next door, on good terms, with little army): it wins and keeps them.
    persia.MapX = spain.MapX + 40; persia.MapY = spain.MapY; persia.Population = spain.Population;
    AiWorldService.SetRelation(eng.State, spain, persia, 30);
    Check(AiWorldService.Motives(eng.State, spain, persia).Aim == WarAim.Conquest, "(the ally's motive toward the enemy is land)");
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    eng.DeclareWar(persia.Id);
    Check(eng.CallToArms(spain.Id, persia.Id).Ok, "ally called");           // the ally's march is listed FIRST
    Check(eng.LaunchInvasion(persia.Id, 600).ok, "player's own march launched");
    foreach (var m in eng.State.MarchingArmies) m.SetRoute(eng.State.CurrentDate, 1);   // (every army arrives tomorrow)
    int soldiersBefore = p.Soldiers;
    int attrition = (int)(soldiersBefore * Balance.WarAttritionPerDay);
    long spainPop = spain.Population;
    eng.AdvanceOneDay();
    Check(persia.IsEliminated && spain.Population > spainPop, "the ally's army wins and annexes the enemy");
    Check(!persia.AtWarWithPlayer && eng.State.MarchingArmies.Count == 0, "the war ends and no march is left");
    Check(p.Soldiers == soldiersBefore - attrition + 600, "the player's force returns exactly once: no loss, no duplicate");
    Check(HasLog(eng, "Iberian Union vs Iran"), "the ally's battle is logged");
}

Console.WriteLine("== 35. Gift, improve relations, ask for aid ==");
using (var eng = NewDipEngine(18))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var france = Ai(eng, "france");
    double pg = p.Gold, tg = persia.Gold;
    int r0 = Rate(persia);
    var gift = eng.GiveGift(persia.Id, 1000);
    Check(gift.Ok && p.Gold == pg - 1000 && persia.Gold == tg + 1000, "a gift moves gold to the recipient's treasury");
    Check(Rate(persia) == r0 + 10, "1,000 gold = +10 relations");
    Check(eng.GiveGift(persia.Id, 1000).Message.Contains("Available again"), "gifts have a short cooldown");
    for (int i = 0; i < Balance.GiftCooldownDays; i++) eng.AdvanceOneDay();
    int r1 = Rate(persia);
    Check(eng.GiveGift(persia.Id, 100).Ok && Rate(persia) >= r1 + 1 - 1, "a 100-gold gift is accepted after the cooldown");
    Check(eng.GiveGift(france.Id, 99).Outcome == DipOutcome.Invalid, "a gift below the minimum is refused");
    Check(eng.GiveGift(france.Id, p.Gold + 1).Outcome == DipOutcome.Invalid, "cannot give more gold than you have");
    int rf = Rate(france);
    Check(eng.GiveGift(france.Id, 5000).Ok && Rate(france) == rf + DiplomacyService.MaxAidGainPerTransaction, "relations gained from one gift are capped");
    SetRating(Ai(eng, "mughal"), 0);
    Check(eng.GiveGift("mughal", 100).Ok, "even a hostile country accepts a gift");
    eng.DeclareWar("nepal");
    double beforeWar = p.Gold;
    Check(eng.GiveGift("nepal", 500).Outcome == DipOutcome.Invalid && p.Gold == beforeWar, "no gifts to a country at war with you");
}

using (var eng = NewDipEngine(19))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var france = Ai(eng, "france");
    double g0 = p.Gold;
    int r0 = Rate(persia);
    var r = eng.ImproveRelations(persia.Id);
    Check(r.Ok && Math.Abs(p.Gold - (g0 - Balance.ImproveRelationsCost)) < 0.01, "envoys cost their fixed price");
    Check(Rate(persia) == r0 + Balance.ImproveRelationsGain, "envoys without an embassy: +5 relations");
    Check(persia.Gold == Ai(eng, "persia").Gold && eng.ImproveRelations(persia.Id).Message.Contains("Available again"), "envoys have a cooldown");

    eng.EstablishEmbassy(france.Id);
    int f0 = Rate(france);
    eng.ImproveRelations(france.Id);
    Check(Rate(france) == f0 + Balance.ImproveRelationsGainWithEmbassy, "with an embassy the envoys are twice as effective");
    var nepal = Ai(eng, "nepal");
    SetRating(nepal, 90);
    Check(eng.ImproveRelations(nepal.Id).Message.Contains("already excellent"), "no envoys when relations are already excellent");
    p.Gold = Balance.ImproveRelationsCost - 1;
    Check(eng.ImproveRelations(Ai(eng, "mughal").Id).Message.Contains("Needs"), "cannot afford the envoys");
}

using (var eng = NewDipEngine(20))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    var persia = Ai(eng, "persia");
    double pg = p.Gold, fg = france.Gold;

    // Too cool: refused, nothing moves.
    var no = eng.AskForAid(france.Id, "Gold");
    Check(no.Outcome == DipOutcome.Rejected && p.Gold == pg && france.Gold == fg, "a neutral country refuses aid: nothing moves");
    Check(Rate(france) == 49, "the refusal stings by a point");
    Check(eng.AskForAid(france.Id, "Gold").Message.Contains("Available again"), "asking again is on cooldown");

    // Warm enough: accepted, the resource really moves.
    p.DiplomacyCooldowns.Clear();
    SetRating(france, 65);
    var yes = eng.AskForAid(france.Id, "Gold");
    double expected = Math.Floor(fg * Balance.AidRequestFraction);
    Check(yes.Ok && p.Gold == pg + expected && france.Gold == fg - expected, "aid moves 5% of their gold to the player");
    Check(Rate(france) == 65 - Balance.AidRequestRatingCost, "asking costs a little goodwill");
    Check(eng.AskForAid(france.Id, "Gold").Message.Contains("Available again"), "aid has a long cooldown");

    // A shared faith helps: 60 + 5 reaches the bar.
    SetRating(persia, 60);
    double wheat0 = persia.GetProduct("Wheat");
    var wheat = eng.AskForAid(persia.Id, "wheat");
    Check(wheat.Ok && Math.Abs(persia.GetProduct("Wheat") - (wheat0 - Math.Floor(wheat0 * 0.05))) < 0.001, "a country of the same faith helps at relations 60 (wheat)");

    // Nothing to spare / bad resource.
    var spain = Ai(eng, "spain");
    SetRating(spain, 80);
    spain.Stone = 0;
    var none = eng.AskForAid(spain.Id, "Stone");
    Check(none.Outcome == DipOutcome.Rejected && none.Message.Contains("no Stone to spare"), "a country with none of it has nothing to give");
    p.DiplomacyCooldowns.Clear();
    Check(eng.AskForAid(spain.Id, "Silver").Outcome == DipOutcome.Invalid, "only gold, wood, stone, iron or wheat can be requested");
}

Console.WriteLine("== 36. Present a colony and missionary work ==");
using (var eng = NewDipEngine(23))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    p.Warships = 5; p.Gold = 100_000; p.GoodsInventory["Wheat"] = 100_000;
    var region = eng.State.FrontierRegions[0];
    Check(eng.State.Colonies.Count == 0, "no colony records at the start");
    eng.FoundColony(region.Id);
    for (int i = 0; i < Balance.ColonyDays; i++) eng.AdvanceOneDay();
    var colony = eng.State.Colonies.Single();
    Check(colony.Name == region.Name && colony.OwnerId == p.Id && colony.FoundedById == p.Id, "a founded colony is recorded with its owner");
    Check(colony.Population == Balance.ColonyStartPopulation && colony.Farms == 8 && colony.Mines == 2, "the record holds what the colony added to the nation");
    Check(p.ColoniesFounded == 1, "the existing colony counter is unchanged");

    long pPop = p.Population, tPop = persia.Population;
    int pFarms = p.Farms, tFarms = persia.Farms, pMines = p.Mines, tMines = persia.Mines;
    int rating = Rate(persia);
    var r = eng.PresentColonyTo(persia.Id, colony.Id);
    Check(r.Ok && colony.OwnerId == persia.Id, "the colony changes owner");
    Check(p.Population == pPop - colony.Population && persia.Population == tPop + colony.Population, "its people move to the new owner");
    Check(p.Farms == pFarms - 8 && persia.Farms == tFarms + 8 && p.Mines == pMines - 2 && persia.Mines == tMines + 2, "its buildings move to the new owner");
    Check(Rate(persia) == rating + Balance.ColonyGiftRatingGain, "presenting a colony warms relations");
    Check(eng.PresentColonyTo(persia.Id, colony.Id).Outcome == DipOutcome.Invalid, "a colony you gave away is no longer yours to give");
    Check(!eng.CheckDiplomaticAction("colony", persia.Id).Available, "and the action closes when you own none");
}

using (var eng = NewDipEngine(24))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    var mine = new Colony { Name = "Mine", OwnerId = p.Id, FoundedById = p.Id, Population = 100, Farms = 1, Mines = 0 };
    var theirs = new Colony { Name = "Theirs", OwnerId = "france", FoundedById = "france", Population = 100 };
    eng.State.Colonies.Add(mine); eng.State.Colonies.Add(theirs);
    var stolen = eng.PresentColonyTo(persia.Id, theirs.Id);
    Check(stolen.Outcome == DipOutcome.Invalid && stolen.Message.Contains("do not own") && theirs.OwnerId == "france", "you cannot present a colony you do not own");
    Check(eng.PresentColonyTo(persia.Id, "missing").Outcome == DipOutcome.Invalid, "unknown colony id");
    p.Farms = 0;
    Check(eng.PresentColonyTo(persia.Id, mine.Id).Message.Contains("no longer all yours") && mine.OwnerId == p.Id, "a colony whose buildings are gone cannot be handed over");
    p.Farms = 50;
    eng.DeclareWar(persia.Id);
    Check(eng.PresentColonyTo(persia.Id, mine.Id).Outcome == DipOutcome.Invalid, "no colonies to a country at war with you");

    // An old save: colonies were counted but never recorded.
    p.ColoniesFounded = 3; eng.State.Colonies.Clear();
    Check(!eng.CheckDiplomaticAction("colony", Ai(eng, "france").Id).Available, "colonies founded before records existed cannot be presented");
}

using (var eng = NewDipEngine(25))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");   // Christianity vs the Ottoman Islam
    double g0 = p.Gold;
    var r = eng.SendMissionary(france.Id);
    Check(r.Ok && Math.Abs(p.Gold - (g0 - Balance.MissionaryCost)) < 0.01, "a mission costs its price");
    Check(Math.Abs(eng.MissionaryInfluenceOn(france.Id) - Balance.MissionaryInfluencePerMission) < 0.001, "a pragmatic country gains the base influence");
    Check(france.Religion == "Christianity", "one mission does not convert");
    Check(eng.SendMissionary(france.Id).Message.Contains("Available again"), "missions have a cooldown");
    for (int i = 0; i < Balance.MissionaryCooldownDays; i++) eng.AdvanceOneDay();
    eng.SendMissionary(france.Id);
    Check(Math.Abs(eng.MissionaryInfluenceOn(france.Id) - 2 * Balance.MissionaryInfluencePerMission) < 0.001, "influence accumulates between missions");

    // Stance and embassy change the effect.
    var spain = Ai(eng, "spain"); spain.Stance = ReligiousStance.Devout;
    int sr = Rate(spain);
    eng.SendMissionary(spain.Id);
    Check(Math.Abs(eng.MissionaryInfluenceOn(spain.Id) - Balance.MissionaryInfluencePerMission * 0.5) < 0.001, "a devout ruler halves the influence");
    Check(Rate(spain) == sr - Balance.MissionaryDevoutRatingPenalty, "and resents the missionaries");
    var england = Ai(eng, "england"); england.Stance = ReligiousStance.Tolerant;
    eng.SendMissionary(england.Id);
    Check(Math.Abs(eng.MissionaryInfluenceOn(england.Id) - Balance.MissionaryInfluencePerMission * 1.5) < 0.001, "a tolerant ruler welcomes it (x1.5)");
    var dutch = Ai(eng, "dutch");
    eng.EstablishEmbassy(dutch.Id);
    eng.SendMissionary(dutch.Id);
    Check(Math.Abs(eng.MissionaryInfluenceOn(dutch.Id) - Balance.MissionaryInfluencePerMission * 1.5) < 0.001, "an embassy helps the missionaries (x1.5)");
}

using (var eng = NewDipEngine(26))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france");
    Check(Math.Abs(ReligionService.SellingPriceMult(france) - 1.05) < 1e-9 && ReligionService.PopulationGrowthBonus(france) == 0, "France starts with the Christian bonus only");
    eng.State.MissionaryInfluence[france.Id] = 90;
    int rating = Rate(france);
    var r = eng.SendMissionary(france.Id);
    Check(r.Ok && r.Message.Contains("adopts") && france.Religion == "Islam", "enough influence converts the country to the player's faith");
    Check(!eng.State.MissionaryInfluence.ContainsKey(france.Id), "the influence is spent");
    Check(Rate(france) == rating + Balance.MissionaryConversionRatingGain, "conversion warms relations");
    Check(ReligionService.PopulationGrowthBonus(france) == 0.005 && Math.Abs(ReligionService.SellingPriceMult(france) - 1.0) < 1e-9,
        "the existing religion effects now apply to the converted country");
    Check(!eng.CheckDiplomaticAction("missionary", france.Id).Available, "no missionaries to a country that already shares the faith");

    var ming = Ai(eng, "ming");   // Confucianism is not in the catalogue: it can still be converted away from
    Check(eng.SendMissionary(ming.Id).Ok && eng.MissionaryInfluenceOn(ming.Id) > 0, "a country of an unlisted faith can be worked on");
    var micronesia = Ai(eng, "micronesia"); micronesia.Religion = "";
    Check(eng.SendMissionary(micronesia.Id).Message.Contains("no state religion"), "a country without a religion cannot be converted");

    // The player's own faith must exist in the religion catalogue.
    double g = p.Gold;
    p.Religion = "Shinto";
    var blocked = eng.CheckDiplomaticAction("missionary", Ai(eng, "spain").Id);
    Check(!blocked.Available && blocked.Reason.Contains("missionary doctrine"), "a faith outside the catalogue cannot send missionaries");
    Check(eng.SendMissionary(Ai(eng, "spain").Id).Outcome == DipOutcome.Invalid && p.Gold == g, "and nothing is spent trying");
    p.Religion = "Islam";
    var mughal = Ai(eng, "kazakh");
    SetRating(mughal, 20);
    Check(eng.CheckDiplomaticAction("missionary", Ai(eng, "spain").Id).Available, "back to a catalogued faith: allowed again");
    mughal.Religion = "Hinduism";
    Check(eng.SendMissionary(mughal.Id).Message.Contains("regards you too poorly"), "a hostile country expels missionaries");
}

Console.WriteLine("== 37. Save / load of treaties, loans, colonies, influence and cooldowns ==");
using (var eng = NewDipEngine(2))
{
    var p = eng.State.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var start = eng.State.CurrentDate;
    eng.EstablishEmbassy(france.Id);
    eng.ProposeNonAggression(france.Id);
    Check(eng.ProposeTradeAgreement(france.Id).Ok, "trade agreement signed (embassy + pact goodwill)");
    eng.EstablishEmbassy(persia.Id);
    eng.SendTroops(persia.Id, 1500);
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    eng.State.Colonies.Add(new Colony { Name = "Saved", OwnerId = persia.Id, FoundedById = p.Id, Population = 5000, Farms = 8, Mines = 2, FoundedDate = start });
    eng.State.MissionaryInfluence[france.Id] = 45;
    eng.GiveGift(spain.Id, 500);
    await eng.SaveAsync();

    // Wreck the live state, then load.
    eng.State.Treaties.Clear(); eng.State.TroopLoans.Clear(); eng.State.Colonies.Clear();
    eng.State.MissionaryInfluence.Clear(); p.DiplomacyCooldowns.Clear(); france.HasTradePactWithPlayer = false;
    await eng.LoadAsync();

    var s = eng.State; var lp = s.PlayerNation;
    Check(s.Treaties.Count == 5, "all five treaties are restored");
    Check(s.Treaties.Count(x => x.Type == TreatyType.Embassy) == 2 && s.Treaties.Any(x => x.Type == TreatyType.DefensiveAlliance)
          && s.Treaties.Any(x => x.Type == TreatyType.TradeAgreement), "treaty types survive the round trip");
    Check(s.Treaties.Single(x => x.Type == TreatyType.NonAggression).ExpiresDate == start.AddDays(Balance.NapDays), "the pact's expiry date survives");
    Check(TreatyService.HasEmbassy(s, lp.Id, "france") && !TreatyService.HasEmbassy(s, "france", lp.Id), "embassy direction survives");
    Check(s.TroopLoans.Single().Soldiers == 1500 && s.TroopLoans.Single().ReturnDate == start.AddDays(Balance.TroopLoanDays), "the troop loan survives");
    Check(s.Colonies.Single().OwnerId == "persia" && s.Colonies.Single().Population == 5000, "colony ownership survives");
    Check(Math.Abs(s.MissionaryInfluence["france"] - 45) < 0.001, "missionary influence survives");
    Check(lp.DiplomacyCooldowns.ContainsKey("gift_spain"), "cooldowns survive");
    Check(s.OtherNations.First(x => x.Id == "france").HasTradePactWithPlayer, "the pact flag survives");

    // And the loaded treaties are live, not just present.
    var blocked = eng.DeclareWar("france");
    Check(blocked is not null && blocked.Contains("non-aggression"), "a loaded pact still forbids war");
    int soldiers = lp.Soldiers;
    for (int i = 0; i < Balance.TroopLoanDays; i++) { lp.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(eng.State.TroopLoans.Count == 0 && lp.Soldiers == soldiers + 1500, "a loaded loan comes home on schedule");

    // Old saves without any of the new fields still load, and a legacy pact flag becomes a treaty.
    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    foreach (var key in new[] { "Treaties", "TroopLoans", "Colonies", "MissionaryInfluence" }) node.Remove(key);
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.Treaties.Count == 0 && old.TroopLoans.Count == 0 && old.Colonies.Count == 0 && old.MissionaryInfluence.Count == 0,
        "an old save without the new fields loads with empty collections");
    foreach (var n in node["OtherNations"]!.AsArray())
        if (n!["Id"]!.GetValue<string>() == "persia") n["HasTradePactWithPlayer"] = true;
    Directory.CreateDirectory(saveFolder);
    File.WriteAllText(Path.Combine(saveFolder, "savegame.json"), node.ToJsonString());
    await eng.LoadAsync();
    Check(TreatyService.Has(eng.State, TreatyType.TradeAgreement, eng.State.PlayerNation.Id, "persia"), "a legacy trade-pact flag is given its treaty on load");
}

Console.WriteLine("== 38. Diplomacy leaves population, tax, shortage and production rules alone ==");
using (var engA = NewDipEngine(77))
using (var engB = NewDipEngine(77))
{
    // World A uses every non-hostile action; world B does nothing. Gold differs, nothing else about the nation may.
    var pA = engA.State.PlayerNation;
    var france = Ai(engA, "france"); var persia = Ai(engA, "persia");
    engA.EstablishEmbassy(france.Id); engA.ProposeNonAggression(france.Id); engA.ProposeTradeAgreement(france.Id);
    engA.GiveGift(persia.Id, 500); engA.ImproveRelations(persia.Id); engA.SendMissionary(france.Id);
    for (int i = 0; i < 60; i++) { engA.AdvanceOneDay(); engB.AdvanceOneDay(); }
    var pB = engB.State.PlayerNation;
    Check(pA.Population == pB.Population, "population is unaffected by diplomacy");
    Check(pA.Soldiers == pB.Soldiers && pA.Workforce.Peasants == pB.Workforce.Peasants, "army and workforce are unaffected");
    Check(pA.GetProduct("Wheat") == pB.GetProduct("Wheat") && pA.GetProduct("Wood") == pB.GetProduct("Wood"), "production and stocks are unaffected");
    Check(pA.RulerRating == pB.RulerRating && pA.TaxApproval == pB.TaxApproval, "ruler rating and tax approval are unaffected");
    Check(pA.LastShortageDeaths == pB.LastShortageDeaths, "shortage effects are unaffected");
}

Console.WriteLine("== 39. Movement report: every movement between states is recorded ==");
using (var eng = NewDipEngine(31))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia"); var france = Ai(eng, "france"); var nepal = Ai(eng, "nepal");
    bool Has(MovementKind kind, MovementStatus status, string from, string to) =>
        eng.State.Movements.Any(m => m.Kind == kind && m.Status == status && m.FromId == from && m.ToId == to);

    Check(eng.State.Movements.Count == 0, "a new game has no movements yet");
    Check(MovementReport.Events(eng.State).Count == 0 && MovementReport.ActiveMissions(eng.State).Count == 0, "nothing to report at the start");

    eng.GiveGift(persia.Id, 1000);
    var gift = eng.State.Movements.Last();
    Check(gift.Kind == MovementKind.Gold && gift.Status == MovementStatus.Completed && gift.FromId == p.Id && gift.ToId == persia.Id
          && gift.FromName == p.Name && gift.ToName == persia.Name && gift.Date == eng.State.CurrentDate, "a gift is recorded: gold from us to them, with date and names");
    Check(HasLog(eng, gift.Text), "the same line is still in the event log");

    eng.EstablishEmbassy(persia.Id);
    Check(Has(MovementKind.Mission, MovementStatus.Completed, p.Id, persia.Id), "an embassy is recorded as a mission");
    Check(eng.ProposeNonAggression(persia.Id).Ok && Has(MovementKind.Treaty, MovementStatus.Completed, p.Id, persia.Id), "a pact is recorded as a treaty");
    Check(HasLog(eng, "Signed an embassy with Iran.") && HasLog(eng, "Signed a non-aggression pact with Iran."), "records read naturally (an embassy, a pact)");
    eng.SendTroops(persia.Id, 1000);
    Check(Has(MovementKind.Troops, MovementStatus.UnderWay, p.Id, persia.Id), "lent soldiers are recorded as under way");
    eng.GiveArmy(france.Id, 500);
    Check(Has(MovementKind.Troops, MovementStatus.Completed, p.Id, france.Id), "given soldiers are recorded as done");
    eng.ImproveRelations(france.Id);
    Check(Has(MovementKind.Mission, MovementStatus.Completed, p.Id, france.Id), "envoys are recorded as a mission");
    var refusedAid = eng.AskForAid(france.Id, "Gold");
    Check(refusedAid.Outcome == DipOutcome.Rejected && Has(MovementKind.Gold, MovementStatus.Failed, france.Id, p.Id), "a refused request for aid is recorded as refused");
    SetRating(france, 65); p.DiplomacyCooldowns.Clear();
    Check(eng.AskForAid(france.Id, "Gold").Ok && Has(MovementKind.Gold, MovementStatus.Completed, france.Id, p.Id), "aid received is recorded: gold from them to us");
    eng.SendMissionary(france.Id);
    Check(Has(MovementKind.Mission, MovementStatus.Completed, p.Id, france.Id) && eng.State.Movements.Last().Text.Contains("influence"), "missionaries are recorded");
    eng.EstablishNetwork(france.Id);
    Check(eng.State.Movements.Last().Text.Contains("Spy network"), "a spy network is recorded");

    // Trade shipments: placed under way, delivered later.
    int before = eng.State.Movements.Count;
    Embassy(eng, france);
    eng.BuyProduct(france.Id, "wheat", 1000);
    Check(Has(MovementKind.Goods, MovementStatus.UnderWay, france.Id, p.Id), "a purchase is recorded: goods from them to us, under way");
    eng.SellProduct(france.Id, "wheat", 100, 500);
    Check(Has(MovementKind.Goods, MovementStatus.UnderWay, p.Id, france.Id), "a sale is recorded: goods from us to them, under way");
    for (int i = 0; i < 8; i++) eng.AdvanceOneDay();
    Check(Has(MovementKind.Goods, MovementStatus.Completed, france.Id, p.Id) && Has(MovementKind.Goods, MovementStatus.Completed, p.Id, france.Id),
        "both shipments are recorded again when they arrive");

    // War and marches.
    eng.DeclareWar(nepal.Id);
    Check(Has(MovementKind.War, MovementStatus.Completed, p.Id, nepal.Id), "a declaration of war is recorded");
    var (okInvade, _) = eng.LaunchInvasion(nepal.Id, 600);
    Check(okInvade && Has(MovementKind.March, MovementStatus.UnderWay, p.Id, nepal.Id), "a launched invasion is recorded as under way");
    int marchDays = eng.State.MarchingArmies[0].TotalDays;
    for (int i = 0; i < marchDays; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(Has(MovementKind.March, MovementStatus.Completed, p.Id, nepal.Id), "the battle on arrival is recorded as done");

    // Colonies.
    p.Warships = 5; p.Gold = 100_000; p.GoodsInventory["Wheat"] = 100_000;
    var region = eng.State.FrontierRegions[0];
    eng.FoundColony(region.Id);
    Check(Has(MovementKind.Colony, MovementStatus.UnderWay, p.Id, region.Id), "an expedition is recorded: us to the region, under way");
    for (int i = 0; i < Balance.ColonyDays; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(Has(MovementKind.Colony, MovementStatus.Completed, p.Id, region.Id), "the founding is recorded when the colony is established");
    var spainC = Ai(eng, "mughal");
    eng.PresentColonyTo(spainC.Id, eng.State.Colonies[0].Id);
    Check(Has(MovementKind.Colony, MovementStatus.Completed, p.Id, spainC.Id), "presenting a colony is recorded");

    // Loans come home.
    for (int i = 0; i < Balance.TroopLoanDays; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(Has(MovementKind.Troops, MovementStatus.Completed, persia.Id, p.Id), "the return of lent soldiers is recorded: from the host back to us");
    Check(eng.State.Movements.All(m => m.FromName.Length > 0 && m.ToName.Length > 0 && m.Text.Length > 0), "every record has names and text");
}

using (var eng = NewDipEngine(21))
{
    // AI-initiated movements are recorded too.
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    for (int i = 0; i < 600 && !persia.AtWarWithPlayer; i++) { persia.RelationToPlayer = -100; p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.War && m.FromId == persia.Id && m.ToId == p.Id), "an AI declaration of war on us is recorded");
}

using (var eng = NewDipEngine(34))
{
    // Allied marches, recalls, treaty expiry, tribute and annexation.
    var p = eng.State.PlayerNation;
    var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    TreatyService.Add(eng.State, TreatyType.DefensiveAlliance, p, spain);
    TreatyService.AllianceDefence(eng.State, persia, p);
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && m.FromId == spain.Id && m.ToId == persia.Id),
        "an ally's march against our attacker is recorded");
    persia.AtWarWithPlayer = false;   // peace: the ally's march is called off on arrival
    int allyDays = eng.State.MarchingArmies[0].TotalDays;
    for (int i = 0; i < allyDays; i++) eng.AdvanceOneDay();
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.March && m.Status == MovementStatus.Failed && m.FromId == spain.Id && m.ToId == persia.Id),
        "an ally's recalled march is recorded as failed");

    p.Units = UnitCatalog.SeedArmy(20000);
    france.Units = UnitCatalog.SeedArmy(5000);
    Check(eng.DemandTribute(france.Id) is null && eng.State.Movements.Any(m => m.Kind == MovementKind.Gold && m.Status == MovementStatus.Completed && m.FromId == france.Id && m.ToId == p.Id),
        "tribute paid to us is recorded");
    Warfare.AnnexNation(eng.State, p, france);
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.War && m.FromId == p.Id && m.ToId == france.Id && m.Text.Contains("annexed")), "an annexation is recorded");
}

using (var eng = NewDipEngine(35))
{
    var p = eng.State.PlayerNation;
    var persia = Ai(eng, "persia");
    eng.EstablishEmbassy(persia.Id);
    eng.ProposeNonAggression(persia.Id);
    for (int i = 0; i < Balance.NapDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.Treaty && m.Text.Contains("has expired")), "an expired pact is recorded");
}

Console.WriteLine("== 40. Movement report: events, missions, filters, cap, save/load ==");
using (var eng = NewDipEngine(36))
{
    var s = eng.State; var p = s.PlayerNation;
    var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    var d0 = s.CurrentDate;
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, p, persia, "a");
    s.CurrentDate = d0.AddDays(1);
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, persia, p, "b");
    s.LogMovement(MovementKind.March, MovementStatus.Completed, spain, france, "c");   // neither side is the player, nor the player's ally
    Check(string.Join("", MovementReport.Events(s).Select(x => x.Text)) == "ba" && s.Movements.Any(x => x.Text == "c"),
        "a movement between two other states is world news: recorded, but not in the player's report");

    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    s.LogMovement(MovementKind.March, MovementStatus.Completed, spain, france, "d");   // the player's ally fights
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, spain, france, "e");    // an ally's gift to a third state is not the player's business

    Check(string.Join("", MovementReport.Events(s).Select(x => x.Text)) == "dba", "events are newest first, even within one day; an ally's war is included");
    Check(string.Join("", MovementReport.Events(s, persia.Id).Select(x => x.Text)) == "ba", "filtering by a state keeps only movements that involve it");
    Check(string.Join("", MovementReport.Events(s, spain.Id).Select(x => x.Text)) == "d" && MovementReport.Events(s, france.Id).Count == 1,
        "both sides of an ally's war see it");
    Check(MovementReport.Events(s, Ai(eng, "mughal").Id).Count == 0, "a state with no movements has an empty list");

    var groups = MovementReport.EventsByState(s);
    Check(groups.Count == 2 && groups[0].StateId == spain.Id && groups[1].StateId == persia.Id, "by state: grouped under the state concerned, newest news first");
    Check(string.Join("", groups[1].Items.Select(x => x.Text)) == "ba" && groups[0].Items.Count == 1, "by state: each group lists its events newest first");
    Check(MovementReport.EventsByState(s, persia.Id).Single().StateId == persia.Id, "by state with a filter shows just that state");
    Check(MovementReport.Counterpart(s, s.Movements[0]).Id == persia.Id && MovementReport.Counterpart(s, s.Movements[1]).Id == persia.Id,
        "the counterpart is the other side from the player's point of view");
}

using (var eng = NewDipEngine(37))
{
    // Mission location: everything on the road or posted abroad right now.
    var s = eng.State; var p = s.PlayerNation;
    var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    Check(MovementReport.ActiveMissions(s).Count == 0, "no missions at the start");

    eng.EstablishEmbassy(persia.Id);
    eng.EstablishNetwork(france.Id);
    eng.SendMissionary(spain.Id);
    eng.SendTroops(persia.Id, 1500);
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    var allyMarch = TreatyService.LaunchMarch(s, spain, persia, 800)!;
    var worldMarch = TreatyService.LaunchMarch(s, Ai(eng, "mughal"), Ai(eng, "japan"), 800)!;   // two other countries: not the player's business
    Embassy(eng, france);
    eng.BuyProduct(france.Id, "wheat", 1000);
    p.Warships = 5; p.Gold = 100_000; p.GoodsInventory["Wheat"] = 100_000;
    eng.FoundColony(s.FrontierRegions[0].Id);

    var all = MovementReport.ActiveMissions(s);
    Check(s.MarchingArmies.Contains(worldMarch) && !all.Any(x => x.FromId == worldMarch.AttackerNationId), "(an army marching between two other countries is under way, but not listed)");
    worldMarch.SetRoute(s.CurrentDate, 1);   // (it is recalled tomorrow, so it does not outlast the rest of this test)
    Check(all.Count == 8, "two embassies (Iran, and France for the trade), spy network, missionaries, loan, an ally's march, shipment and expedition are all listed");
    var embassy = all.Single(x => x.Text.StartsWith("Embassy") && x.LocationId == persia.Id);
    var spy = all.Single(x => x.Text.StartsWith("Spy network"));
    var mission = all.Single(x => x.Text.StartsWith("Missionaries"));
    Check(embassy.LocationId == persia.Id && spy.LocationId == france.Id && mission.LocationId == spain.Id, "stationed missions say where they are");
    Check(embassy.Due is null && embassy.Progress is null && spy.DaysLeft is null && mission.Due is null, "stationed missions have no end date");
    var loan = all.Single(x => x.Kind == MovementKind.Troops);
    Check(loan.FromId == p.Id && loan.LocationId == persia.Id && loan.DaysLeft == Balance.TroopLoanDays && loan.Due == s.CurrentDate.AddDays(Balance.TroopLoanDays)
          && loan.Progress == 0 && loan.Text.Contains("1,500"), "a troop loan shows who lent how many to whom, and when they return");
    var march = all.Single(x => x.Kind == MovementKind.March);
    Check(march.FromId == spain.Id && march.LocationId == persia.Id && march.DaysLeft == allyMarch.DaysLeft && march.Text.Contains("800"), "a marching army shows where it is going and when it arrives");
    var ship = all.Single(x => x.Kind == MovementKind.Goods);
    Check(ship.FromId == france.Id && ship.LocationId == p.Id && ship.DaysLeft is >= 3 and <= 7 && ship.Text.Contains("Wheat"), "a shipment in transit shows its goods and delivery date");
    var exp = all.Single(x => x.Kind == MovementKind.Colony);
    Check(exp.DaysLeft == Balance.ColonyDays && exp.LocationName == s.ActiveExpedition!.RegionName, "the colony expedition shows its destination and days left");
    var dues = all.Select(x => x.Due ?? DateOnly.MaxValue).ToList();
    Check(dues.SequenceEqual(dues.OrderBy(x => x)), "missions are ordered by arrival, stationed ones last");

    Check(MovementReport.ActiveMissions(s, persia.Id).Count == 3, "filtering by a state shows what is in it (embassy, loan, ally march)");
    Check(MovementReport.ActiveMissions(s, spain.Id).Count == 2, "...and what it sent (missionaries there, its own army marching)");
    var byState = MovementReport.MissionsByState(s);
    Check(byState.Single(x => x.StateId == persia.Id).Items.Count == 3 && byState.Sum(x => x.Items.Count) == 8, "missions group by where they are");

    // They leave the list once they are over.
    for (int i = 0; i < 8; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(!MovementReport.ActiveMissions(s).Any(x => x.Kind == MovementKind.Goods), "a delivered shipment leaves the mission list");
    for (int i = 8; i < Balance.TroopLoanDays; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(s.MarchingArmies.Count == 0 && !MovementReport.ActiveMissions(s).Any(x => x.Kind == MovementKind.March), "a finished (here: recalled) march leaves the mission list");
    Check(!MovementReport.ActiveMissions(s).Any(x => x.Kind is MovementKind.Troops or MovementKind.Colony), "the loan and the expedition leave the list when they end");
    Check(MovementReport.ActiveMissions(s).Count(x => x.Text.StartsWith("Embassy") || x.Text.StartsWith("Spy") || x.Text.StartsWith("Missionaries")) == 4,
        "stationed missions stay until they end");
}

using (var eng = NewDipEngine(38))
{
    // The record is capped, and survives save / load (old saves without it load empty).
    var s = eng.State;
    int total = GameState.MaxMovements + 100;
    for (int i = 0; i < total; i++) s.LogMovement(MovementKind.Gold, MovementStatus.Completed, "a", "A", "b", "B", $"m{i}");
    Check(s.Movements.Count == GameState.MaxMovements && s.Movements[0].Text == "m100" && s.Movements[^1].Text == $"m{total - 1}",
        $"only the newest {GameState.MaxMovements:N0} movements are kept");

    // At the cap, world news is dropped before anything that concerned the player.
    s.Movements.Clear();
    var p = s.PlayerNation; var persia = Ai(eng, "persia");
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, p, persia, "mine");
    for (int i = 0; i < GameState.MaxMovements + 50; i++) s.LogMovement(MovementKind.Gold, MovementStatus.Completed, "a", "A", "b", "B", $"world{i}");
    Check(s.Movements.Count == GameState.MaxMovements && s.Movements.Any(x => x.Text == "mine") && MovementReport.Events(s).Single().Text == "mine",
        "past the cap, world news goes first: the player's oldest record is still there");

    s.Movements.Clear();
    eng.GiveGift(persia.Id, 500);
    eng.EstablishEmbassy(persia.Id);
    s.LogMovement(MovementKind.War, MovementStatus.Completed, Ai(eng, "spain"), Ai(eng, "france"), "linked", playerLinked: true);
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, Ai(eng, "spain"), Ai(eng, "france"), "world");
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, Ai(eng, "spain"), Ai(eng, "france"), "spoils", military: true);
    var saved = s.Movements.Select(x => (x.Kind, x.Status, x.Date, x.FromId, x.ToId, x.Text, x.PlayerLinked, x.Military)).ToList();
    await eng.SaveAsync();
    eng.State.Movements.Clear();
    await eng.LoadAsync();
    var loaded = eng.State.Movements.Where(x => !x.Text.Contains("Game saved") && !x.Text.Contains("Save loaded")).Select(x => (x.Kind, x.Status, x.Date, x.FromId, x.ToId, x.Text, x.PlayerLinked, x.Military)).ToList();
    Check(saved.Count == 5 && loaded.SequenceEqual(saved) && loaded.Single(x => x.Text == "linked").PlayerLinked && !loaded.Single(x => x.Text == "world").PlayerLinked && loaded.Single(x => x.Text == "spoils").Military,
        "movement records survive save / load unchanged, including whether they concern the player and whether they are a result of war");

    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    node.Remove("Movements");
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.Movements.Count == 0, "an old save without movements loads with an empty report");
}

Console.WriteLine("== 41. Movement report: assembly flow, and every kind has an icon ==");
Check(Enum.GetValues<MovementKind>().All(k => MovementReport.Icon(k) != "•" && MovementReport.KindName(k).Length > 0),
    "every kind of movement has an icon and a name");
using (var eng = NewDipEngine(41))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    Check(eng.SubmitProposal("military_restriction", persia.Id, 60, 30) is null, "proposal submitted");
    var submitted = s.Movements.Last();
    Check(submitted.Kind == MovementKind.Assembly && submitted.Status == MovementStatus.UnderWay && submitted.FromId == p.Id
          && submitted.ToId == persia.Id && submitted.Text.Contains("Iran"), "a proposal is recorded as under way against its target");

    // Everyone but the target backs it: approved on the deadline.
    var prop = s.AssemblyProposals.Single();
    foreach (var n in s.AllNations().Where(n => n.Id != persia.Id)) prop.Votes[n.Id] = true;
    for (int i = 0; i < 30; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var approved = s.Movements.Last(m => m.Kind == MovementKind.Assembly);
    Check(approved.Status == MovementStatus.Completed && approved.Text.StartsWith("Assembly APPROVED") && approved.Text.Contains("Iran")
          && approved.FromId == p.Id && approved.ToId == persia.Id, "an approved proposal is recorded as done, naming the country (not its id)");

    for (int i = 0; i < 60; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var expired = s.Movements.Last(m => m.Kind == MovementKind.Assembly);
    Check(expired.Status == MovementStatus.Completed && expired.Text.StartsWith("Assembly policy expired") && expired.Text.Contains("Iran") && expired.ToId == persia.Id,
        "the policy expiring is recorded");
}

using (var eng = NewDipEngine(42))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    eng.SubmitProposal("military_restriction", persia.Id, 60, 30);
    var prop = s.AssemblyProposals.Single();
    foreach (var n in s.AllNations().Where(n => n.Id != persia.Id && n.Id != p.Id)) prop.Votes[n.Id] = false;
    for (int i = 0; i < 30; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var rejected = s.Movements.Last(m => m.Kind == MovementKind.Assembly);
    Check(rejected.Status == MovementStatus.Failed && rejected.Text.StartsWith("Assembly REJECTED") && rejected.ToId == persia.Id,
        "a rejected proposal is recorded as refused");
    Check(MovementReport.Events(s, persia.Id).Count(m => m.Kind == MovementKind.Assembly) == 2, "both assembly records show under the target's state");
}

Console.WriteLine("== 42. Allied assistance: the troops come out of the ally's army ==");
using (var eng = NewDipEngine(51))
{
    var p = eng.State.PlayerNation; var spain = Ai(eng, "spain");
    spain.Warships = 500;   // warships must not inflate the contingent (the old rule counted 100 power each)
    SetRating(spain, 80);
    int pBefore = p.Soldiers, sBefore = spain.Soldiers;
    var typeBefore = Enum.GetValues<UnitType>().ToDictionary(t => t, t => CountOf(t, p) + CountOf(t, spain));
    int ratingBefore = Rate(spain);
    string msg = eng.RequestAlliedAssistance(spain.Id);
    int expected = (int)(sBefore * Balance.AlliedHelpFraction);
    Check(msg.Contains("sends") && p.Soldiers == pBefore + expected, "the player receives 10% of the ally's SOLDIERS (not its warship power)");
    Check(spain.Soldiers == sBefore - expected, "the ally loses exactly what the player gains");
    Check(p.Soldiers + spain.Soldiers == pBefore + sBefore, "no soldiers are created");
    Check(Enum.GetValues<UnitType>().All(t => CountOf(t, p) + CountOf(t, spain) == typeBefore[t]), "every unit type is conserved: moved, not copied");
    Check(Rate(spain) == ratingBefore - Balance.AlliedHelpRatingCost, "asking costs a little goodwill");
    Check(eng.State.Movements.Any(m => m.Kind == MovementKind.Troops && m.Status == MovementStatus.Completed && m.FromId == spain.Id && m.ToId == p.Id),
        "the transfer is in the Movement Report");
    Check(eng.RequestAlliedAssistance(spain.Id).Contains("Available again"), "asking again at once is on cooldown (no more infinite troops)");

    for (int i = 0; i < Balance.AlliedHelpCooldownDays; i++) eng.AdvanceOneDay();
    SetRating(spain, 80);
    int s2 = spain.Soldiers, p2 = p.Soldiers, again = (int)(s2 * Balance.AlliedHelpFraction);
    Check(eng.RequestAlliedAssistance(spain.Id).Contains("sends") && spain.Soldiers == s2 - again && p.Soldiers == p2 + again,
        "after the cooldown the ally can be asked again, and shrinks again");

    var france = Ai(eng, "france");
    SetRating(france, 69);
    int f0 = france.Soldiers, pp = p.Soldiers;
    Check(eng.RequestAlliedAssistance(france.Id).Contains("relations too low") && france.Soldiers == f0 && p.Soldiers == pp, "below relations 70 nothing moves");
    Check(eng.RequestAlliedAssistance("nowhere") == "Nation not found.", "unknown country");
    var persia = Ai(eng, "persia");
    SetRating(persia, 90);
    persia.Units = new List<UnitStack> { new() { Type = UnitType.Musketeer, Count = 5 } };
    Check(eng.RequestAlliedAssistance(persia.Id).Contains("no troops to spare"), "an ally with a token army has nothing to spare");
}

Console.WriteLine("== 43. Annexation protection: Assembly ban and sovereignty guarantee ==");
using (var eng = NewDipEngine(52))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null, "nothing protects a country at the start");

    // The Assembly passes an annexation ban.
    eng.SubmitProposal("annexation_ban", persia.Id, 120, 30);
    foreach (var n in s.AllNations().Where(n => n.Id != persia.Id)) s.AssemblyProposals.Single().Votes[n.Id] = true;
    for (int i = 0; i < 30; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var block = TreatyService.AnnexationBlock(s, persia.Id);
    Check(block is not null && block.Contains("Assembly") && block.Contains(s.CurrentDate.AddDays(120).ToString("dd-MM-yyyy")), "the passed ban protects the country, naming the end date");

    p.Units = UnitCatalog.SeedArmy(6000);
    persia.Units = UnitCatalog.SeedArmy(3500);
    eng.DeclareWar(persia.Id);
    var (okLaunch, launchMsg) = eng.LaunchInvasion(persia.Id, 3000);
    Check(!okLaunch && launchMsg.Contains("forbidden annexing") && s.MarchingArmies.Count == 0 && p.Soldiers == 6000, "an invasion of a protected country is refused and no soldiers leave");
    Check(eng.CheckDiplomaticAction("annex", persia.Id).Reason.Contains("Win a battle"), "without a victory the Annex button explains that winning earns the choice");

    // A march already on the road when the ban begins: the battle is won, but the country cannot be annexed.
    s.ActiveAssemblyPolicies.Clear();
    Check(eng.LaunchInvasion(persia.Id, 3000).ok, "invasion launched while the country is unprotected");
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "annexation_ban", TargetId = persia.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(120) });
    int days = s.MarchingArmies[0].TotalDays;
    for (int i = 0; i < days; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(!persia.IsEliminated && persia.AtWarWithPlayer && eng.PendingVictory(persia.Id) is not null, "the battle is won and the decision waits");
    var refused = eng.ResolveVictory(persia.Id, VictoryChoice.Annex);
    Check(refused.Outcome == DipOutcome.Invalid && refused.Message.Contains("forbidden annexing") && !persia.IsEliminated && eng.PendingVictory(persia.Id) is not null,
        "annexing a protected country is refused and the decision stays open");
    Check(persia.Soldiers < 3500, "the battle itself still happened (casualties stand)");
    double goldBefore = p.Gold;
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Resources).Ok && p.Gold > goldBefore && !persia.IsEliminated && !persia.AtWarWithPlayer,
        "the other choices still work: resources are taken, the country survives, the war ends");

    // Once the protection ends the same country can be annexed after a new victory.
    s.ActiveAssemblyPolicies.Clear();
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null, "with the ban gone nothing protects it");
    eng.DeclareWar(persia.Id);
    Check(eng.LaunchInvasion(persia.Id, 3000).ok, "invasion launched again");
    int days2 = s.MarchingArmies[0].TotalDays;
    for (int i = 0; i < days2; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Annex).Ok && persia.IsEliminated, "unprotected, the defeated country can be annexed");
}

using (var eng = NewDipEngine(53))
{
    // A sovereignty guarantee binds the guarantor in peacetime, but it is not an annexation block: what a victor does with a beaten
    // country is the victor's explicit choice, so a guarantee signed before the war does not override it.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, p, persia, Balance.SovereigntyDays);
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null, "a guaranteed country is not blocked from annexation by the guarantee");
    var war = eng.DeclareWar(persia.Id);
    Check(war is not null && war.Contains("guaranteed") && !persia.AtWarWithPlayer, "the guarantor still cannot declare war on the country it guaranteed");
    Check(TreatyService.Has(s, TreatyType.SovereigntyGuarantee, p.Id, persia.Id), "and the guarantee stays in the diplomatic system");
    s.PendingVictories.Add(new VictoryDecision { LoserId = persia.Id, LoserName = persia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    Check(eng.CheckDiplomaticAction("annex", persia.Id).Available, "the Annex option is offered after a victory despite the old guarantee");
    var annexed = eng.ResolveVictory(persia.Id, VictoryChoice.Annex);
    Check(annexed.Ok && persia.IsEliminated, "so a victory can be turned into an annexation of it");
    Check(!s.Treaties.Any(t => t.Involves(persia.Id)), "the guarantee ends with the annexed country");
}

using (var eng = NewDipEngine(53))
{
    // A third party's guarantee does not shield a beaten country either: an ally's army wins and annexes it.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, p, persia, Balance.SovereigntyDays);
    persia.AtWarWithPlayer = true;
    persia.Units = UnitCatalog.SeedArmy(1200);
    p.Units = UnitCatalog.SeedArmy(2000);   // keeps the beaten AI from suing for peace, which would recall the march
    persia.MapX = spain.MapX + 40; persia.MapY = spain.MapY; persia.Population = spain.Population;
    AiWorldService.SetRelation(s, spain, persia, 30);   // (the ally covets its lands, so it would annex if it could)
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Conquest, "(the ally's motive toward the guaranteed country is land)");
    var march = TreatyService.LaunchMarch(s, spain, persia, 2700)!;
    for (int i = 0; i < march.TotalDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(persia.Soldiers < 1200, "the ally's army did fight the battle");
    Check(persia.IsEliminated, "and the ally annexes the guaranteed country");
    Check(!HasLog(eng, "cannot annex it"), "with no refusal logged");
}

Console.WriteLine("== 44. Ask Attack: a friendly country (no alliance) joins your war ==");
using (var eng = NewDipEngine(54))
{
    var s = eng.State; var p = s.PlayerNation; var spain = Ai(eng, "spain"); var nepal = Ai(eng, "nepal"); var france = Ai(eng, "france");
    Check(!eng.CheckDiplomaticAction("askattack", spain.Id).Available && eng.CheckDiplomaticAction("askattack", spain.Id).Reason.Contains("regards you too poorly"),
        "unavailable below relations 70");
    SetRating(spain, 70);
    Check(eng.AskToAttack(spain.Id, nepal.Id).Message.Contains("not at war with anyone else"), "nothing to join while you are at peace");
    eng.DeclareWar(nepal.Id);
    Check(eng.CheckDiplomaticAction("askattack", spain.Id).Available, "available at relations 70 with a war on");
    Check(eng.AskToAttack(spain.Id, france.Id).Message.Contains("at war with"), "the enemy must be a country you are at war with");

    int playerBefore = p.Soldiers, spainBefore = spain.Soldiers;
    var yes = eng.AskToAttack(spain.Id, nepal.Id);
    int commit = (int)(spainBefore * Balance.CallToArmsFraction);
    Check(yes.Ok && !TreatyService.Has(s, TreatyType.DefensiveAlliance, p.Id, spain.Id), "a non-ally agrees");
    Check(spain.Soldiers == spainBefore - commit && p.Soldiers == playerBefore, "the friend's own soldiers march; the player's army is untouched");
    var march = s.MarchingArmies.Single(x => x.AttackerNationId == spain.Id);
    Check(march.TargetNationId == nepal.Id && march.Strength == commit, "a normal tracked march on the enemy");
    Check(s.Movements.Any(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && m.Text.Contains("agrees to attack")), "recorded in the Movement Report");
    Check(eng.AskToAttack(spain.Id, nepal.Id).Message.Contains("Available again"), "the cooldown stops repeat requests");

    // The friend can say no: an enemy that dwarfs it.
    p.DiplomacyCooldowns.Clear(); s.MarchingArmies.Clear();
    nepal.Units = UnitCatalog.SeedArmy(100_000);
    var no = eng.AskToAttack(spain.Id, nepal.Id);
    Check(no.Outcome == DipOutcome.Rejected && no.Message.Contains("too strong") && s.MarchingArmies.Count == 0, "a friend refuses an enemy that dwarfs it, and nothing marches");
    // At war with the friend itself: closed.
    spain.AtWarWithPlayer = true;
    Check(!eng.CheckDiplomaticAction("askattack", spain.Id).Available, "no requests to a country you are at war with");
}

Console.WriteLine("== 45. Victory: annex, take resources, or let go — each choice has its own flow ==");
using (var eng = NewDipEngine(55))
{
    // Integration: winning a battle opens a decision instead of annexing at once.
    var s = eng.State; var p = s.PlayerNation; var nepal = Ai(eng, "nepal");
    p.Units = UnitCatalog.SeedArmy(6000);
    nepal.Units = UnitCatalog.SeedArmy(3500);   // strong enough that the beaten AI does not sue for peace before the battle
    eng.DeclareWar(nepal.Id);
    Check(eng.CheckDiplomaticAction("annex", nepal.Id).Reason.Contains("Win a battle"), "before any victory the Annex button asks for one");
    Check(eng.LaunchInvasion(nepal.Id, 3000).ok, "invasion launched");
    int battlesBefore = p.BattlesWon;
    int days = s.MarchingArmies[0].TotalDays;
    for (int i = 0; i < days; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var pending = eng.PendingVictory(nepal.Id);
    Check(pending is not null && !nepal.IsEliminated && nepal.AtWarWithPlayer && p.BattlesWon == battlesBefore + 1,
        "a won battle leaves a decision, not an annexation, and counts as a battle won");
    Check(pending!.ExpiresDate == pending.WonDate.AddDays(Balance.VictoryDecisionDays) && pending.DaysLeft(s.CurrentDate) == Balance.VictoryDecisionDays, "the player has 30 days to decide");
    Check(eng.CheckDiplomaticAction("annex", nepal.Id).Available, "the Annex button opens once a victory is won");
    Check(s.Movements.Any(m => m.Kind == MovementKind.War && m.Status == MovementStatus.UnderWay && m.Text.Contains("Victory over")), "the victory is in the Movement Report, under way");
    int nepalSoldiers = nepal.Soldiers, playerSoldiers = p.Soldiers;
    for (int i = 0; i < 10; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(nepal.Soldiers == nepalSoldiers && p.Soldiers == playerSoldiers && nepal.AtWarWithPlayer && eng.PendingVictory(nepal.Id) is not null,
        "while the decision waits the beaten country neither fights, invades nor pleads for peace");
}

using (var eng = NewDipEngine(56))
{
    // Flow 1 — ANNEX: the whole country becomes the player's.
    var s = eng.State; var p = s.PlayerNation; var easter = Ai(eng, "easter");
    easter.AtWarWithPlayer = true; easter.RelationToPlayer = -100;
    VictoryService.Begin(s, p, easter);
    VictoryService.Begin(s, p, easter);
    Check(s.PendingVictories.Count == 1, "a second victory over the same country does not open a second decision");
    long popBefore = p.Population; double gold = p.Gold, easterGold = easter.Gold; int battles = p.BattlesWon;
    var r = eng.ResolveVictory(easter.Id, VictoryChoice.Annex);
    Check(r.Ok && easter.IsEliminated && !easter.AtWarWithPlayer && eng.PendingVictory(easter.Id) is null, "annex: the country is absorbed and the decision is closed");
    Check(Math.Abs(p.Gold - (gold + easterGold)) < 0.01 && easter.Population > 0 && s.TotalPopulation(p) > popBefore, "annex: its treasury becomes ours and its people live on in our realm");
    Check(s.NationsAnnexedByPlayer == 1 && p.BattlesWon == battles, "annex: counted as one annexation, with no extra battle won");
    Check(s.Movements.Any(m => m.Kind == MovementKind.War && m.FromId == p.Id && m.ToId == easter.Id && m.Text.Contains("annexed")), "annex: recorded in the Movement Report");
    Check(eng.ResolveVictory(easter.Id, VictoryChoice.Annex).Outcome == DipOutcome.Invalid, "nothing is left to decide afterwards");
}

using (var eng = NewDipEngine(57))
{
    // Flow 2 — TAKE RESOURCES: a share of its wealth moves, the country survives, resentful.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    persia.AtWarWithPlayer = true; persia.RelationToPlayer = -100;
    VictoryService.Begin(s, p, persia);
    double share = Balance.VictorySpoilsFraction;
    double pGold = p.Gold, tGold = persia.Gold;
    double tWood = persia.Wood, pWood = p.Wood, tWheat = persia.GetGood("Wheat"), pWheat = p.GetGood("Wheat");
    long tPop = persia.Population; int tSoldiers = persia.Soldiers;
    var r = eng.ResolveVictory(persia.Id, VictoryChoice.Resources);
    double takenGold = Math.Floor(tGold * share);
    Check(r.Ok && !persia.IsEliminated && !persia.AtWarWithPlayer && eng.PendingVictory(persia.Id) is null, "resources: the country survives, the war ends, the decision closes");
    Check(p.Gold == pGold + takenGold && persia.Gold == tGold - takenGold, "resources: exactly 30% of its gold moves to us");
    Check(Math.Abs(p.Gold + persia.Gold - (pGold + tGold)) < 0.001, "resources: nothing is created or lost");
    Check(persia.Wood == tWood - Math.Floor(tWood * share) && p.Wood == pWood + Math.Floor(tWood * share), "resources: minerals move the same way");
    Check(Math.Abs(persia.GetGood("Wheat") - (tWheat - Math.Floor(tWheat * share))) < 0.001 && Math.Abs(p.GetGood("Wheat") - (pWheat + Math.Floor(tWheat * share))) < 0.001,
        "resources: food and goods stocks move the same way");
    Check(persia.Population == tPop && persia.Soldiers == tSoldiers && s.NationsAnnexedByPlayer == 0, "resources: its people and army are left alone, and nothing is annexed");
    Check(Rate(persia) == Balance.VictorySpoilsMaxRating, "resources: the plundered country regards us poorly (relations 20)");
    Check(s.Movements.Any(m => m.Kind == MovementKind.Gold && m.Status == MovementStatus.Completed && m.FromId == persia.Id && m.ToId == p.Id)
          && s.Movements.Any(m => m.Kind == MovementKind.Goods && m.FromId == persia.Id && m.ToId == p.Id)
          && s.Movements.Any(m => m.Kind == MovementKind.War && m.Text.Contains("after taking its resources")), "resources: gold, goods and the peace are recorded");
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Resources).Outcome == DipOutcome.Invalid, "it cannot be plundered twice for one victory");
}

using (var eng = NewDipEngine(58))
{
    // Flow 3 — NOTHING: a white peace; the country is grateful.
    var s = eng.State; var p = s.PlayerNation; var mughal = Ai(eng, "mughal");
    mughal.AtWarWithPlayer = true; mughal.RelationToPlayer = -100;
    VictoryService.Begin(s, p, mughal);
    double pGold = p.Gold, tGold = mughal.Gold; int moved = s.Movements.Count(m => m.Kind is MovementKind.Gold or MovementKind.Goods);
    var r = eng.ResolveVictory(mughal.Id, VictoryChoice.Nothing);
    Check(r.Ok && !mughal.IsEliminated && !mughal.AtWarWithPlayer && eng.PendingVictory(mughal.Id) is null, "nothing: the war ends and the country stands");
    Check(p.Gold == pGold && mughal.Gold == tGold && s.Movements.Count(m => m.Kind is MovementKind.Gold or MovementKind.Goods) == moved, "nothing: nothing at all is taken");
    Check(Rate(mughal) == Balance.VictoryMercyRating, "nothing: the country is grateful (relations back to neutral)");
    Check(s.Movements.Any(m => m.Kind == MovementKind.War && m.Status == MovementStatus.Completed && m.Text.Contains("let Mughal Empire go")), "nothing: recorded in the Movement Report");
}

using (var eng = NewDipEngine(59))
{
    // Unanswered, ended by peace, taken by someone else, saved and loaded.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var nepal = Ai(eng, "nepal"); var france = Ai(eng, "france");
    persia.AtWarWithPlayer = true; persia.RelationToPlayer = -100;
    VictoryService.Begin(s, p, persia);
    for (int i = 0; i < Balance.VictoryDecisionDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(eng.PendingVictory(persia.Id) is null && !persia.AtWarWithPlayer && !persia.IsEliminated && HasLog(eng, "No decision was made"),
        "left undecided for 30 days, the country is let go with nothing taken");

    spain.AtWarWithPlayer = true;
    VictoryService.Begin(s, p, spain);
    Warfare.AnnexNation(s, france, spain);
    Check(eng.PendingVictory(spain.Id) is null, "if someone else annexes the country first, the decision disappears");

    nepal.AtWarWithPlayer = true;
    VictoryService.Begin(s, p, nepal);
    Check(eng.ProposePeace(nepal.Id, new[] { PeaceTerm.Peace() }).Outcome == DipOutcome.Invalid && eng.PendingVictory(nepal.Id) is not null, "with a victory waiting, talks are refused: the victory choices are how that war ends");
    Check(eng.ResolveVictory(nepal.Id, VictoryChoice.Nothing).Ok && eng.PendingVictory(nepal.Id) is null && !nepal.AtWarWithPlayer, "letting the country go closes the decision");

    // Save / load.
    var mughal = Ai(eng, "mughal"); mughal.AtWarWithPlayer = true;
    VictoryService.Begin(s, p, mughal);
    var before = s.PendingVictories.Single();
    await eng.SaveAsync();
    eng.State.PendingVictories.Clear();
    await eng.LoadAsync();
    var after = eng.State.PendingVictories.Single();
    Check(after.LoserId == before.LoserId && after.LoserName == before.LoserName && after.WonDate == before.WonDate && after.ExpiresDate == before.ExpiresDate,
        "a pending victory survives save / load");
    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    node.Remove("PendingVictories");
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.PendingVictories.Count == 0, "an old save without the field loads with no pending victories");
}

Console.WriteLine("== 46. Research: points, technologies and the research contract ==");
Check(TechnologyCatalog.All.Count == 3 && TechnologyCatalog.All.Select(t => t.Id).Distinct().Count() == 3 && TechnologyCatalog.All.All(t => t.Cost > 0 && t.Effect.Length > 0),
    "three technologies, each with a cost and an effect text");
using (var eng = NewDipEngine(61))
{
    var s = eng.State; var p = s.PlayerNation;
    Check(Math.Abs(ResearchService.DailyPoints(p) - 4.0) < 1e-9 && Math.Abs(eng.ResearchPerDay - 4.0) < 1e-9,
        "the Ottomans (30 million people) research 4 points a day: 1 + 1 per 10 million");
    Check(p.ResearchPoints == 0 && p.Technologies.Count == 0 && p.CurrentResearchId is null, "nothing researched at the start");
    for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
    Check(p.ResearchPoints > 39.9 && p.ResearchPoints < 42, "points bank every day, even with nothing chosen");
    Check(p.BattleStrengthMult == 1.0 && p.TradeIncomeMult == 1.0 && LawService.GeneralProdOutputMult(p) == 1.0, "no technology: every multiplier is exactly 1.0");

    Check(eng.StartResearch("nope") == "Unknown technology.", "an unknown technology is refused");
    Check(eng.StartResearch("drill_manuals") is null && p.CurrentResearchId == "drill_manuals", "a technology can be chosen");
    Check(eng.StartResearch("drill_manuals")!.Contains("already being researched"), "choosing the same one again is refused");
    p.ResearchPoints = 399;
    eng.AdvanceOneDay();
    Check(p.Technologies.Contains("drill_manuals") && p.CurrentResearchId is null, "the technology completes the day the points cover its cost");
    Check(p.ResearchPoints > 0 && p.ResearchPoints < 10, "its cost is spent and the surplus stays banked");
    Check(HasLog(eng, "Research complete: Drill Manuals"), "completion is logged");
    Check(Math.Abs(p.BattleStrengthMult - 1.05) < 1e-9, "Drill Manuals: +5% battle strength");
    p.ActiveEdicts.Add(EdictType.MilitaryDrills);
    Check(Math.Abs(p.BattleStrengthMult - 1.10 * 1.05) < 1e-9, "it stacks with the Military Drills edict");
    Check(eng.StartResearch("drill_manuals")!.Contains("already researched"), "a finished technology cannot be researched again");
    p.Technologies.Add("merchant_law");
    Check(Math.Abs(p.TradeIncomeMult - 1.10) < 1e-9, "Merchant Law: +10% trade income");
    p.Technologies.Add("improved_tools");
    Check(Math.Abs(LawService.GeneralProdOutputMult(p) - 1.05) < 1e-9, "Improved Tools: +5% production output");
}

using (var engA = NewDipEngine(62))
using (var engB = NewDipEngine(62))
{
    // Improved Tools really raises what the mills make.
    engA.State.PlayerNation.Technologies.Add("improved_tools");
    engA.AdvanceOneDay(); engB.AdvanceOneDay();
    double stockA = engA.State.PlayerNation.GoodsInventory.Values.Sum(), stockB = engB.State.PlayerNation.GoodsInventory.Values.Sum();
    Check(stockA > stockB, "with Improved Tools the mills produce more");
}

using (var engA = NewDipEngine(66))
using (var engB = NewDipEngine(66))
{
    // Banking points and even choosing a technology change nothing else; AI countries never research.
    engA.StartResearch("improved_tools");
    for (int i = 0; i < 40; i++) { engA.AdvanceOneDay(); engB.AdvanceOneDay(); }
    var pA = engA.State.PlayerNation; var pB = engB.State.PlayerNation;
    Check(pA.Technologies.Count == 0 && pA.ResearchPoints > 150, "the technology is still being researched");
    Check(pA.Population == pB.Population && pA.Gold == pB.Gold && pA.Soldiers == pB.Soldiers && pA.GetProduct("Wheat") == pB.GetProduct("Wheat"),
        "population, treasury, army and stocks are unaffected by researching");
    Check(engA.State.OtherNations.All(n => n.ResearchPoints == 0 && n.Technologies.Count == 0 && n.CurrentResearchId is null), "AI countries never research");
}

using (var eng = NewDipEngine(63))
{
    var s = eng.State; var p = s.PlayerNation; var mughal = Ai(eng, "mughal");   // 100 million people, Islam like the Ottomans
    Check(eng.ProposeResearchContract(mughal.Id).Message.Contains("embassy"), "a research contract needs an embassy first");
    eng.EstablishEmbassy(mughal.Id);
    double g0 = p.Gold;
    var r = eng.ProposeResearchContract(mughal.Id);
    Check(r.Ok && Math.Abs(p.Gold - (g0 - Balance.ResearchContractCost)) < 0.01, "contract signed for its price (50 + embassy 5 + shared faith 5 reaches the 60 asked)");
    var contract = s.Treaties.Single(x => x.Type == TreatyType.ResearchContract);
    Check(contract.NationAId == p.Id && contract.NationBId == mughal.Id && contract.ExpiresDate == s.CurrentDate.AddDays(Balance.ResearchContractDays), "tracked, with a one-year term");
    Check(Math.Abs(ResearchService.ContractIncome(s) - 5.5) < 1e-6 && Math.Abs(eng.ResearchPerDay - 9.5) < 1e-6,
        "the partner (11 points a day) hands over half: +5.5 on top of our own 4");
    Check(eng.ProposeResearchContract(mughal.Id).Message.Contains("already have a research contract"), "no second contract with the same partner");
    for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
    Check(p.ResearchPoints > 94 && p.ResearchPoints < 100, "the contract income banks every day");

    // The cap on contracts.
    TreatyService.Add(s, TreatyType.ResearchContract, p, Ai(eng, "spain"), 365);
    TreatyService.Add(s, TreatyType.ResearchContract, p, Ai(eng, "france"), 365);
    Check(eng.ProposeResearchContract(Ai(eng, "england").Id).Message.Contains("most allowed"), "at most three contracts at a time");

    // Cancelling and war end the benefit.
    int rating = Rate(mughal);
    Check(eng.CancelTreaty(TreatyType.ResearchContract, mughal.Id).Ok && Rate(mughal) == rating - Balance.BreakMinorTreatyRatingPenalty,
        "a contract can be cancelled for a small relations cost");
    Check(Math.Abs(ResearchService.ContractIncome(s) - (ResearchService.DailyPoints(Ai(eng, "spain")) + ResearchService.DailyPoints(Ai(eng, "france"))) * 0.5) < 1e-6,
        "only the remaining partners still pay");
    eng.DeclareWar("spain");
    Check(ResearchService.Contracts(s).Count == 1, "war with a partner ends its contract");
}

using (var engA = NewDipEngine(67))
using (var engB = NewDipEngine(67))
{
    // The real benefit: the contract finishes a technology sooner.
    TreatyService.Add(engA.State, TreatyType.ResearchContract, engA.State.PlayerNation, Ai(engA, "mughal"), 365);
    engA.StartResearch("drill_manuals"); engB.StartResearch("drill_manuals");
    for (int i = 0; i < 45; i++) { engA.AdvanceOneDay(); engB.AdvanceOneDay(); }
    Check(engA.State.PlayerNation.Technologies.Contains("drill_manuals") && !engB.State.PlayerNation.Technologies.Contains("drill_manuals"),
        "with a research contract the technology is done in 45 days; without one it is not");
}

using (var eng = NewDipEngine(68))
{
    // A contract runs out after its term.
    var s = eng.State; var p = s.PlayerNation;
    TreatyService.Add(s, TreatyType.ResearchContract, p, Ai(eng, "mughal"), Balance.ResearchContractDays);
    for (int i = 0; i < Balance.ResearchContractDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(ResearchService.Contracts(s).Count == 0 && ResearchService.ContractIncome(s) == 0 && HasLog(eng, "research contract with Mughal Empire has expired"),
        "the contract expires after a year and the income stops");
}

Console.WriteLine("== 47. Support Sovereignty: an independence guarantee ==");
using (var eng = NewDipEngine(65))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    Check(eng.ProposeSovereigntyGuarantee(persia.Id).Message.Contains("embassy"), "a guarantee needs an embassy first");
    eng.EstablishEmbassy(persia.Id);
    Check(eng.ProposeSovereigntyGuarantee(persia.Id).Message.Contains("regards you too poorly"), "relations 55+ are needed");
    SetRating(persia, 60);
    int scoreBefore = TreatyService.Score(s, persia);
    double g0 = p.Gold;
    var r = eng.ProposeSovereigntyGuarantee(persia.Id);
    Check(r.Ok && Math.Abs(p.Gold - (g0 - Balance.SovereigntyCost)) < 0.01, "the guarantee is given for its price");
    var treaty = s.Treaties.Single(x => x.Type == TreatyType.SovereigntyGuarantee);
    Check(treaty.NationAId == p.Id && treaty.NationBId == persia.Id && treaty.ExpiresDate == s.CurrentDate.AddDays(Balance.SovereigntyDays), "tracked, with a two-year term");
    Check(TreatyService.Has(s, TreatyType.SovereigntyGuarantee, p.Id, persia.Id) && !TreatyService.Has(s, TreatyType.SovereigntyGuarantee, persia.Id, p.Id),
        "the guarantee runs one way: we guarantee them");
    Check(TreatyService.Score(s, persia) == scoreBefore + Balance.SovereigntyScoreBonus, "a guaranteed country trusts us more");
    Check(eng.ProposeSovereigntyGuarantee(persia.Id).Message.Contains("already guarantee"), "no second guarantee");

    // It binds the guarantor and shields the country.
    var war = eng.DeclareWar(persia.Id);
    Check(war is not null && war.Contains("guaranteed") && !persia.AtWarWithPlayer, "we cannot declare war on a country we guaranteed");
    var tribute = eng.DemandTribute(persia.Id);
    Check(tribute is not null && tribute.Contains("guaranteed"), "nor demand tribute");
    Check(eng.EstablishNetwork(persia.Id) is null, "a spy network may still be set up");
    string?[] spy = { eng.SpySteal(persia.Id), eng.SpySabotage(persia.Id), eng.SpyInciteRevolt(persia.Id) };
    Check(spy.All(x => x is not null && x.Contains("guaranteed")), "but hostile spy work is barred");
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null, "(the guarantee does not stand in the way of annexing it after a victory)");
    Check(s.Movements.Any(m => m.Kind == MovementKind.Treaty && m.Text.Contains("sovereignty guarantee")), "the guarantee is in the Movement Report");

    // It warms relations faster than an embassy alone.
    eng.EstablishEmbassy(france.Id);
    double p0 = persia.RelationToPlayer, f0 = france.RelationToPlayer;
    for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
    Check((persia.RelationToPlayer - p0) - (france.RelationToPlayer - f0) > 2.0, "a guarantee warms relations faster than an embassy alone");

    // Walking away costs relations and frees us.
    int rating = Rate(persia);
    Check(eng.CancelTreaty(TreatyType.SovereigntyGuarantee, persia.Id).Ok && Rate(persia) == rating - Balance.BreakPactRatingPenalty, "cancelling the guarantee costs relations like breaking a pact");
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null && eng.DeclareWar(persia.Id) is null, "afterwards the country is unprotected and war is possible");
}

using (var eng = NewDipEngine(69))
{
    // Research and guarantee data survive save / load; old saves without research fields load clean.
    var s = eng.State; var p = s.PlayerNation; var mughal = Ai(eng, "mughal"); var persia = Ai(eng, "persia");
    p.Technologies.Add("merchant_law"); p.ResearchPoints = 123.5; p.CurrentResearchId = "improved_tools";
    TreatyService.Add(s, TreatyType.ResearchContract, p, mughal, 365);
    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, p, persia, 730);
    await eng.SaveAsync();
    p.Technologies.Clear(); p.ResearchPoints = 0; p.CurrentResearchId = null; s.Treaties.Clear();
    await eng.LoadAsync();
    var lp = eng.State.PlayerNation;
    Check(lp.Technologies.SequenceEqual(new[] { "merchant_law" }) && Math.Abs(lp.ResearchPoints - 123.5) < 1e-9 && lp.CurrentResearchId == "improved_tools",
        "technologies, banked points and the current research survive save / load");
    Check(eng.State.Treaties.Count(x => x.Type is TreatyType.ResearchContract or TreatyType.SovereigntyGuarantee) == 2
          && TreatyService.Has(eng.State, TreatyType.SovereigntyGuarantee, lp.Id, "persia") && ResearchService.Contracts(eng.State).Count == 1,
        "the research contract and the guarantee survive, and are live after loading");
    Check(Math.Abs(lp.TradeIncomeMult - 1.10) < 1e-9, "a loaded technology still works");

    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    var playerNode = node["PlayerNation"]!.AsObject();
    foreach (var key in new[] { "Technologies", "ResearchPoints", "CurrentResearchId" }) playerNode.Remove(key);
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.PlayerNation.Technologies.Count == 0 && old.PlayerNation.ResearchPoints == 0 && old.PlayerNation.CurrentResearchId is null,
        "an old save without research fields loads with nothing researched");
}

Console.WriteLine("== 48. AI countries feed themselves; only the chosen country starts short ==");
double MillOutput(Nation n, ConsumptionSpec spec)
{
    var mill = ProductionCatalog.All.First(b => b.Produces == spec.Item);
    return n.GetProductionBuilding(mill.Id) * ConsumptionService.OutputPerMill(n, mill);
}
bool Covers(Nation n) => ConsumptionCatalog.All.All(spec => MillOutput(n, spec) >= ConsumptionService.DailyNeed(spec, n.Population));

using (var eng = NewDipEngine(81))
{
    var s = eng.State; var p = s.PlayerNation;
    Check(s.OtherNations.All(Covers), "every AI country's mills cover every item's need from the start");
    Check(!Covers(p), "the chosen country starts short: its mills do not cover its need");
    for (int i = 0; i < 40; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(s.OtherNations.All(n => !n.ShortagePct.Values.Any(v => v > 0) && n.LastShortageDeaths == 0), "no AI country is ever short, and none has shortage deaths");
    Check(s.OtherNations.All(n => n.RulerRating == 50), "no AI country loses ruler rating to shortages");
}

using (var eng = NewDipEngine(82))
{
    eng.StartCampaign("france");
    var s = eng.State;
    Check(s.PlayerNation.Id == "france" && !Covers(s.PlayerNation), "whichever country is chosen starts short (here France)");
    Check(Covers(Ai(eng, "ottoman")), "...and the Ottomans, now an AI country, feed themselves");
}

using (var eng = NewDipEngine(83))
{
    var s = eng.State; var persia = Ai(eng, "persia");
    persia.Population *= 3;
    Check(!Covers(persia), "a tripled population outgrows its mills");
    int added = ConsumptionService.EnsureSupply(persia, Balance.AiSupplyCoverage);
    Check(added > 0 && Covers(persia), "EnsureSupply adds the mills it needs");
    Check(ConsumptionService.EnsureSupply(persia, Balance.AiSupplyCoverage) == 0, "and does nothing the second time");
    var snapshot = persia.ProductionBuildings.ToDictionary(kv => kv.Key, kv => kv.Value);
    persia.Population /= 3;
    Check(ConsumptionService.EnsureSupply(persia, Balance.AiSupplyCoverage) == 0 && persia.ProductionBuildings.All(kv => snapshot[kv.Key] == kv.Value),
        "it never removes mills when the population falls");

    // Once the AI world is awake, a growing country builds what it needs by itself.
    s.AiWorldStartDate = s.CurrentDate;
    var spain = Ai(eng, "spain");
    spain.Population *= 2;
    for (int i = 0; i < Balance.AiThinkIntervalDays * 2; i++) eng.AdvanceOneDay();
    Check(Covers(spain), "an AI country whose people doubled builds the mills it needs within a couple of thinks");
}

using (var eng = NewDipEngine(84))
{
    var s = eng.State; var persia = Ai(eng, "persia");
    int baseSoldiers = persia.BaseSoldiers;
    Check(baseSoldiers == persia.Soldiers && baseSoldiers > 0 && persia.BasePopulation == persia.Population, "an AI country's base army is its starting army");
    persia.Units = UnitCatalog.SeedArmy(baseSoldiers / 4);
    int low = persia.Soldiers;
    s.AiWorldStartDate = s.CurrentDate.AddDays(10_000);
    for (int i = 0; i < 60; i++) eng.AdvanceOneDay();
    Check(persia.Soldiers == low, "before the AI world wakes up, armies are not rebuilt");
    s.AiWorldStartDate = s.CurrentDate;
    for (int i = 0; i < 400; i++) eng.AdvanceOneDay();
    Check(persia.Soldiers > low * 2 && persia.Soldiers <= baseSoldiers * 1.2, "once awake, an AI country rebuilds its army toward its target");

    var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    int bs = spain.BaseSoldiers, fs = france.BaseSoldiers; long bp = spain.BasePopulation, fp = france.BasePopulation;
    Warfare.AnnexNation(s, spain, france);
    Check(spain.BaseSoldiers == bs + fs && spain.BasePopulation == bp + fp, "annexing a country adds its base army and base population, so the winner's army target is not inflated");
}

Console.WriteLine("== 49. Relations between AI countries ==");
using (var eng = NewDipEngine(85))
{
    var s = eng.State; var a = Ai(eng, "france"); var b = Ai(eng, "spain");
    Check(AiWorldService.PairKey(a.Id, b.Id) == AiWorldService.PairKey(b.Id, a.Id), "a pair has one key, whichever way round");
    Check(Math.Abs(AiWorldService.BaseRelation(a, b) - AiWorldService.BaseRelation(b, a)) < 1e-9, "regard between two countries is symmetric");
    var all = s.OtherNations;
    var pairs = (from i in Enumerable.Range(0, all.Count) from j in Enumerable.Range(i + 1, all.Count - i - 1)
                 select (A: all[i], B: all[j], R: AiWorldService.BaseRelation(all[i], all[j]))).ToList();
    Check(pairs.All(x => x.R >= -90 && x.R <= 90), "starting regard stays within -90..90");
    double same = pairs.Where(x => x.A.Religion != "" && x.A.Religion == x.B.Religion).Average(x => x.R);
    double diff = pairs.Where(x => x.A.Religion != x.B.Religion).Average(x => x.R);
    Check(same - diff > 15, $"countries of one faith like each other more (average {same:0.#} against {diff:0.#})");
    Check(pairs.Any(x => x.R < 0) && pairs.Any(x => x.R >= Balance.AiAllianceMinRelation), "there are both rivals and friends to start with");

    double baseR = AiWorldService.BaseRelation(a, b);
    AiWorldService.AddRelation(s, a, b, 5);
    Check(Math.Abs(AiWorldService.Relation(s, a, b) - Math.Clamp(baseR + 5, -100, 100)) < 1e-9, "events move regard away from its starting value");
    AiWorldService.AddRelation(s, a, b, -500);
    Check(AiWorldService.Relation(s, a, b) == -100, "regard never goes below -100");

    // Changes fade back toward the starting regard once the AI world is awake.
    s.AiRelations.Clear(); s.AiWorldStartDate = s.CurrentDate;
    AiWorldService.AddRelation(s, a, b, 10);
    for (int i = 0; i < 20; i++) eng.AdvanceOneDay();
    Check(Math.Abs(s.AiRelations[AiWorldService.PairKey(a.Id, b.Id)] - (10 - 20 * Balance.AiRelationDecayPerDay)) < 0.001, "a change in regard fades slowly back over time");
}

Console.WriteLine("== 50. AI wars, alliances, armies and peace ==");
using (var eng = NewDipEngine(86))
{
    var s = eng.State;
    for (int i = 0; i < 300; i++) { s.PlayerNation.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(s.Wars.Count == 0 && s.AiRelations.Count == 0 && !s.Treaties.Any(t => t.Type == TreatyType.DefensiveAlliance),
        "the world is calm for the first year: no AI war, regard change or alliance before the AI world wakes up");
}

using (var eng = NewDipEngine(87))
{
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia");
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    var war = s.Wars.Single(w => w.Links(france.Id, persia.Id));
    Check(war.AggressorId == france.Id && war.DefenderId == persia.Id && AiWorldService.AtWar(s, persia.Id, france.Id), "a war between two AI countries is recorded, with who started it");
    Check(s.AiRelations[AiWorldService.PairKey(france.Id, persia.Id)] == Balance.AiWarRelationHit, "the war sours their regard");
    Check(s.Movements.Any(m => m.Kind == MovementKind.War && m.FromId == france.Id && m.ToId == persia.Id && m.Text.Contains("declared war")), "it is in the Movement Report");
    Check(AiWorldService.EnemiesOf(s, france).Any(n => n.Id == persia.Id) && AiWorldService.EnemiesOf(s, persia).Any(n => n.Id == france.Id), "each is the other's enemy");
    Check(AiWorldService.InWar(s, france, persia) && !AiWorldService.InWar(s, france, Ai(eng, "spain")), "an army may fight a country it is at war with, not another");
}

{
    // Allies: those of the attacked country usually join; those of the attacker sometimes do; one that has soured ignores the call.
    int defenderJoined = 0, attackerJoined = 0;
    for (int seed = 1; seed <= 20; seed++)
    {
        using var e = NewDipEngine(200 + seed);
        var s = e.State; s.AiWorldStartDate = s.CurrentDate;
        var france = Ai(e, "france"); var persia = Ai(e, "persia"); var spain = Ai(e, "spain"); var mughal = Ai(e, "mughal");
        TreatyService.Add(s, TreatyType.DefensiveAlliance, spain, persia);
        TreatyService.Add(s, TreatyType.DefensiveAlliance, mughal, france);
        AiWorldService.AddRelation(s, spain, persia, 300); AiWorldService.AddRelation(s, mughal, france, 300);
        AiWorldService.DeclareWar(s, france, persia, new Random(seed));
        if (s.Wars.Any(w => w.AggressorId == france.Id && w.DefenderId == spain.Id)) defenderJoined++;
        if (s.Wars.Any(w => w.AggressorId == mughal.Id && w.DefenderId == persia.Id)) attackerJoined++;
    }
    Check(defenderJoined >= 12, $"an ally of the attacked country usually joins ({defenderJoined} of 20)");
    Check(attackerJoined >= 1 && attackerJoined <= 12 && attackerJoined < defenderJoined, $"an ally of the attacker joins less often ({attackerJoined} of 20)");

    using var eng = NewDipEngine(88);
    var st = eng.State; st.AiWorldStartDate = st.CurrentDate;
    var f = Ai(eng, "france"); var pe = Ai(eng, "persia"); var jp = Ai(eng, "japan");
    TreatyService.Add(st, TreatyType.DefensiveAlliance, jp, pe);
    AiWorldService.AddRelation(st, jp, pe, -300);   // the alliance has soured
    AiWorldService.DeclareWar(st, f, pe, new Random(1));
    Check(!st.Wars.Any(w => w.DefenderId == jp.Id), "an ally that no longer cares ignores the call");
    Check(AiWorldService.AlliesOf(st, pe).Any(n => n.Id == jp.Id) && AiWorldService.AlliesOf(st, pe).All(n => !n.IsPlayer), "(allies are listed without the player)");
}

using (var eng = NewDipEngine(89))
{
    // Armies march, fight and the war ends in peace.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia");
    france.Units = UnitCatalog.SeedArmy(9000); persia.Units = UnitCatalog.SeedArmy(8000);
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    for (int i = 0; i < 200; i++) { s.PlayerNation.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(s.Movements.Any(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && ((m.FromId == france.Id && m.ToId == persia.Id) || (m.FromId == persia.Id && m.ToId == france.Id))),
        "the two sides send armies against each other");
    Check(s.Movements.Any(m => m.Kind == MovementKind.March && m.Status == MovementStatus.Completed && m.Text.Contains("vs")), "their armies fight a battle, and it is recorded");
    Check(s.OtherNations.All(n => n.Soldiers >= 0), "no army ever goes below zero");
}

using (var eng = NewDipEngine(90))
{
    // A war that has worn both sides out ends in peace.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia");
    france.Units = UnitCatalog.SeedArmy(300); persia.Units = UnitCatalog.SeedArmy(300);
    france.BaseSoldiers = 0; persia.BaseSoldiers = 0;   // (no recruiting, so they stay worn out)
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    for (int i = 0; i < 40; i++) { s.PlayerNation.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(!AiWorldService.AtWar(s, france.Id, persia.Id) && HasLog(eng, "France and Iran made peace"), "a war between exhausted countries ends in peace");
    // (regard fades back toward its starting value by 0.05 a day after the peace, so allow a point or two)
    Check(Math.Abs(AiWorldService.Relation(s, france, persia) - Balance.AiWarPeaceRelation) < 3 && AiWorldService.Motives(s, france, persia).Grudge < 0.03,
        "and leaves them neutral, with no grudge left to start the war again");
}

using (var eng = NewDipEngine(91))
{
    // AI countries pick the player as a target: hostile and stronger, within reach.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var near = s.OtherNations.Where(n => AiWorldService.Distance(n, p) <= Balance.AiWarRange).OrderBy(n => AiWorldService.Distance(n, p)).First();
    near.Units = UnitCatalog.SeedArmy(200_000);
    // (An AI country picks its juiciest target, and weaker AI neighbours would outrank the player: move the others out of reach.)
    foreach (var other in s.OtherNations.Where(n => n.Id != near.Id)) other.MapX += 100_000;
    for (int i = 0; i < 4000 && !near.AtWarWithPlayer; i++)
    {
        near.RelationToPlayer = -60;   // (hostile: peace-time drift would otherwise cool it)
        p.Gold = 1_000_000_000_000;
        eng.AdvanceOneDay();
    }
    Check(near.AtWarWithPlayer, $"a hostile, much stronger neighbour ({near.Name}) eventually declares war on the player");
    Check(s.Movements.Any(m => m.Kind == MovementKind.War && m.FromId == near.Id && m.ToId == p.Id && m.Text.Contains("declared war")), "the declaration is recorded");
    Check(s.ActiveWarnings.Any(w => w.Contains("DECLARED WAR")), "and the player is warned");
}

using (var eng = NewDipEngine(92))
{
    // An attack on the player's ally is flagged to the player.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, persia);
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    Check(s.ActiveWarnings.Any(w => w.Contains("your ally") && w.Contains("Iran")), "the player is told when an AI country attacks the player's ally");

    // A march between countries that are not at war is called off (nobody may fight without a war).
    var spain = Ai(eng, "spain"); var mughal = Ai(eng, "mughal");
    int before = spain.Soldiers;
    var march = TreatyService.LaunchMarch(s, spain, mughal, 800)!;
    for (int i = 0; i < march.TotalDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(!mughal.IsEliminated && s.MarchingArmies.All(m => m.AttackerNationId != spain.Id) && HasLog(eng, "march on Mughal Empire was called off"), "an army sent against a country it is not at war with is called off");
}

Console.WriteLine("== 51. Wars from motives: any country may attack any other ==");

// Two countries side by side, neutral toward each other: the setting the motive tests start from.
void Neighbours(GameState st, Nation a, Nation b, double regard = 0)
{
    b.MapX = a.MapX + 40; b.MapY = a.MapY;
    b.Population = a.Population;
    AiWorldService.SetRelation(st, a, b, regard);
}

using (var eng = NewDipEngine(100))
{
    var s = eng.State; var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    spain.Units = UnitCatalog.SeedArmy(9000); persia.Units = UnitCatalog.SeedArmy(2000);
    Neighbours(s, spain, persia);
    var near = AiWorldService.Motives(s, spain, persia);
    Check(near.Land > 0.9 && near.Grudge == 0 && near.Duty == 0, $"a populous neighbour is a prize (land {near.Land:0.00}), and there is no grudge between friends of neutral regard");
    persia.MapX = spain.MapX + 100_000;
    var far = AiWorldService.Motives(s, spain, persia);
    Check(far.Land == 0 && far.Fear == 0, "a country beyond reach is neither a prize nor a menace");

    Neighbours(s, spain, persia, -80);
    Check(Math.Abs(AiWorldService.Motives(s, spain, persia).Grudge - 0.8) < 0.02, "regard below zero is a grudge: -80 gives 0.8");
    Neighbours(s, spain, persia, 60);
    Check(AiWorldService.Motives(s, spain, persia).Grudge == 0, "regard above zero is no grudge");

    // Fear: a near neighbour whose army matches one's own, that one is not friendly with.
    persia.Units = UnitCatalog.SeedArmy(20_000);
    Neighbours(s, spain, persia, 0);
    double menace = AiWorldService.Motives(s, spain, persia).Fear;
    AiWorldService.SetRelation(s, spain, persia, 100);
    double friend = AiWorldService.Motives(s, spain, persia).Fear;
    Check(menace > 0.9 && friend < 0.05, $"a strong neighbour is a menace (fear {menace:0.00}), unless it is a close friend (fear {friend:0.00})");

    // Duty: the country is at war with one of the ruler's allies.
    var mughal = Ai(eng, "mughal");
    TreatyService.Add(s, TreatyType.DefensiveAlliance, spain, mughal);
    Check(AiWorldService.Motives(s, spain, persia).Duty == 0, "no duty while nobody is at war");
    s.Wars.Add(new WarRecord { AggressorId = persia.Id, DefenderId = mughal.Id, StartDate = s.CurrentDate });
    Check(AiWorldService.Motives(s, spain, persia).Duty == 1, "an enemy of one's ally is a matter of duty");
}

using (var eng = NewDipEngine(101))
{
    // What a war would be for follows the strongest motive.
    var s = eng.State; var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    spain.Units = UnitCatalog.SeedArmy(9000);
    persia.Units = UnitCatalog.SeedArmy(1500);
    Neighbours(s, spain, persia, 10);
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Conquest, "a rich, weak neighbour: a war of conquest");
    persia.Population = spain.Population / 20; persia.Units = UnitCatalog.SeedArmy(1500);
    AiWorldService.SetRelation(s, spain, persia, -85);
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Humiliation, "a hated but small rival: a war to humble it");
    persia.Units = UnitCatalog.SeedArmy(30_000);
    AiWorldService.SetRelation(s, spain, persia, 0);
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Containment, "a small but heavily armed neighbour: a war to contain it");
    persia.MapX = spain.MapX + 100_000;
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Containment, "no motive at all ends in a white peace, never a conquest");
}

using (var eng = NewDipEngine(102))
{
    // A weaker country does weigh a war on a stronger one: it is rarer, not forbidden, and allies tip the odds.
    var s = eng.State; var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    spain.Units = UnitCatalog.SeedArmy(3000); persia.Units = UnitCatalog.SeedArmy(12_000);
    Neighbours(s, spain, persia, -70);
    double weak = AiWorldService.Appetite(s, spain, persia);
    double strong = AiWorldService.Appetite(s, persia, spain);
    Check(weak > 0, $"a country four times weaker still has an appetite for the war ({weak:0.000})");
    Check(strong > weak * 5, $"...but a stronger country wants the same war far more ({strong:0.00} against {weak:0.000})");
    Check(AiWorldService.Confidence(0.2) > 0 && AiWorldService.Confidence(0.2) < AiWorldService.Confidence(1) && AiWorldService.Confidence(1) < AiWorldService.Confidence(5)
          && Math.Abs(AiWorldService.Confidence(1) - 0.5) < 1e-9, "confidence rises with the odds: never nil, even at even odds, high for a crushing edge");

    foreach (var id in new[] { "france", "mughal", "japan" })
    {
        var ally = Ai(eng, id);
        ally.Units = UnitCatalog.SeedArmy(12_000);
        TreatyService.Add(s, TreatyType.DefensiveAlliance, spain, ally);
    }
    double backed = AiWorldService.Appetite(s, spain, persia);
    Check(backed > weak * 4, $"three strong allies make the same war far more tempting ({backed:0.00} against {weak:0.000})");
}

using (var eng = NewDipEngine(103))
{
    // End to end: a weaker, hostile country with strong allies declares war on a stronger neighbour.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    foreach (var n in s.OtherNations.Where(n => n.Id != spain.Id && n.Id != persia.Id)) n.MapX += 100_000;   // (nobody else is within reach)
    p.MapX += 100_000;
    spain.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(9000);
    spain.BaseSoldiers = 5000; persia.BaseSoldiers = 9000;
    Neighbours(s, spain, persia, -100);
    foreach (var id in new[] { "france", "mughal", "japan" })
    {
        var ally = Ai(eng, id); ally.Units = UnitCatalog.SeedArmy(15_000); ally.BaseSoldiers = 15_000;
        TreatyService.Add(s, TreatyType.DefensiveAlliance, spain, ally);
    }
    int day = 0;
    for (; day < 10_000 && !AiWorldService.AtWar(s, spain.Id, persia.Id); day++)
    {
        AiWorldService.SetRelation(s, spain, persia, -100);   // (a standing grudge: peace-time drift would cool it)
        p.Gold = 1e12;
        eng.AdvanceOneDay();
    }
    var declared = s.Wars.FirstOrDefault(w => w.AggressorId == spain.Id && w.DefenderId == persia.Id);
    Check(declared is not null && spain.Soldiers < persia.Soldiers, $"the weaker country declared war on the stronger one (after {day} days)");
    Check(declared?.Aim == WarAim.Humiliation && HasLog(eng, "declared war on Iran — to humble a hated rival"), "for the reason a ruler would give: a grudge, recorded as the war's aim and shown in the Movement Report");
}

using (var eng = NewDipEngine(104))
{
    // The war's aim is fixed when it is declared, whatever happens to the countries' regard afterwards.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    spain.Units = UnitCatalog.SeedArmy(9000); persia.Units = UnitCatalog.SeedArmy(1500);
    Neighbours(s, spain, persia, 10);
    AiWorldService.DeclareWar(s, spain, persia, new Random(1));
    var war = s.Wars.Single(w => w.Links(spain.Id, persia.Id));
    Check(war.Aim == WarAim.Conquest && HasLog(eng, "declared war on Iran — it covets its lands"), "a war declared for land is a war of conquest");
    AiWorldService.SetRelation(s, spain, persia, -100);
    Check(AiWorldService.Motives(s, spain, persia).Aim != WarAim.Conquest && war.Aim == WarAim.Conquest, "later hatred changes what the ruler would want now, but not what this war was fought for");
}

using (var eng = NewDipEngine(105))
{
    // No limit on alliances: six friends side by side end up allied with each other, five alliances each.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var ids = new[] { "france", "spain", "persia", "mughal", "japan", "russia" };
    var club = ids.Select(id => Ai(eng, id)).ToList();
    foreach (var n in s.OtherNations.Except(club)) n.MapX += 100_000;
    p.MapX += 100_000;
    foreach (var n in club) { n.MapX = club[0].MapX; n.MapY = club[0].MapY; }
    for (int i = 0; i < club.Count; i++)
        for (int j = i + 1; j < club.Count; j++)
        {
            AiWorldService.AddRelation(s, club[i], club[j], 200);
            TreatyService.Add(s, TreatyType.NonAggression, club[i], club[j], 20_000);   // (no war breaks the friendship before the alliances are made)
        }
    for (int i = 0; i < 3000; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    var counts = club.Select(n => AiWorldService.AlliesOf(s, n).Count).ToList();
    Check(counts.All(c => c == club.Count - 1), $"every country allied with all five friends (allies each: {string.Join(", ", counts)})");
    Check(s.Treaties.Count(t => t.Type == TreatyType.DefensiveAlliance && club.Any(n => t.Involves(n.Id))) == club.Count * (club.Count - 1) / 2, "fifteen alliances among the six");
}

Console.WriteLine("== 52. Winners settle the war by what it was for ==");
using (var eng = NewDipEngine(93))
{
    // Conquest: the winner annexes, and its other wars end with the loser. How strong the loser is does not matter.
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    spain.Units = UnitCatalog.SeedArmy(4000); france.Units = UnitCatalog.SeedArmy(60_000);
    var ally = Ai(eng, "persia");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = france.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    s.Wars.Add(new WarRecord { AggressorId = ally.Id, DefenderId = france.Id, StartDate = s.CurrentDate });
    bool loserStronger = ArmyHelper.ArmyPower(france.Units) > ArmyHelper.ArmyPower(spain.Units) * 10;
    VictoryService.ResolveForAi(s, spain, france);
    Check(loserStronger && france.IsEliminated, "a war of conquest ends in annexation even when the loser's army is many times the winner's: there is no relative-power condition");
    Check(!s.Wars.Any(w => w.Involves(france.Id)) && !HasLog(eng, "took spoils instead"), "the loser's other wars end with it, and nothing is 'plundered instead'");
}

using (var eng = NewDipEngine(94))
{
    // No cooldown for a winner, no spacing between annexations anywhere: independent wars end in independent annexations, even on one day.
    var s = eng.State; var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter"); var nepal = Ai(eng, "nepal");
    var persia = Ai(eng, "persia"); var japan = Ai(eng, "japan");
    foreach (var (w, l) in new[] { (spain, easter), (spain, nepal), (persia, japan) })
        s.Wars.Add(new WarRecord { AggressorId = w.Id, DefenderId = l.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    VictoryService.ResolveForAi(s, spain, easter);
    VictoryService.ResolveForAi(s, spain, nepal);
    VictoryService.ResolveForAi(s, persia, japan);
    Check(easter.IsEliminated && nepal.IsEliminated && japan.IsEliminated, "one winner annexes two countries back to back, and another annexes on the same day");
}

using (var eng = NewDipEngine(95))
{
    // Humiliation: the loser's wealth is taken, it survives, and the war ends.
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = france.Id, StartDate = s.CurrentDate, Aim = WarAim.Humiliation });
    double w0 = spain.Gold, l0 = france.Gold;
    VictoryService.ResolveForAi(s, spain, france);
    double taken = Math.Floor(l0 * Balance.VictorySpoilsFraction);
    Check(!france.IsEliminated && Math.Abs(spain.Gold - (w0 + taken)) < 0.01 && Math.Abs(france.Gold - (l0 - taken)) < 0.01, "a war to humble a rival takes its wealth and leaves it standing");
    Check(!AiWorldService.AtWar(s, spain.Id, france.Id) && HasLog(eng, "the rival is humbled"), "the war between them ends");
    Check(Math.Abs(AiWorldService.Relation(s, spain, france) - Balance.AiWarPeaceRelation) < 0.5, "leaving them at the post-war regard");
}

using (var eng = NewDipEngine(96))
{
    // Containment: the menace is beaten back; nothing is taken.
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = france.Id, StartDate = s.CurrentDate, Aim = WarAim.Containment });
    double w0 = spain.Gold, l0 = france.Gold;
    VictoryService.ResolveForAi(s, spain, france);
    Check(!france.IsEliminated && spain.Gold == w0 && france.Gold == l0, "a war to contain a menace takes nothing");
    Check(!AiWorldService.AtWar(s, spain.Id, france.Id) && HasLog(eng, "the threat is broken"), "and ends in a white peace");
}

using (var eng = NewDipEngine(97))
{
    // A conquest the Assembly forbids is not converted into plunder: the battle is won, the war goes on.
    var s = eng.State; var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    var ban = new ActiveAssemblyPolicy { TypeId = "annexation_ban", TargetId = easter.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(120) };
    s.ActiveAssemblyPolicies.Add(ban);
    double w0 = spain.Gold, l0 = easter.Gold;
    VictoryService.ResolveForAi(s, spain, easter);
    Check(!easter.IsEliminated && HasLog(eng, "cannot annex it"), "a country under an Assembly ban is not annexed, and the refusal is logged");
    Check(spain.Gold == w0 && easter.Gold == l0 && AiWorldService.AtWar(s, spain.Id, easter.Id), "nothing is taken instead, and the war goes on");
    s.ActiveAssemblyPolicies.Remove(ban);
    VictoryService.ResolveForAi(s, spain, easter);
    Check(easter.IsEliminated, "once the ban ends, the next victory annexes it");
}

using (var eng = NewDipEngine(97))
{
    // A sovereignty guarantee is not an Assembly ban: it does not stop an AI victor annexing the beaten country.
    var s = eng.State; var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, s.PlayerNation, easter, 730);
    VictoryService.ResolveForAi(s, spain, easter);
    Check(easter.IsEliminated && !HasLog(eng, "cannot annex it"), "a guaranteed country is annexed by the victor all the same");
}

using (var eng = NewDipEngine(106))
{
    // The aim is the one the aggressor went to war for, not what it feels at the moment of victory.
    var s = eng.State; var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter");
    Neighbours(s, spain, easter, -100);   // (it now hates the country: left to its present motives it would only plunder)
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    VictoryService.ResolveForAi(s, spain, easter);
    Check(easter.IsEliminated, "a war that was declared for conquest ends in conquest");
}

using (var eng = NewDipEngine(107))
{
    // A defender that wins, or an ally with no war of its own, has no declared aim: its motives of the moment decide.
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    Neighbours(s, spain, france, -100);
    france.Population = spain.Population / 30; france.Units = UnitCatalog.SeedArmy(500);
    Check(AiWorldService.Motives(s, spain, france).Aim == WarAim.Humiliation, "(the defender hates its invader and has little to gain from its land)");
    s.Wars.Add(new WarRecord { AggressorId = france.Id, DefenderId = spain.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    double w0 = spain.Gold, l0 = france.Gold;
    VictoryService.ResolveForAi(s, spain, france);   // spain, the defender, wins the field
    Check(!france.IsEliminated && spain.Gold > w0 && france.Gold < l0 && !AiWorldService.AtWar(s, spain.Id, france.Id),
        "the defender does not inherit the invader's aim: it humbles it for its hatred instead");

    var persia = Ai(eng, "persia"); var easter = Ai(eng, "easter");
    persia.MapX = easter.MapX + 100_000;   // (out of reach, and on neutral terms: no motive at all)
    AiWorldService.SetRelation(s, persia, easter, 20);
    double persiaGold = persia.Gold;
    VictoryService.ResolveForAi(s, persia, easter);
    Check(!easter.IsEliminated && persia.Gold == persiaGold, "an ally that wins a battle in someone else's war, with no motive of its own, annexes and takes nothing");
}

Console.WriteLine("== 53. The AI world over four years: stability, determinism, independence, saves ==");
(List<string> Events, int Eliminated, int Declarations, int Alliances, int Peaces, bool Ok, string Problem) RunWorld(int seed)
{
    using var eng = NewDipEngine(seed);
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    bool ok = true; string problem = "";
    for (int i = 1; i <= 1460; i++)
    {
        s.PlayerNation.Gold = 1_000_000_000_000;
        eng.AdvanceOneDay();
        if (i % 30 != 0) continue;
        foreach (var w in s.Wars)
        {
            var a = s.OtherNations.FirstOrDefault(n => n.Id == w.AggressorId); var b = s.OtherNations.FirstOrDefault(n => n.Id == w.DefenderId);
            if (a is null || b is null || a.IsEliminated || b.IsEliminated) { ok = false; problem = "a war involves a country that no longer exists"; }
            else if (AiWorldService.Allied(s, a.Id, b.Id)) { ok = false; problem = $"allies {a.Name} and {b.Name} are at war"; }
        }
        if (s.Wars.GroupBy(w => AiWorldService.PairKey(w.AggressorId, w.DefenderId)).Any(g => g.Count() > 1)) { ok = false; problem = "two wars between the same pair"; }
        if (s.OtherNations.Any(n => n.Soldiers < 0 || n.Population < 0 || n.Gold < 0)) { ok = false; problem = "a negative army, population or treasury"; }
        if (s.OtherNations.Where(n => n.IsEliminated).Any(n => s.Treaties.Any(t => t.Involves(n.Id)))) { ok = false; problem = "an eliminated country still has treaties"; }
    }
    var events = s.Movements.Select(m => $"{m.Date:yyyy-MM-dd} {m.Kind} {m.Text}").ToList();
    return (events, s.OtherNations.Count(n => n.IsEliminated),
        s.Movements.Count(m => m.Kind == MovementKind.War && m.Text.Contains("declared war") && m.ToId != s.PlayerNation.Id),
        s.Movements.Count(m => m.Text.Contains("formed a defensive alliance")),
        s.Movements.Count(m => m.Text.Contains("made peace")), ok, problem);
}
{
    var runA = RunWorld(301);
    var runB = RunWorld(301);
    Check(runA.Ok, "four years of AI world: every invariant held at every month-end (" + runA.Problem + ")");
    Check(runA.Declarations >= 8 && runA.Alliances >= 6 && runA.Peaces >= 4, $"the world is alive: {runA.Declarations} wars declared, {runA.Alliances} alliances formed, {runA.Peaces} peaces made");
    Check(runA.Eliminated >= 1 && runA.Eliminated <= 10, $"...but not a bloodbath: {runA.Eliminated} of 40 countries eliminated in four years");
    Check(runA.Events.SequenceEqual(runB.Events), "the same seed gives the same history, event for event (determinism)");
    Check(!RunWorld(302).Events.SequenceEqual(runA.Events), "a different seed gives a different history");
}

{
    // The AI world never disturbs the rolls the rest of the simulation makes.
    var simA = new SimulationService(seed: 5); var simB = new SimulationService(seed: 5);
    using var engA = new GameEngine(simA, new SaveService(saveFolder));
    using var engB = new GameEngine(simB, new SaveService(saveFolder));
    engB.State.AiWorldStartDate = engB.State.CurrentDate;   // B has a living world, A a calm one
    for (int i = 0; i < 200; i++) { engA.State.PlayerNation.Gold = 1e12; engB.State.PlayerNation.Gold = 1e12; engA.AdvanceOneDay(); engB.AdvanceOneDay(); }
    Check(engB.State.Movements.Any(m => m.Text.Contains("declared war") || m.Text.Contains("formed a defensive alliance")), "(B's world really was active)");
    Check(!engA.State.Movements.Any(m => m.Text.Contains("declared war") || m.Text.Contains("formed a defensive alliance")), "(and A's world really was calm)");
    Check(simA.NextDouble() == simB.NextDouble(), "the main random stream is untouched by the AI world");
}

using (var eng = NewDipEngine(98))
{
    // Eliminated countries leave no wars or alliances behind; the world is visible in the Movement Report.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var persia = Ai(eng, "persia"); var france = Ai(eng, "france"); var spain = Ai(eng, "spain"); var mughal = Ai(eng, "mughal");
    TreatyService.Add(s, TreatyType.DefensiveAlliance, persia, mughal);
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    s.Wars.Add(new WarRecord { AggressorId = persia.Id, DefenderId = spain.Id, StartDate = s.CurrentDate });
    Check(s.Movements.Any(m => m.Text.Contains("declared war")) && MovementReport.Events(s).Count == 0 && MovementReport.Events(s, persia.Id).Count == 0
          && MovementReport.EventsByState(s).Count == 0,
        "AI-to-AI events are recorded as world history but are not in the player's Movement Report");
    Warfare.AnnexNation(s, spain, persia);
    Check(!s.Wars.Any(w => w.Involves(persia.Id)) && !s.Treaties.Any(t => t.Involves(persia.Id)), "annexing a country ends its wars and alliances");
}

using (var eng = NewDipEngine(99))
{
    // Save / load of the AI world; old saves load calm.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    for (int i = 0; i < 250; i++) { s.PlayerNation.Gold = 1e12; eng.AdvanceOneDay(); }
    // Two wars with known aims (one without), so the aim is part of what is saved whatever the world happened to do.
    var easter = Ai(eng, "easter"); var nepal = Ai(eng, "nepal"); var japan = Ai(eng, "japan");
    s.Wars.RemoveAll(w => w.Involves(easter.Id) || w.Involves(nepal.Id) || w.Involves(japan.Id));
    s.Wars.Add(new WarRecord { AggressorId = easter.Id, DefenderId = nepal.Id, StartDate = s.CurrentDate, Aim = WarAim.Humiliation });
    s.Wars.Add(new WarRecord { AggressorId = japan.Id, DefenderId = nepal.Id, StartDate = s.CurrentDate });
    var wars = s.Wars.Select(w => (w.AggressorId, w.DefenderId, w.StartDate, w.Aim)).ToList();
    var relations = s.AiRelations.ToDictionary(kv => kv.Key, kv => kv.Value);
    Check(relations.Count > 0, "(there is AI-world state to save)");
    var france = Ai(eng, "france"); int fb = france.BaseSoldiers; long fp = france.BasePopulation;
    await eng.SaveAsync();
    s.Wars.Clear(); s.AiRelations.Clear(); france.BaseSoldiers = 0;
    await eng.LoadAsync();
    var l = eng.State;
    Check(l.Wars.Select(w => (w.AggressorId, w.DefenderId, w.StartDate, w.Aim)).SequenceEqual(wars), "AI wars survive save / load, with what each was fought for");
    Check(l.AiRelations.Count == relations.Count && relations.All(kv => Math.Abs(l.AiRelations[kv.Key] - kv.Value) < 1e-9), "AI relations survive save / load");
    Check(l.AiWorldStartDate == s.AiWorldStartDate && Ai(eng, "france").BaseSoldiers == fb && Ai(eng, "france").BasePopulation == fp,
        "the AI world's start date and each country's army base survive");

    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    foreach (var key in new[] { "Wars", "AiRelations", "AiWorldStartDate" }) node.Remove(key);
    foreach (var n in node["OtherNations"]!.AsArray()) { n!.AsObject().Remove("BaseSoldiers"); n.AsObject().Remove("BasePopulation"); }
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.Wars.Count == 0 && old.AiRelations.Count == 0 && old.AiWorldStartDate == new DateOnly(1601, 1, 1)
          && old.OtherNations.All(n => n.BaseSoldiers == 0 && n.BasePopulation == 0), "an old save without the AI world loads calm, with every default");

    // A save from the build that still had the annexation cooldown (the two removed fields) loads without complaint.
    var older = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    older["LastAiAnnexation"] = "1601-03-01";
    foreach (var n in older["OtherNations"]!.AsArray()) n!.AsObject()["LastAnnexed"] = "1601-02-01";
    Check(JsonSerializer.Deserialize<GameState>(older.ToJsonString()) is not null, "a save that still carries the old annexation-cooldown fields loads (they are ignored)");
}

Console.WriteLine("== 54. The Movement Report is about the player's country ==");
using (var eng = NewDipEngine(108))
{
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var mughal = Ai(eng, "mughal"); var japan = Ai(eng, "japan"); var easter = Ai(eng, "easter");

    // Wars, alliances and conquests between countries that have nothing to do with the player are world news.
    AiWorldService.DeclareWar(s, france, mughal, new Random(1));
    s.Wars.Add(new WarRecord { AggressorId = japan.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    VictoryService.ResolveForAi(s, japan, easter);
    TreatyService.Add(s, TreatyType.DefensiveAlliance, france, mughal);
    s.LogMovement(MovementKind.Treaty, MovementStatus.Completed, france, mughal, "🛡 France and Mughal Empire formed a defensive alliance.");
    Check(s.Movements.Count >= 3 && easter.IsEliminated, "(the world did record a war, a conquest and an alliance)");
    Check(MovementReport.Events(s).Count == 0 && MovementReport.ActiveMissions(s).Count == 0, "none of it is in the player's report: no events, no missions");
    Check(s.ActiveWarnings.Count == 0, "and none of it interrupts the player");

    // What touches the player is listed: their own movements...
    eng.GiveGift(spain.Id, 100);
    Check(MovementReport.Events(s).Count == 1 && MovementReport.Events(s)[0].FromId == p.Id, "a gift the player sent is listed");
    AiWorldService.DeclareWarOnPlayer(s, persia);
    Check(MovementReport.Events(s).Any(m => m.FromId == persia.Id && m.ToId == p.Id && m.Text.Contains("declared war")), "a war declared on the player is listed");

    // ...and what happens to the player's allies.
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    int before = MovementReport.Events(s).Count;
    AiWorldService.DeclareWar(s, mughal, spain, new Random(1));
    Check(MovementReport.Events(s).Count > before && MovementReport.Events(s).Any(m => m.ToId == spain.Id && m.Text.Contains("declared war")), "a war on the player's ally is listed");
    Check(MovementReport.Events(s, spain.Id).Any(m => m.FromId == mughal.Id), "...under the ally");

    // The fall of an ally is listed and warned about, even though the alliance ends with it.
    s.Wars.Add(new WarRecord { AggressorId = japan.Id, DefenderId = spain.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    s.ActiveWarnings.Clear();
    VictoryService.ResolveForAi(s, japan, spain);
    Check(spain.IsEliminated && MovementReport.Events(s).Any(m => m.Text.Contains("Iberian Union has been annexed")), "the fall of the player's ally is listed");
    Check(s.ActiveWarnings.Any(w => w.Contains("Iberian Union has been annexed")), "and the player is warned (the alliance is checked before it is wiped)");
}

using (var eng = NewDipEngine(109))
{
    // An enemy of the player falling to someone else is listed; an unrelated country falling is not.
    var s = eng.State; var p = s.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var easter = Ai(eng, "easter"); var nepal = Ai(eng, "nepal");
    AiWorldService.DeclareWarOnPlayer(s, persia);
    int before = MovementReport.Events(s).Count;
    Warfare.AnnexNation(s, france, nepal);
    Check(MovementReport.Events(s).Count == before, "a country the player has no dealings with is annexed: not listed");
    Warfare.AnnexNation(s, france, persia);
    Check(MovementReport.Events(s).Any(m => m.Text.Contains("Iran has been annexed")), "the player's enemy is annexed by someone else: listed");

    // The filter: the picker only offers countries that appear in the player's report, and filters within it.
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, p, easter, "to Easter");
    Check(MovementReport.Events(s, easter.Id).Single().Text == "to Easter" && MovementReport.Events(s, nepal.Id).Count == 0,
        "filtering by a country shows only what the player has with it");
}

Console.WriteLine("== 55. World news (its own section, apart from the Movement Report): military and other news ==");
Check(new[] { MovementKind.War, MovementKind.March, MovementKind.Troops }.All(WorldNewsReport.IsMilitary)
      && Enum.GetValues<MovementKind>().Where(k => k is not (MovementKind.War or MovementKind.March or MovementKind.Troops)).All(k => !WorldNewsReport.IsMilitary(k)),
    "wars, armies and soldiers lent are military news; every other kind (treaties, gold, goods, colonies, missions, Assembly, diplomacy) is other news");
using (var eng = NewDipEngine(110))
{
    var s = eng.State; var p = s.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var mughal = Ai(eng, "mughal"); var japan = Ai(eng, "japan"); var easter = Ai(eng, "easter");
    var d0 = s.CurrentDate;

    s.LogMovement(MovementKind.War, MovementStatus.Completed, france, persia, "w1");        // military
    s.CurrentDate = d0.AddDays(1);
    s.LogMovement(MovementKind.March, MovementStatus.UnderWay, mughal, japan, "w2");        // military
    s.LogMovement(MovementKind.Troops, MovementStatus.Completed, spain, easter, "w3");      // military
    s.LogMovement(MovementKind.Treaty, MovementStatus.Completed, france, spain, "o1");      // other
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, persia, mughal, "o2");       // other
    s.LogMovement(MovementKind.Mission, MovementStatus.Completed, japan, france, "o3");     // other

    // None of this is the player's business: the player's own report stays empty.
    Check(MovementReport.Events(s).Count == 0, "(the player's report has none of it)");

    Check(string.Join(",", WorldNewsReport.Items(s, true).Select(x => x.Text)) == "w3,w2,w1", "military world news: wars, marches and troops, newest first");
    Check(string.Join(",", WorldNewsReport.Items(s, false).Select(x => x.Text)) == "o3,o2,o1", "other world news: treaties, gold and missions, newest first");
    Check(WorldNewsReport.Items(s).Count == 6, "both together are every world record");

    // What concerns the player is not world news: it is in the player's report instead, never in both.
    eng.GiveGift(persia.Id, 100);
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    s.LogMovement(MovementKind.War, MovementStatus.Completed, france, spain, "ally attacked");   // touches the player's ally
    Check(WorldNewsReport.Items(s).Count == 6 && WorldNewsReport.Items(s).All(x => x.Text.StartsWith("w") || x.Text.StartsWith("o")),
        "the player's own movements and wars on the player's allies are not world news");
    Check(MovementReport.Events(s).Any(x => x.Text == "ally attacked") && MovementReport.Events(s).Count == 2, "...they are in the player's report");

    // Filter and grouping.
    Check(string.Join(",", WorldNewsReport.Items(s, null, persia.Id).Select(x => x.Text)) == "o2,w1", "filtering by a country shows its news, military and other");
    Check(WorldNewsReport.Items(s, true, persia.Id).Single().Text == "w1" && WorldNewsReport.Items(s, false, easter.Id).Count == 0, "...and respects the military / other split");
    var groups = WorldNewsReport.ByState(s, true);
    Check(groups.Select(g => g.StateId).SequenceEqual(new[] { spain.Id, mughal.Id, france.Id }) && groups.All(g => g.Items.Count == 1),
        "grouped by the country that acted, the one with the latest news first");
    Check(WorldNewsReport.Items(s, null, p.Id).Count == 0, "nothing in the world news involves the player");
    Check(!MovementReport.Events(s).Any(e => WorldNewsReport.Items(s).Contains(e)) && MovementReport.Events(s).Count + WorldNewsReport.Items(s).Count == s.Movements.Count,
        "the two sections are separate: every record is in exactly one of the Movement Report and the World news");
}

using (var eng = NewDipEngine(112))
{
    // Plunder is gold and goods, but it is a result of war: military news, not "other".
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = france.Id, StartDate = s.CurrentDate, Aim = WarAim.Humiliation });
    VictoryService.ResolveForAi(s, spain, france);
    var military = WorldNewsReport.Items(s, true);
    Check(military.Any(x => x.Kind == MovementKind.Gold && x.Text.Contains("took")) && military.Any(x => x.Kind == MovementKind.Goods && x.Text.Contains("took")),
        "gold and goods taken as spoils are in the military news");
    Check(WorldNewsReport.Items(s, false).All(x => !x.Text.Contains("took")), "...and not in the other news");
    s.LogMovement(MovementKind.Gold, MovementStatus.Completed, spain, france, "a plain gift");
    Check(WorldNewsReport.Items(s, false).Single().Text == "a plain gift", "while a plain gift between the same two countries is other news");
}

using (var eng = NewDipEngine(111))
{
    // The AI world's own news lands in the right half: a war declared is military, an alliance formed is other.
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var mughal = Ai(eng, "mughal"); var japan = Ai(eng, "japan");
    foreach (var n in s.OtherNations.Except(new[] { mughal, japan })) n.MapX += 100_000;
    s.PlayerNation.MapX += 500_000;   // (far from everyone, so nobody has a quarrel with the player)
    mughal.MapX = japan.MapX; mughal.MapY = japan.MapY;
    AiWorldService.AddRelation(s, mughal, japan, 200);
    for (int i = 0; i < 600; i++) { s.PlayerNation.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(WorldNewsReport.Items(s, false).Any(x => x.Text.Contains("formed a defensive alliance")), "an alliance between two other countries is in the world's other news");
    AiWorldService.DeclareWar(s, france, persia, new Random(1));
    Check(WorldNewsReport.Items(s, true).Any(x => x.Text.Contains("declared war on Iran")) && !WorldNewsReport.Items(s, false).Any(x => x.Text.Contains("declared war")),
        "a war declared between two other countries is in the world's military news, and only there");
    Check(MovementReport.Events(s).Count == 0 && !s.ActiveWarnings.Any(w => w.Contains("war", StringComparison.OrdinalIgnoreCase)), "and the player's own report and war warnings stay quiet");
}

Console.WriteLine("== 56. Trade: three product groups, MAX quantities, the price really charged ==");
{
    List<string> InGroup(string key) => TradeCatalog.InGroup(TradeCatalog.Groups.Single(g => g.Key == key)).Select(p => p.Id).ToList();
    Check(TradeCatalog.Groups.Select(g => g.Title).SequenceEqual(new[] { "Military", "Food", "Minerals" }), "products are sorted into three groups: Military, Food, Minerals");
    Check(TradeCatalog.All.All(p => TradeCatalog.Groups.Count(g => TradeCatalog.InGroup(g).Any(x => x.Id == p.Id)) == 1), "every product is in exactly one group");
    Check(InGroup("military").SequenceEqual(new[] { "helmet", "dagger", "pike", "shotgun", "arquebus", "shipparts" }), "Military: the equipment");
    Check(InGroup("minerals").SequenceEqual(new[] { "wood", "stone", "iron", "copper", "lead" }), "Minerals: the raw materials");
    Check(InGroup("food").Count == 12 && InGroup("food").Contains("wheat") && InGroup("food").Contains("bread") && InGroup("food").Contains("perfume"), "Food: the food and everyday goods");
}

using (var eng = NewDipEngine(113))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    void SetStock(Nation n, string id, double amount) => n.AdjustProductStock(id, amount - n.GetProductStock(id));

    Embassy(eng, persia);
    // The price the popup shows is the price that is charged: import law and trade agreement included.
    double plain = eng.BuyPricePer1000(persia.Id, "wheat");
    Check(Math.Abs(plain - MarketPricing.PricePer1000("wheat", persia.Id) * LawService.ImportPriceMult(p)) < 1e-9, "the buy price is the market price with the import law");
    TreatyService.Add(s, TreatyType.TradeAgreement, p, persia);
    Check(Math.Abs(eng.BuyPricePer1000(persia.Id, "wheat") - plain * Balance.TradeAgreementImportMult) < 1e-9, "...and a trade agreement makes it cheaper");
    SetStock(persia, "wheat", 100_000); p.Gold = 1_000_000;
    double before = p.Gold;
    Check(eng.BuyProduct(persia.Id, "wheat", 2000) is null
          && Math.Abs((before - p.Gold) - MarketPricing.TotalValue(eng.BuyPricePer1000(persia.Id, "wheat"), 2000)) < 0.01, "a purchase costs exactly what the quoted price says");

    // MAX to buy: whatever the seller will part with (half its stock), or what the gold pays for.
    SetStock(persia, "wheat", 1001); p.Gold = 1e9;
    Check(eng.MaxBuyQuantity(persia.Id, "wheat") == 500, "MAX to buy is limited by the seller: half of 1,001, in whole units");
    Check(eng.BuyProduct(persia.Id, "wheat", 500) is null, "...and exactly that much can be bought");
    SetStock(persia, "wheat", 2_000_000); p.Gold = 50;
    double maxBuy = eng.MaxBuyQuantity(persia.Id, "wheat");
    Check(maxBuy > 0 && maxBuy < 1_000_000 && MarketPricing.TotalValue(eng.BuyPricePer1000(persia.Id, "wheat"), maxBuy) <= 50, "MAX to buy is limited by the gold when the player is poor");
    Check(eng.BuyProduct(persia.Id, "wheat", maxBuy) is null, "...and that much can be bought");
    SetStock(persia, "wheat", 2_000_000); p.Gold = 50;
    Check(eng.BuyProduct(persia.Id, "wheat", maxBuy + 10)?.Contains("gold") == true, "a little more than MAX is refused for lack of gold");
    p.Gold = 0;
    Check(eng.MaxBuyQuantity(persia.Id, "wheat") == 0 && eng.MaxBuyQuantity("nobody", "wheat") == 0 && eng.MaxBuyQuantity(p.Id, "wheat") == 0, "MAX to buy is zero with no gold, an unknown seller, or yourself");
}

using (var eng = NewDipEngine(114))
{
    var s = eng.State; var p = s.PlayerNation; var mughal = Ai(eng, "mughal");
    void SetStock(Nation n, string id, double amount) => n.AdjustProductStock(id, amount - n.GetProductStock(id));
    double price = MarketPricing.PricePer1000("wheat", p.Id);

    Embassy(eng, mughal);
    // MAX to sell: what the player has (whole units), or what the buyer can pay for.
    mughal.Gold = 1e12; SetStock(p, "wheat", 1234.7);
    Check(eng.MaxSellQuantity(mughal.Id, "wheat", price) == 1234, "MAX to sell is the stock, in whole units");
    Check(eng.SellProduct(mughal.Id, "wheat", 1234, price) is null, "...and exactly that much can be sold");
    SetStock(p, "wheat", 5_000_000); mughal.Gold = 100;
    double maxSell = eng.MaxSellQuantity(mughal.Id, "wheat", price);
    Check(maxSell > 0 && maxSell < 5_000_000 && MarketPricing.TotalValue(price, maxSell) <= 100, "MAX to sell is limited by what the buyer can pay");
    Check(eng.SellProduct(mughal.Id, "wheat", maxSell, price) is null, "...and that much can be sold");
    SetStock(p, "wheat", 5_000_000);
    Check(eng.SellProduct(mughal.Id, "wheat", maxSell + 10, price)?.Contains("cannot afford") == true, "a little more than MAX is refused: the buyer cannot afford it");

    // A product with nothing in stock can still be picked; the checks are at the end.
    SetStock(p, "wheat", 0);
    Check(eng.MaxSellQuantity(mughal.Id, "wheat", price) == 0, "MAX to sell is zero with none in stock");
    Check(eng.SellProduct(mughal.Id, "wheat", 100, price) == "Only 0 available.", "selling a product you have none of is refused with a clear message");
    Check(TradeCatalog.All.Count(x => x.CanSell && p.GetProductStock(x.Id) <= 0) > 5, "(most products are out of stock at the start, yet they are all sellable on the list)");
}

Console.WriteLine("== 57. Inbox: three categories, filed by the type of event ==");
{
    var all = Enum.GetValues<InboxTopic>();
    var intel = all.Where(t => InboxCatalog.CategoryOf(t) == InboxCategory.Intelligence).ToList();
    var military = all.Where(t => InboxCatalog.CategoryOf(t) == InboxCategory.Military).ToList();
    var other = all.Where(t => InboxCatalog.CategoryOf(t) == InboxCategory.Other).ToList();
    Check(intel.Count + military.Count + other.Count == all.Length && intel.Count > 0 && military.Count > 0 && other.Count > 0, "every event topic is filed in exactly one of the three categories");
    Check(intel.SequenceEqual(new[] { InboxTopic.SpyNetwork, InboxTopic.SpyMission, InboxTopic.SpyCaught, InboxTopic.TroopMovement, InboxTopic.ForeignStrength }),
        "Intelligence: spy networks and missions, caught spies, enemy troop movements, foreign strength");
    Check(new[] { InboxTopic.WarDeclared, InboxTopic.Battle, InboxTopic.Peace, InboxTopic.Invasion, InboxTopic.CallToArms, InboxTopic.AllyAssistance, InboxTopic.ArmyMovement, InboxTopic.Reinforcements }.All(military.Contains),
        "Military: war and peace, battles, invasions, calls to arms, allied help, army movements, reinforcements");
    Check(new[] { InboxTopic.Diplomacy, InboxTopic.Aid, InboxTopic.Trade, InboxTopic.Shortage, InboxTopic.Production, InboxTopic.RulerRating, InboxTopic.Religion, InboxTopic.Research }.All(other.Contains),
        "Other: diplomacy, aid, trade, shortages, production, ruler rating, religion, research");
    Check(all.All(t => InboxCatalog.TopicTitle(t).Length > 0 && InboxCatalog.TopicIcon(t) != "✉️"), "every topic has a title and its own icon");
    Check(Enum.GetValues<InboxCategory>().All(c => InboxCatalog.CategoryName(c).Length > 0 && InboxCatalog.CategoryIcon(c).Length > 0), "every category has a name and an icon");
}

using (var eng = NewDipEngine(115))
{
    var s = eng.State; var persia = Ai(eng, "persia");
    Check(s.Inbox.Count == 0 && InboxService.UnreadCount(s) == 0, "a new game's inbox is empty: nothing is made up to fill it");
    for (int i = 0; i < 30; i++) { s.PlayerNation.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(s.Inbox.All(m => m.Category != InboxCategory.Military || m.Topic != InboxTopic.WarDeclared), "(and a quiet month does not invent events)");

    // Filing follows the event's type, not its wording.
    s.Inbox.Clear();
    InboxService.Notify(s, InboxTopic.Diplomacy, "spy war battle army intelligence", persia.Id);
    InboxService.Notify(s, InboxTopic.SpyCaught, "A treaty and a gift", persia.Id);
    Check(InboxService.Messages(s, InboxCategory.Other).Single().Summary.Contains("battle") && InboxService.Messages(s, InboxCategory.Intelligence).Single().Summary.Contains("treaty"),
        "a message is filed by its event type, whatever its text says");

    var m = InboxService.Messages(s, InboxCategory.Other).Single();
    Check(!m.Read && m.Date == s.CurrentDate && m.CountryId == persia.Id && m.Title == "Diplomacy — Iran" && m.Details == m.Summary, "a new message is unread, dated, about its country, with a title and details");
    InboxService.Notify(s, InboxTopic.Aid, "second", title: "Custom title");
    Check(InboxService.Messages(s, InboxCategory.Other).Select(x => x.Summary).SequenceEqual(new[] { "second", "spy war battle army intelligence" }), "a category lists its messages newest first");
    Check(InboxService.Messages(s, InboxCategory.Military).Count == 0, "other categories do not show them (keeping each in its own tab)");

    // Reading.
    Check(InboxService.UnreadCount(s) == 3 && InboxService.UnreadCount(s, InboxCategory.Other) == 2 && InboxService.UnreadCount(s, InboxCategory.Intelligence) == 1 && InboxService.UnreadCount(s, InboxCategory.Military) == 0,
        "unread counts per tab and in total");
    int changes = 0; eng.StateChanged += () => changes++;
    var opened = eng.OpenMessage(m.Id);
    Check(opened is not null && opened.Read && InboxService.UnreadCount(s, InboxCategory.Other) == 1 && changes == 1, "opening a message marks it as read (and refreshes the screens)");
    eng.OpenMessage(m.Id);
    Check(changes == 1 && InboxService.UnreadCount(s) == 2, "opening it again changes nothing");
    Check(eng.OpenMessage("no-such-message") is null, "an unknown message cannot be opened");
}

using (var eng = NewDipEngine(116))
{
    // Past the cap, the oldest read messages go first; unread ones are kept.
    var s = eng.State;
    for (int i = 0; i < 10; i++) InboxService.Notify(s, InboxTopic.Aid, $"unread {i}");
    for (int i = 0; i < InboxService.MaxMessages + 100; i++) InboxService.MarkRead(s, InboxService.Notify(s, InboxTopic.Trade, $"read {i}").Id);
    Check(s.Inbox.Count == InboxService.MaxMessages && s.Inbox.Count(x => !x.Read) == 10, $"the inbox keeps {InboxService.MaxMessages} messages and drops old read ones before unread ones");
}

Console.WriteLine("== 58. Inbox: messages come from real events, once each ==");
using (var eng = NewDipEngine(117))
{
    // War: declared on the player (military), on the player's ally (military), between strangers (nothing).
    var s = eng.State; s.AiWorldStartDate = s.CurrentDate; var p = s.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var mughal = Ai(eng, "mughal"); var japan = Ai(eng, "japan");

    AiWorldService.DeclareWar(s, mughal, japan, new Random(1));
    Check(s.Inbox.Count == 0, "a war between two countries that have nothing to do with the player is not reported to the inbox");

    AiWorldService.DeclareWarOnPlayer(s, france, "it covets its lands");
    var war = InboxService.Messages(s, InboxCategory.Military).Single();
    Check(war.Topic == InboxTopic.WarDeclared && war.CountryId == france.Id && war.Summary.Contains("declared war") && !war.Read && s.Inbox.Count == 1,
        "a war declared on the player is one military message about the aggressor");
    for (int i = 0; i < 20; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(s.Inbox.Count(x => x.Topic == InboxTopic.WarDeclared) == 1, "the simulation updating does not repeat it");

    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    int before = s.Inbox.Count;
    AiWorldService.DeclareWar(s, persia, spain, new Random(1));
    var ally = s.Inbox.Skip(before).ToList();
    Check(ally.Count >= 1 && ally.All(x => x.Topic == InboxTopic.AllyAtWar && x.Category == InboxCategory.Military), "a war on the player's ally is a military message");
}

using (var eng = NewDipEngine(118))
{
    // Spies: a network reports on the country's strength; a mission reports its result. Both are intelligence.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 1e9; persia.Units = UnitCatalog.SeedArmy(4321); int army = persia.Soldiers;
    Check(eng.EstablishNetwork(persia.Id) is null, "(a spy network is set up)");
    var intel = InboxService.Messages(s, InboxCategory.Intelligence);
    Check(intel.Count == 2 && intel.Any(x => x.Topic == InboxTopic.SpyNetwork) && intel.Any(x => x.Topic == InboxTopic.ForeignStrength), "a new spy network gives two intelligence messages: the network, and its report");
    var report = intel.Single(x => x.Topic == InboxTopic.ForeignStrength);
    Check(report.Summary.Contains(army.ToString("N0")) && report.Details.Contains("Treasury") && report.CountryId == persia.Id, "the strength report carries the country's real army figure");
    p.Gold = 1e9;
    for (int i = 0; i < 5; i++) { persia.Gold = 1e6; net(eng, persia); }
    void net(GameEngine e, Nation n) { e.EstablishNetwork(n.Id); }
    Check(eng.SpySteal(persia.Id) is null, "(spies steal)");
    Check(InboxService.Messages(s, InboxCategory.Intelligence).Any(x => x.Topic == InboxTopic.SpyMission && x.Summary.Contains("stole")), "a spy mission's result is an intelligence message");
    Check(s.Inbox.Where(x => x.Category == InboxCategory.Intelligence).All(x => x.Topic is InboxTopic.SpyNetwork or InboxTopic.SpyMission or InboxTopic.SpyCaught or InboxTopic.ForeignStrength), "...and nothing else is filed there");
}

using (var eng = NewDipEngine(119))
{
    // Enemy army movements: an army marching on (or for) the player's ally, or setting out from a country the player's spies watch.
    var s = eng.State; var p = s.PlayerNation;
    var france = Ai(eng, "france"); var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var mughal = Ai(eng, "mughal");
    foreach (var n in new[] { france, persia, spain, mughal }) n.Units = UnitCatalog.SeedArmy(9000);
    s.SpyNetworks.Add(new SpyNetwork { TargetNationId = france.Id, TargetNationName = france.Name, Strength = 50 });   // the player's spies watch France
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);                                                          // Spain is the player's ally

    // (the march decision is a chance roll: keep trying until the army sets out)
    void March(Nation from, Nation to) { for (int seed = 1; seed < 200 && !TreatyService.HasMarchOn(s, from.Id, to.Id); seed++) AiWorldService.TryMarch(s, from, to, new Random(seed)); }
    List<InboxMessage> Reports() => InboxService.Messages(s, InboxCategory.Intelligence).Where(x => x.Topic == InboxTopic.TroopMovement).ToList();

    March(persia, mughal);
    Check(TreatyService.HasMarchOn(s, persia.Id, mughal.Id) && Reports().Count == 0, "an army of two countries nobody watches, marching on a stranger, is not reported");

    March(france, persia);
    var fromWatched = Reports();
    Check(fromWatched.Count == 1 && fromWatched[0].CountryId == france.Id && fromWatched[0].Summary.Contains("Our spies in France") && fromWatched[0].Summary.Contains("soldiers marching on Iran"),
        "an army that sets out from a country the player's spies watch is reported, once, as the spies' news");

    March(mughal, spain);
    March(spain, mughal);
    var forAlly = Reports().Where(x => x.CountryId == mughal.Id || x.CountryId == spain.Id).ToList();
    Check(forAlly.Count == 2, "armies marching on the player's ally, and the ally's own, are reported");
    Check(Reports().Count == 3 && Reports().All(x => x.Category == InboxCategory.Intelligence), "enemy army movements are intelligence (not military reports), and nothing was reported twice");
    int count = s.Inbox.Count;
    for (int i = 0; i < 5; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(Reports().Count == 3, "days passing do not repeat them");
}

using (var eng = NewDipEngine(120))
{
    // The player's own war: the battle, the victory decision and the answer to a call to arms are military; a treaty answer is other.
    var s = eng.State; var p = s.PlayerNation; var nepal = Ai(eng, "nepal"); var spain = Ai(eng, "spain"); var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(6000);
    nepal.Units = UnitCatalog.SeedArmy(3500);
    eng.DeclareWar(nepal.Id);
    Check(eng.LaunchInvasion(nepal.Id, 3000).ok, "(invasion launched)");
    int days = s.MarchingArmies[0].TotalDays;
    for (int i = 0; i < days; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    var mil = InboxService.Messages(s, InboxCategory.Military);
    Check(mil.Any(x => x.Topic == InboxTopic.Battle && x.CountryId == nepal.Id && x.Summary.Contains("Victory")), "a battle the player fought is a military message with its result");
    Check(mil.Count(x => x.Topic == InboxTopic.Victory) == 1, "...and so is the victory that asks for a decision, once");

    SetRating(spain, 30);
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    SetRating(spain, 30);
    var refused = eng.CallToArms(spain.Id, nepal.Id);
    Check(refused.Outcome == DipOutcome.Rejected && InboxService.Messages(s, InboxCategory.Military).Any(x => x.Topic == InboxTopic.CallToArms && x.CountryId == spain.Id), "a refused call to arms is a military message");

    SetRating(persia, 10);
    var declined = eng.ProposeAlliance(persia.Id);
    Check(declined.Outcome == DipOutcome.Rejected || declined.Outcome == DipOutcome.Invalid, "(a treaty is refused or not available)");
    SetRating(persia, 60); eng.EstablishEmbassy(persia.Id);
    Check(InboxService.Messages(s, InboxCategory.Other).Any(x => x.Topic == InboxTopic.Diplomacy && x.CountryId == persia.Id), "an embassy's answer is a diplomacy message (other)");
}

using (var eng = NewDipEngine(121))
{
    // Annexation: of the player's ally (reported, and warned about) or of a stranger (not).
    var s = eng.State; var p = s.PlayerNation;
    var spain = Ai(eng, "spain"); var japan = Ai(eng, "japan"); var easter = Ai(eng, "easter"); var nepal = Ai(eng, "nepal");
    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    Warfare.AnnexNation(s, nepal, easter);
    Check(s.Inbox.Count == 0, "the fall of a country that has nothing to do with the player is not in the inbox");
    Warfare.AnnexNation(s, japan, spain);
    Check(InboxService.Messages(s, InboxCategory.Military).Single().Topic == InboxTopic.Annexation && s.Inbox.Count == 1, "the fall of the player's ally is one military message, even though the alliance ends with it");

    // The fall of the player: one message, not three.
    s.Inbox.Clear();
    Warfare.AnnexNation(s, japan, p);
    eng.AdvanceOneDay();
    Check(s.Inbox.Count == 1 && s.Inbox[0].Topic == InboxTopic.EmpireFallen, "the empire falling is one message");
}

using (var eng = NewDipEngine(122))
{
    // Production, research, trade, shortages: the player's own, once each.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.ConstructionQueue.Add(new ConstructionProject { Building = BuildingType.Farm, DaysLeft = 1, TotalDays = 5 });
    persia.ConstructionQueue.Add(new ConstructionProject { Building = BuildingType.Farm, DaysLeft = 1, TotalDays = 5 });
    int farms = p.Farms;
    eng.AdvanceOneDay();
    var production = s.Inbox.Where(x => x.Topic == InboxTopic.Production).ToList();
    Check(p.Farms == farms + 1 && production.Count == 1 && production[0].Category == InboxCategory.Other, "a building the player finished is one production message (other)");
    Check(!s.Inbox.Any(x => x.Summary.Contains("Iran")), "a country's own production is not the player's news");

    var tech = TechnologyCatalog.All[0];
    p.CurrentResearchId = tech.Id; p.ResearchPoints = tech.Cost;
    eng.AdvanceOneDay();
    Check(s.Inbox.Count(x => x.Topic == InboxTopic.Research && x.Title.Contains(tech.Name)) == 1, "a technology researched is one message");

    Embassy(eng, persia);
    p.Gold = 1e9; SetStockOf(persia, "wheat", 100_000);
    void SetStockOf(Nation n, string id, double amount) => n.AdjustProductStock(id, amount - n.GetProductStock(id));
    Check(eng.BuyProduct(persia.Id, "wheat", 500) is null, "(wheat bought)");
    for (int i = 0; i < 9; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(s.Inbox.Count(x => x.Topic == InboxTopic.Trade && x.Summary.Contains("delivered")) == 1, "a delivered purchase is one trade message");
}

using (var eng = NewDipEngine(123))
{
    // A shortage that goes on for weeks is reported when it begins, not every day; deleting the message does not bring it back.
    eng.StartCampaign("france");
    var s = eng.State; var p = s.PlayerNation;
    for (int i = 0; i < 45; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(s.ActiveWarnings.Any(w => w.Contains("Shortage")), "(the player is short of food)");
    var shortages = s.Inbox.Where(x => x.Topic == InboxTopic.Shortage).ToList();
    Check(shortages.Count == 1 && shortages[0].Category == InboxCategory.Other && !shortages[0].Read,
        "a long shortage is one message that is kept up to date while it is unread, not a message a day");
    Check(shortages[0].Details.Contains("lives a day") && shortages[0].Date > s.CurrentDate.AddDays(-45), "the report says what the shortage costs, and is dated with its latest update");

    // Once the message has been opened, a change in the shortage is news again: a new message.
    InboxService.MarkRead(s, shortages[0].Id);
    s.InboxConditions["shortage"] = "something else";
    for (int i = 0; i < 3; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(s.Inbox.Count(x => x.Topic == InboxTopic.Shortage) == 2 && s.Inbox.Count(x => x.Topic == InboxTopic.Shortage && !x.Read) == 1,
        "after the player has read it, a change in the shortage is a new message");

    // Ending and restarting a condition reports it afresh.
    InboxService.ClearCondition(s, "shortage");
    var unread = s.Inbox.Single(x => x.Topic == InboxTopic.Shortage && !x.Read);
    InboxService.MarkRead(s, unread.Id);
    eng.AdvanceOneDay();
    Check(s.Inbox.Count(x => x.Topic == InboxTopic.Shortage) == 3, "a shortage that ended and came back is reported again");

    int deleted = eng.DeleteAllMessages();
    for (int i = 0; i < 10; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(deleted == 3 && !s.Inbox.Any(x => x.Topic == InboxTopic.Shortage), "after deleting, the same shortage is not announced again");
}

Console.WriteLine("== 59. Inbox: delete all, and save / load ==");
using (var eng = NewDipEngine(124))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    // Real events whose effects must survive the deletion of their messages.
    p.Gold = 1e9;
    eng.EstablishEmbassy(persia.Id);
    AiWorldService.DeclareWarOnPlayer(s, france);
    p.Units = UnitCatalog.SeedArmy(3000);
    int movements = s.Movements.Count, treaties = s.Treaties.Count; double gold = p.Gold; int soldiers = p.Soldiers;
    bool atWar = france.AtWarWithPlayer;
    Check(s.Inbox.Count >= 2 && InboxService.UnreadCount(s) == s.Inbox.Count, "(there are messages, all unread)");
    s.InboxConditions["shortage"] = "Salt";

    int count = s.Inbox.Count;
    Check(eng.DeleteAllMessages() == count && s.Inbox.Count == 0 && InboxService.UnreadCount(s) == 0, "delete all removes every message, in every tab");
    Check(s.Movements.Count == movements && s.Treaties.Count == treaties && p.Gold == gold && p.Soldiers == soldiers && france.AtWarWithPlayer == atWar && atWar,
        "...and undoes nothing: the Movement Report, the treaty, the war, the gold and the army are as they were");
    Check(s.InboxConditions.ContainsKey("shortage"), "(what was already reported is not reported again)");
    Check(eng.DeleteAllMessages() == 0, "deleting an empty inbox is harmless");
}

using (var eng = NewDipEngine(125))
{
    var s = eng.State; var france = Ai(eng, "france"); var persia = Ai(eng, "persia");
    var a = InboxService.Notify(s, InboxTopic.WarDeclared, "war!", france.Id);
    var b = InboxService.Notify(s, InboxTopic.SpyMission, "stole gold", persia.Id, details: "line one\nline two");
    var c = InboxService.Notify(s, InboxTopic.Trade, "delivered");
    eng.OpenMessage(b.Id);
    s.InboxConditions["shortage"] = "Salt, Bread";
    var before = s.Inbox.Select(x => (x.Id, x.Date, x.Topic, x.Title, x.Summary, x.Details, x.CountryId, x.Read)).ToList();
    await eng.SaveAsync();
    s.Inbox.Clear(); s.InboxConditions.Clear();
    await eng.LoadAsync();
    var l = eng.State;
    Check(l.Inbox.Select(x => (x.Id, x.Date, x.Topic, x.Title, x.Summary, x.Details, x.CountryId, x.Read)).SequenceEqual(before), "messages survive save / load unchanged, with their dates, countries and details");
    Check(l.Inbox.Single(x => x.Id == b.Id).Read && !l.Inbox.Single(x => x.Id == a.Id).Read && InboxService.UnreadCount(l) == 2, "read and unread state survive save / load");
    Check(l.Inbox.Single(x => x.Id == a.Id).Category == InboxCategory.Military && l.Inbox.Single(x => x.Id == b.Id).Category == InboxCategory.Intelligence && l.Inbox.Single(x => x.Id == c.Id).Category == InboxCategory.Other,
        "each message is still in its own category after loading");
    Check(l.InboxConditions["shortage"] == "Salt, Bread", "what was already reported survives too, so a loaded game does not repeat it");

    var node = JsonNode.Parse(JsonSerializer.Serialize(eng.State))!.AsObject();
    foreach (var key in new[] { "Inbox", "InboxConditions" }) node.Remove(key);
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.Inbox.Count == 0 && old.InboxConditions.Count == 0, "an old save without an inbox loads with an empty one");
}

Console.WriteLine("== 60. Production: how many of a mill can be built (checked in the build popup) ==");
using (var eng = NewDipEngine(126))
{
    var s = eng.State; var p = s.PlayerNation;
    // A mill that costs gold, wood and stone.
    var spec = ProductionCatalog.All.First(x => x.Category != ProductionCategory.Military && x.GoldCost > 0 && x.WoodCost > 0 && x.StoneCost > 0);
    void Stock(double gold, double wood, double stone, double iron) { p.Gold = gold; p.Wood = wood; p.Stone = stone; p.Iron = iron; }

    // Plenty of everything: only the build queue limits.
    Stock(1e12, 1e12, 1e12, 1e12);
    var rich = eng.MaxBuildProduction(spec.Id);
    Check(rich.Max == Balance.MaxBuildQueue - p.ConstructionQueue.Count && rich.LimitedBy == "the build queue", $"with everything in plenty the build queue is the limit ({rich.Max:N0})");

    // Each resource in turn is the one that runs out first.
    foreach (var (name, set) in new (string, Action<int>)[]
    {
        ("gold",  n => Stock(spec.GoldCost * n + spec.GoldCost / 2, 1e12, 1e12, 1e12)),
        ("wood",  n => Stock(1e12, spec.WoodCost * n + spec.WoodCost / 2, 1e12, 1e12)),
        ("stone", n => Stock(1e12, 1e12, spec.StoneCost * n + spec.StoneCost / 2, 1e12)),
    })
    {
        set(7);
        var limit = eng.MaxBuildProduction(spec.Id);
        Check(limit.Max == 7 && limit.LimitedBy == name, $"enough {name} for seven mills (and a half): the limit is 7, limited by {name}");
    }

    // The engine accepts exactly that many, and refuses one more.
    Stock(spec.GoldCost * 5.5, 1e12, 1e12, 1e12);
    int max = eng.MaxBuildProduction(spec.Id).Max;
    int queued = p.ConstructionQueue.Count;
    Check(max == 5 && eng.BuildProduction(spec.Id, max) is null && p.ConstructionQueue.Count == queued + 5, "the engine accepts exactly the most the popup says");
    Stock(spec.GoldCost * 5.5, 1e12, 1e12, 1e12);
    Check(eng.BuildProduction(spec.Id, max + 1) == "Not enough resources.", "...and refuses one more");

    // Not even one: the answer is zero and says what is missing, so the popup can explain (the button itself is never disabled).
    Stock(spec.GoldCost / 2, 1e12, 1e12, 1e12);
    var none = eng.MaxBuildProduction(spec.Id);
    Check(none.Max == 0 && none.LimitedBy == "gold" && eng.BuildProduction(spec.Id, 1) == "Not enough resources.", "short of gold for even one mill: zero, limited by gold");
    Stock(1e12, 0, 1e12, 1e12);
    Check(eng.MaxBuildProduction(spec.Id) is { Max: 0, LimitedBy: "wood" }, "no wood at all: zero, limited by wood");
}

using (var eng = NewDipEngine(127))
{
    // The build queue itself can be the limit.
    var s = eng.State; var p = s.PlayerNation;
    var spec = ProductionCatalog.All.First(x => x.Category != ProductionCategory.Military);
    p.Gold = 1e12; p.Wood = 1e12; p.Stone = 1e12; p.Iron = 1e12;
    while (p.ConstructionQueue.Count < Balance.MaxBuildQueue - 4)
        p.ConstructionQueue.Add(new ConstructionProject { Building = BuildingType.Farm, ProductionBuildingId = spec.Id, DaysLeft = 9, TotalDays = 9 });
    var limit = eng.MaxBuildProduction(spec.Id);
    Check(limit.Max == 4 && limit.LimitedBy == "the build queue", "with the build queue nearly full, room for four is the limit");
    Check(eng.BuildProduction(spec.Id, 5)?.Contains("queue is full") == true && eng.BuildProduction(spec.Id, 4) is null, "...the engine agrees: five are refused, four accepted");
    Check(eng.MaxBuildProduction(spec.Id).Max == 0, "a full queue leaves room for none");
}

Console.WriteLine("== 61. Trade needs an embassy first ==");
using (var eng = NewDipEngine(128))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    void SetStock(Nation n, string id, double amount) => n.AdjustProductStock(id, amount - n.GetProductStock(id));
    p.Gold = 1e9; SetStock(persia, "wheat", 100_000); SetStock(france, "wheat", 100_000); SetStock(p, "wheat", 5000);
    persia.Gold = 1e9; france.Gold = 1e9;
    double price = MarketPricing.PricePer1000("wheat", persia.Id);

    // No embassy: no buying, no selling.
    Check(!eng.HasTradeEmbassy(persia.Id) && eng.TradeBlock(persia.Id) is { } why && why.Contains("Iran") && why.Contains("embassy") && why.Contains("Diplomacy"), "without an embassy trade is blocked, and the reason says why and where to fix it");
    double gold0 = p.Gold; double stock0 = persia.GetProductStock("wheat");
    var buy = eng.BuyProduct(persia.Id, "wheat", 100);
    var sell = eng.SellProduct(persia.Id, "wheat", 100, price);
    Check(buy is not null && buy.Contains("embassy") && sell is not null && sell.Contains("embassy"), "buying from and selling to a country without an embassy are both refused");
    Check(p.Gold == gold0 && persia.GetProductStock("wheat") == stock0 && p.GetProductStock("wheat") == 5000 && !s.TradeContracts.Any(), "...and nothing moves: no gold, no goods, no contract");
    Check(eng.MaxBuyQuantity(persia.Id, "wheat") == 0 && eng.MaxSellQuantity(persia.Id, "wheat", price) == 0, "MAX is zero for a country you cannot trade with");
    Check(eng.TradeBlock("nobody") is not null && eng.TradeBlock(p.Id) is not null, "an unknown country, or yourself, is not a trading partner");

    // Set up the embassy the real way, in Diplomacy.
    SetRating(persia, 70);
    Check(eng.EstablishEmbassy(persia.Id).Ok && eng.HasTradeEmbassy(persia.Id) && eng.TradeBlock(persia.Id) is null, "(an embassy is set up in Diplomacy)");
    Check(eng.BuyProduct(persia.Id, "wheat", 100) is null && eng.SellProduct(persia.Id, "wheat", 100, price) is null, "with the embassy both buying and selling work");
    Check(eng.MaxBuyQuantity(persia.Id, "wheat") > 0 && eng.MaxSellQuantity(persia.Id, "wheat", price) > 0, "...and so does MAX");

    // The embassy is in one country: it does not open the others.
    Check(!eng.HasTradeEmbassy(france.Id) && eng.BuyProduct(france.Id, "wheat", 100)?.Contains("France") == true, "an embassy in Iran does not let you trade with France");

    // Walking away from the embassy closes the door again, but does not cancel goods already on the road.
    int contracts = s.TradeContracts.Count(c => c.Status == TradeStatus.InTransit);
    Check(eng.CancelTreaty(TreatyType.Embassy, persia.Id).Ok && eng.BuyProduct(persia.Id, "wheat", 100)?.Contains("embassy") == true, "cancelling the embassy stops trade with that country");
    for (int i = 0; i < 9; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(contracts == 2 && s.TradeContracts.Count(c => c.Status == TradeStatus.Delivered) == 2, "the purchase and the sale already under way are still delivered");
}

Console.WriteLine("== 62. The diplomatic web of 1600: the start of every campaign ==");
{
    var ids = GameState.NewGame("ottoman").AllNations().Select(n => n.Id).ToList();
    var ties = HistoricalDiplomacy.Ties;
    Check(ids.Count == 41, "(41 countries to start as)");
    Check(ties.All(t => ids.Contains(t.A) && ids.Contains(t.B) && t.A != t.B), "every tie in the table is between two real, different countries");
    Check(ties.Select(t => string.CompareOrdinal(t.A, t.B) <= 0 ? $"{t.A}|{t.B}" : $"{t.B}|{t.A}").Distinct().Count() == ties.Count, "no pair is listed twice");
    Check(ties.All(t => t.Rating is >= 0 and <= 100 && t.Why.Length > 8), "every tie has a rating from 0 to 100 and a historical reason");
    Check(ties.Count >= 80 && ties.Count(t => t.Embassy) >= 25 && ties.Count(t => t.Rating >= 60) >= 20 && ties.Count(t => t.Rating <= 30) >= 20,
        $"the table has real friendships ({ties.Count(t => t.Rating >= 60)}), quarrels ({ties.Count(t => t.Rating <= 30)}) and embassies ({ties.Count(t => t.Embassy)})");
    Check(ties.Where(t => t.Rating < 15).Count() == 0, "(no tie is so bitter that it would push the other country to declare war outright)");
}

{
    // History, spot-checked from several chairs.
    int R(GameState s, string id) => DiplomacyService.ToDisplayRating(s.OtherNations.First(n => n.Id == id).RelationToPlayer);
    bool E(GameState s, string id) => TreatyService.HasEmbassy(s, s.PlayerNation.Id, id);

    var ottoman = GameState.NewGame("ottoman");
    Check(new[] { "france", "venice", "england", "crimea" }.All(id => E(ottoman, id)) && !E(ottoman, "austria") && !E(ottoman, "spain"),
        "the Ottomans start with embassies in France, Venice, England and Crimea — and none in Habsburg lands");
    Check(R(ottoman, "france") >= 70 && R(ottoman, "england") >= 60 && R(ottoman, "austria") <= 25 && R(ottoman, "spain") <= 30 && R(ottoman, "persia") <= 30,
        "...friendly with France and England, bad blood with the Habsburgs, Spain and Persia");

    var england = GameState.NewGame("england");
    Check(E(england, "dutch") && E(england, "morocco") && E(england, "russia") && R(england, "dutch") >= 65 && R(england, "spain") <= 20 && R(england, "papal") <= 25,
        "England: embassies with the Dutch, Morocco and Russia; at odds with Spain and the Pope");

    var korea = GameState.NewGame("korea");
    Check(E(korea, "ming") && R(korea, "ming") >= 80 && R(korea, "japan") <= 20, "Korea: an embassy in Ming China, whose army saved it, and hatred of Japan");

    var spain = GameState.NewGame("spain");
    Check(E(spain, "genoa") && E(spain, "papal") && E(spain, "austria") && R(spain, "dutch") <= 20 && R(spain, "england") <= 20, "Iberian Union: Genoa, the Pope and Vienna at its side; the Dutch and the English against it");

    var poland = GameState.NewGame("poland");
    Check(E(poland, "papal") && E(poland, "austria") && R(poland, "sweden") <= 20 && R(poland, "russia") <= 40, "Poland: the Pope and Vienna as friends, Sweden and Russia as rivals");

    Check(R(GameState.NewGame("austria"), "ottoman") == R(ottoman, "austria") && R(GameState.NewGame("france"), "ottoman") == R(ottoman, "france"),
        "a quarrel or friendship is the same from either side");

    // Every country starts with a mix: some friends, some rivals — unless history says it kept to itself.
    var mixed = new[] { "ottoman", "spain", "france", "england", "dutch", "austria", "poland", "russia", "persia", "ming", "japan", "korea", "venice", "papal" };
    Check(mixed.All(id => { var s = GameState.NewGame(id); return s.OtherNations.Any(n => DiplomacyService.ToDisplayRating(n.RelationToPlayer) >= 60) && s.OtherNations.Any(n => DiplomacyService.ToDisplayRating(n.RelationToPlayer) <= 35); }),
        "the great powers each start with friends (60+) and rivals (35 or less)");
    Check(mixed.All(id => GameState.NewGame(id).Treaties.Count(t => t.Type == TreatyType.Embassy) >= HistoricalDiplomacy.MinEmbassies), "...and with embassies");
}

{
    // Countries history says little about start neutral (a touch warmer between one faith); islands far from everyone start alone.
    var swedenStart = GameState.NewGame("sweden");
    var embassies = swedenStart.Treaties.Where(t => t.Type == TreatyType.Embassy).Select(t => swedenStart.OtherNations.First(n => n.Id == t.NationBId)).ToList();
    Check(embassies.Count == HistoricalDiplomacy.MinEmbassies && embassies.All(n => AiWorldService.Distance(swedenStart.PlayerNation, n) <= Balance.AiWarRange && DiplomacyService.ToDisplayRating(n.RelationToPlayer) >= Balance.EmbassyMinRating),
        "a lesser power with no embassy in the table gets two in the warmest acceptable neighbours within reach, never a hostile one");

    var spainRussia = GameState.NewGame("spain").OtherNations.First(n => n.Id == "russia");
    var ottomanJapan = GameState.NewGame("ottoman").OtherNations.First(n => n.Id == "japan");
    Check(DiplomacyService.ToDisplayRating(spainRussia.RelationToPlayer) == 56 && DiplomacyService.ToDisplayRating(ottomanJapan.RelationToPlayer) == 48,
        "a pair history is silent about: warmer (56) between countries of one faith, a little cool (48) between different ones");

    var easter = GameState.NewGame("easter");
    var easterEmbassies = easter.Treaties.Where(t => t.Type == TreatyType.Embassy).ToList();
    Check(easterEmbassies.All(t => HistoricalDiplomacy.TieBetween("easter", t.NationBId) is null) && easterEmbassies.Count <= HistoricalDiplomacy.MinEmbassies && easter.OtherNations.All(n => DiplomacyService.ToDisplayRating(n.RelationToPlayer) is >= 44 and <= 58),
        "an island that kept to itself starts with no historical ties, no strong feelings, and at most a neighbour's embassy");
    Check(GameState.NewGame("ottoman", historicalDiplomacy: false).Treaties.Count == 0 && GameState.NewGame("ottoman", historicalDiplomacy: false).OtherNations.All(n => n.RelationToPlayer == 0),
        "(the historical start can be switched off)");
}

using (var eng = new GameEngine(new SimulationService(seed: 77), new SaveService(saveFolder)))
{
    // In play: the embassies let the player trade at once; the web of 1600 does not melt away.
    eng.StartCampaign("ottoman");
    var s = eng.State; var p = s.PlayerNation;
    Check(eng.TradeBlock("france") is null && eng.TradeBlock("venice") is null && eng.TradeBlock("spain") is not null, "from the first day the Ottomans can trade with France and Venice, but not with Spain");

    var austria = s.OtherNations.First(n => n.Id == "austria"); var france = s.OtherNations.First(n => n.Id == "france"); var morocco = s.OtherNations.First(n => n.Id == "morocco");
    double a0 = austria.RelationToPlayer, f0 = france.RelationToPlayer, m0 = morocco.RelationToPlayer;
    Check(DiplomacyService.ToDisplayRating(a0) <= 25 && DiplomacyService.ToDisplayRating(f0) >= 70, "(Vienna is cold, Paris is warm)");
    for (int i = 0; i < 400; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    Check(austria.RelationToPlayer <= a0 + 1 && austria.RelationToPlayer < -40, "a year and more later the Habsburgs are still cold: old quarrels are not forgotten");
    Check(france.RelationToPlayer >= f0 - 1 && Math.Abs(morocco.RelationToPlayer - m0) <= 1, "...and old friendships and neutral courts stay where they were");
    Check(!austria.AtWarWithPlayer || s.Movements.Any(m => m.ToId == p.Id && m.FromId == austria.Id && m.Text.Contains("declared war")), "(and a war with Vienna, if there is one, has a cause on record)");
}

{
    // The grudge that makes the AI attack the player is what has soured since the start, not old bad blood.
    var s = GameState.NewGame("ottoman");
    var austria = s.OtherNations.First(n => n.Id == "austria"); var p = s.PlayerNation;
    double Grudge() => AiWorldService.Motives(s, austria, p).Grudge;
    Check(Grudge() == 0, "a country that has always disliked the player has no grudge on the first day");
    austria.RelationToPlayer = -100;
    double fallen = (austria.BaselineRelation - -100) / 100.0;   // how far it has soured below its historical level
    Check(Math.Abs(Grudge() - Math.Min(1.0, fallen)) < 1e-9 && Grudge() > 0.3, "an insult that sours it further becomes a grudge (only the part below its old level)");
    austria.BaselineRelation = 0; austria.RelationToPlayer = -40;
    Check(Math.Abs(Grudge() - 0.4) < 1e-9, "with no history (baseline 0, as in an old save) a grudge is simply the regard below zero, as before");
    austria.BaselineRelation = 40; austria.RelationToPlayer = -10;
    Check(Math.Abs(Grudge() - 0.1) < 1e-9, "a former friend turned cold: the grudge is the regard below zero (0.1), not the whole fall");

    // Old saves have no baseline: they load as indifferent, as before.
    var node = JsonNode.Parse(JsonSerializer.Serialize(s))!.AsObject();
    foreach (var n in node["OtherNations"]!.AsArray()) n!.AsObject().Remove("BaselineRelation");
    var old = JsonSerializer.Deserialize<GameState>(node.ToJsonString());
    Check(old is not null && old.OtherNations.All(n => n.BaselineRelation == 0), "an old save without baselines loads with every country indifferent, as before");
}

Console.WriteLine("== 63. War, stage 1: march time comes from the distance between the capitals ==");
{
    var s0 = GameState.NewGame("ottoman");
    var ott = s0.PlayerNation;
    var others = s0.OtherNations.Where(n => !n.IsEliminated).ToList();
    double Dist(Nation a, Nation b) => Math.Sqrt(Math.Pow(a.MapX - b.MapX, 2) + Math.Pow(a.MapY - b.MapY, 2));

    Check(MapGeography.MissingCapitals(s0).Count == 0, "every one of the 41 countries has capital coordinates: nothing needs geographical data");
    Check(others.All(n => MapGeography.Route(ott, n) is { UsedFallback: false, Distance: not null } r && r.Days == Math.Max(1, (int)Math.Ceiling(Dist(ott, n) / Balance.MarchSpeedPerDay))),
        "march time is the capital-to-capital distance over one movement speed, rounded up, for every country");
    var byDistance = others.OrderBy(n => Dist(ott, n)).Select(n => MapGeography.Route(ott, n).Days).ToList();
    Check(byDistance.Zip(byDistance.Skip(1), (a, b) => b >= a).All(x => x), "the nearer the country, the sooner the army arrives (never the other way round)");
    Check(byDistance.Last() > 30 && byDistance.First() < byDistance.Last() / 4, $"nothing is capped: the nearest takes {byDistance.First()} days, the farthest {byDistance.Last()}");
    var neighbours = others.OrderBy(n => Dist(ott, n)).Take(8).Select(n => MapGeography.Route(ott, n).Days).Distinct().Count();
    Check(neighbours >= 4, $"neighbouring countries do not all take the same time ({neighbours} different times among the nearest eight)");
    Check(others.All(n => MapGeography.Route(n, n).Days >= 1) && MapGeography.Route(ott, ott).Days == 1, "nobody arrives instantly: even the shortest march takes a day");
    Check(MapGeography.Route(ott, others.First(n => n.Id == "crimea")).Days < MapGeography.Route(ott, others.First(n => n.Id == "japan")).Days, "Crimea, next door, is reached sooner than distant Japan");
}

using (var eng = NewDipEngine(129))
{
    var s = eng.State; var p = s.PlayerNation; var crimea = Ai(eng, "crimea"); var japan = Ai(eng, "japan");
    crimea.Units = UnitCatalog.SeedArmy(2000); japan.Units = UnitCatalog.SeedArmy(2000);
    eng.DeclareWar(crimea.Id); eng.DeclareWar(japan.Id);
    var near = eng.LaunchInvasion(crimea.Id, 2500); var far = eng.LaunchInvasion(japan.Id, 2500);
    var mNear = s.MarchingArmies.Single(m => m.TargetNationId == crimea.Id); var mFar = s.MarchingArmies.Single(m => m.TargetNationId == japan.Id);
    Check(near.ok && far.ok && mNear.TotalDays < mFar.TotalDays, $"the nearby target receives the army sooner ({mNear.TotalDays} days against {mFar.TotalDays})");
    Check(mNear.StartDate == s.CurrentDate && mNear.ArrivalDate == s.CurrentDate.AddDays(mNear.TotalDays) && mFar.ArrivalDate == s.CurrentDate.AddDays(mFar.TotalDays),
        "each march stores the day it set out and the day it will arrive");
    Check(near.message.Contains(mNear.ArrivalDate.ToString("dd-MM-yyyy")), "the arrival date is told to the player");
    Check(mNear.TotalDays == MapGeography.Route(p, crimea).Days, "the march takes exactly what the geography says");

    // Progress is kept by the date, through a save and a load.
    for (int i = 0; i < mNear.TotalDays - 2; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    var arrival = mNear.ArrivalDate; var farArrival = mFar.ArrivalDate; int armyAtSave = s.MarchingArmies.Sum(m => m.Strength);
    await eng.SaveAsync();
    s.MarchingArmies.Clear();
    await eng.LoadAsync();
    var l = eng.State;
    var loaded = l.MarchingArmies.ToList();
    Check(loaded.Count == 2 && loaded.Sum(m => m.Strength) == armyAtSave && loaded.Any(m => m.ArrivalDate == arrival) && loaded.Any(m => m.ArrivalDate == farArrival),
        "marching armies, with their dates, survive save / load");
    Check(l.CurrentDate.AddDays(2) == arrival, "(two days to go when saved)");
    eng.AdvanceOneDay();
    Check(l.MarchingArmies.Any(m => m.ArrivalDate == arrival), "one day on, the loaded army is still on the road");
    eng.AdvanceOneDay();
    Check(!l.MarchingArmies.Any(m => m.ArrivalDate == arrival) && l.MarchingArmies.Any(m => m.ArrivalDate == farArrival), "and it arrives on the stored date, while the far army marches on");
}

{
    // A country without capital coordinates: an explicit fallback, and it is reported.
    var s = GameState.NewGame("ottoman");
    var lost = s.OtherNations.First(n => n.Id == "france");
    double mx = lost.MapX, my = lost.MapY;
    lost.MapX = 0; lost.MapY = 0;
    var estimated = MapGeography.Route(s.PlayerNation, lost);
    Check(MapGeography.MissingCapitals(s).Select(n => n.Id).SequenceEqual(new[] { "france" }), "a country without coordinates is listed as needing geographical data");
    Check(estimated.UsedFallback && estimated.Distance is not null && estimated.Days >= 1 && MapGeography.FallbackNote(s.PlayerNation, lost, estimated)!.Contains("France"),
        "its capital is estimated from its territory, and the fallback is flagged");
    var territory = lost.Territory; lost.Territory = new List<List<MapPoint>>();
    var blind = MapGeography.Route(s.PlayerNation, lost);
    Check(blind.UsedFallback && blind.Distance is null && blind.Days == Balance.MarchFallbackDays && MapGeography.FallbackNote(s.PlayerNation, lost, blind)!.Contains($"{Balance.MarchFallbackDays} days"),
        $"with no territory either, the march takes the fixed fallback of {Balance.MarchFallbackDays} days, and says so");
    lost.Territory = territory; lost.MapX = mx; lost.MapY = my;
    Check(MapGeography.MissingCapitals(s).Count == 0 && !MapGeography.Route(s.PlayerNation, lost).UsedFallback, "(with the coordinates back, nothing is missing)");
}

Console.WriteLine("== 64. War, stage 2: an enemy counter-invasion is a real march ==");
using (var eng = NewDipEngine(130))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    persia.Units = UnitCatalog.SeedArmy(30_000);   // far stronger than the player's army: it will want to attack
    p.Units = UnitCatalog.SeedArmy(8000);
    eng.DeclareWar(persia.Id);
    int persiaBefore = persia.Soldiers; int playerBefore = p.Soldiers;
    MarchingArmy? march = null; int day = 0;
    for (; day < 3000 && march is null; day++)
    {
        p.Gold = 1e12; eng.AdvanceOneDay();
        march = s.MarchingArmies.FirstOrDefault(m => m.AttackerNationId == persia.Id && m.TargetNationId == p.Id);
    }
    Check(march is not null, "(a far stronger enemy sends an army against the player)");
    int atLaunch = persia.Soldiers;
    Check(atLaunch < persiaBefore - march!.Strength + 200, $"the enemy's marching soldiers left its home army at once ({march.Strength:N0} marching, home army {persiaBefore:N0} -> {atLaunch:N0})");
    Check(march.Strength >= Balance.MinInvasionForce && march.ArrivalDate == march.StartDate.AddDays(march.TotalDays) && march.TotalDays == MapGeography.Route(persia, p).Days,
        "its march takes the time the same capital-to-capital rule gives, with stored dates");
    Check(!p.IsEliminated && p.Soldiers >= playerBefore - 40 * (day + 1) && !s.Movements.Any(m => m.Text.Contains("reached") && m.FromId == persia.Id), "on the day it sets out nothing is resolved: the home army is intact and no battle has been fought");

    var launched = s.Movements.First(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && m.FromId == persia.Id && m.ToId == p.Id);
    var inboxLaunch = s.Inbox.FirstOrDefault(m => m.Topic == InboxTopic.Invasion && m.CountryId == persia.Id);
    Check(launched.Text.Contains("marches") && inboxLaunch is not null && inboxLaunch.Category == InboxCategory.Military && inboxLaunch.Summary.Contains(march.ArrivalDate.ToString("dd-MM-yyyy")),
        "the player gets a military report when the march sets out, with the arrival date");

    // It is not sent twice, and nothing resolves before the arrival date.
    int sentBefore = s.Movements.Count(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && m.FromId == persia.Id && m.ToId == p.Id);
    int guard = 0;
    while (s.CurrentDate < march.ArrivalDate.AddDays(-1) && guard++ < 400)
    {
        p.Gold = 1e12; eng.AdvanceOneDay();
        if (!s.MarchingArmies.Contains(march)) break;
    }
    Check(s.MarchingArmies.Contains(march) && s.MarchingArmies.Count(m => m.AttackerNationId == persia.Id && m.TargetNationId == p.Id) == 1
          && s.Movements.Count(m => m.Kind == MovementKind.March && m.Status == MovementStatus.UnderWay && m.FromId == persia.Id && m.ToId == p.Id) == sentBefore && !p.IsEliminated,
        "while it marches the same troops are not deployed twice and the battle has not been fought");
    Check(persia.Soldiers + march.Strength <= persiaBefore, "(the enemy's home army plus its marching army never exceeds what it had)");
    p.Gold = 1e12; eng.AdvanceOneDay();   // the arrival day
    Check(!s.MarchingArmies.Contains(march), "the army reaches the player on its arrival date and the battle is fought then");
    var report = s.Inbox.FirstOrDefault(m => m.Topic == InboxTopic.Battle && m.CountryId == persia.Id);
    Check(report is not null && report.Category == InboxCategory.Military && (report.Summary.Contains("reached") || report.Summary.Contains("repelled")), "...and the player gets a second military report when it arrives");
}

// The battle is fought against the troops actually at home when the enemy arrives (a fresh game each round).
foreach (bool reinforce in new[] { false, true })
{
    using var eng = NewDipEngine(131);
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    persia.Units = UnitCatalog.SeedArmy(10_000); p.Units = UnitCatalog.SeedArmy(600); eng.DeclareWar(persia.Id);
    var march = TreatyService.LaunchMarch(s, persia, p, 3000)!;
    Check(persia.Soldiers == 7000, "(3,000 left the enemy's home army)");
    if (reinforce) p.Units = UnitCatalog.SeedArmy(9000);   // the player's reinforcements are home before the enemy arrives (enough to hold, not enough for the enemy to sue for peace)
    for (int i = 0; i < march.TotalDays; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); }
    if (reinforce)
        Check(!p.IsEliminated && s.Inbox.Any(m => m.Topic == InboxTopic.Battle && m.Summary.Contains("repelled")), "reinforced before it arrived, the home army beats the enemy: the defence counts the troops at home on arrival");
    else
        Check(p.IsEliminated && s.Inbox.Any(m => m.Topic == InboxTopic.Battle && m.Summary.Contains("broke the home army")), "left with a small home army, the player loses it when the enemy arrives");
}

using (var eng = NewDipEngine(132))
{
    // Enemy marches obey the same rules as the player's: a minimum force, and none from an army too small.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    persia.Units = UnitCatalog.SeedArmy(900); p.Units = UnitCatalog.SeedArmy(100);   // stronger than 1.2x, but half of 900 is under the minimum
    eng.DeclareWar(persia.Id);
    for (int i = 0; i < 1500; i++) { p.Gold = 1e12; eng.AdvanceOneDay(); persia.Units = UnitCatalog.SeedArmy(900); }
    Check(!s.MarchingArmies.Any(m => m.AttackerNationId == persia.Id), "an enemy whose army is too small for the minimum force does not march at all");
}

Console.WriteLine("== 65. War, stage 3: a sovereignty guarantee does not block annexation; annexation puts each thing in exactly one place ==");
using (var eng = NewDipEngine(140))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    // Give the doomed country a recognisable estate, and the player an army, a colony shipment and a march on it.
    persia.Gold = 5000; persia.Wood = 300; persia.Stone = 200; persia.Iron = 150; persia.Copper = 120; persia.Lead = 80; persia.Goods = 70;
    persia.Helmets = 40; persia.Pikes = 30; persia.ShipParts = 12;
    persia.GoodsInventory["Salt"] = 55; persia.ProductionBuildings["bakery"] = 3;
    persia.Farms = 11; persia.Mines = 4; persia.Sawmills = 5; persia.Workshops = 6;
    persia.Reserves = 100; persia.BaseSoldiers = 2000; persia.BasePopulation = 4_000_000;
    persia.Commanders.Add(new Commander());
    persia.Units = UnitCatalog.SeedArmy(2500);
    persia.Warships = 7;
    int polygons = persia.Territory.Count;
    s.Colonies.Add(new Colony { Name = "Persian Outpost", OwnerId = persia.Id, FoundedById = persia.Id });
    s.SpyNetworks.Add(new SpyNetwork { TargetNationId = persia.Id, TargetNationName = persia.Name });
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "annexation_ban", TargetId = persia.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(90) });

    p.Units = UnitCatalog.SeedArmy(6000);
    persia.AtWarWithPlayer = true;
    var inbound = TreatyService.LaunchMarch(s, p, persia, 2000)!;
    Check(p.Soldiers == 4000, "(2,000 soldiers left the home army on the march against the doomed country)");

    // A purchase from it is still on the road (gold paid, goods reserved): once it is part of the player's realm it comes home.
    var boughtWheat = new TradeContract { BuyerId = p.Id, SellerId = persia.Id, ProductId = "dagger", Quantity = 100, TotalValue = 250, IsPlayerBuyer = true,
        CreatedDate = s.CurrentDate, DeliveryDate = s.CurrentDate.AddDays(10), Status = TradeStatus.InTransit };
    s.TradeContracts.Add(boughtWheat);

    double gold0 = p.Gold, wood0 = p.Wood, stone0 = p.Stone, iron0 = p.Iron, copper0 = p.Copper, lead0 = p.Lead, goods0 = p.Goods, helmets0 = p.Helmets, pikes0 = p.Pikes, parts0 = p.ShipParts;
    double salt0 = p.GetGood("Salt"), daggers0 = p.GetProductStock("dagger") + persia.GetProductStock("dagger");
    int bakeries0 = p.GetProductionBuilding("bakery"), farms0 = p.Farms, mines0 = p.Mines, saws0 = p.Sawmills, shops0 = p.Workshops, polys0 = p.Territory.Count;
    long pop0 = p.Population, persiaPop = persia.Population, popLost = (long)(persiaPop * Balance.BattlePopulationLoss);
    long base0 = p.BasePopulation > 0 ? p.BasePopulation : p.HistoricalPopulation;
    int sold0 = p.Soldiers, annexed0 = s.NationsAnnexedByPlayer;

    Warfare.AnnexNation(s, p, persia);

    Check(persia.IsEliminated && !persia.AtWarWithPlayer && persia.AnnexedById == p.Id, "the country is no longer a country: it is annexed, under the winner, and the war flag is cleared");
    // What the winner takes at once.
    Check(p.Gold == gold0 + 5000 + 250 && persia.Gold == 0, "its treasury (and the gold paid for goods still on the road) moves to the winner, once");
    Check(p.Helmets == helmets0 + 40 && p.Pikes == pikes0 + 30 && p.ShipParts == parts0 + 12 && persia.Helmets == 0 && persia.Pikes == 0 && persia.ShipParts == 0, "its military equipment moves to the winner");
    Check(p.Territory.Count == polys0 + polygons && persia.Territory.Count == 0, "its lands are added to the winner's realm on the map and removed from the territory's own list");
    Check(s.Colonies.Single(c => c.Name == "Persian Outpost").OwnerId == p.Id, "its colony now belongs to the winner");
    Check(p.BasePopulation == base0 + 4_000_000 && persia.BasePopulation == 0 && persia.BaseSoldiers == 0, "the army-target figures move across once");
    Check(p.GetProductStock("dagger") == daggers0 + 100, "the purchase still on the road comes home to the player, not lost");
    // What stays where it is: the territory's own local economy — counted once, not added to the winner's.
    Check(persia.Wood == 300 && persia.Stone == 200 && persia.Iron == 150 && persia.Copper == 120 && persia.Lead == 80 && persia.Goods == 70
          && p.Wood == wood0 && p.Stone == stone0 && p.Iron == iron0 && p.Copper == copper0 && p.Lead == lead0 && p.Goods == goods0, "its minerals and goods stay in the territory's stockpile (the winner's are untouched)");
    Check(persia.GetGood("Salt") == 55 && p.GetGood("Salt") == salt0, "its stocked food stays in the territory");
    Check(persia.GetProductionBuilding("bakery") == 3 && p.GetProductionBuilding("bakery") == bakeries0, "its mills stay in the territory");
    Check(persia.Farms == 11 && persia.Mines == 4 && persia.Sawmills == 5 && persia.Workshops == 6
          && p.Farms == farms0 && p.Mines == mines0 && p.Sawmills == saws0 && p.Workshops == shops0, "its farms, mines, sawmills and workshops stay in the territory");
    Check(persia.Population == persiaPop - popLost && p.Population == pop0, "its people (less the war losses) live on in the territory; the winner's own population is unchanged");
    Check(s.TotalPopulation(p) == pop0 + persia.Population, "the realm counts them once: its total is the winner's people plus the territory's");

    Check(p.Soldiers == sold0 + 2000, "the army still marching on it came home exactly once (no duplicated or vanished soldiers)");
    Check(persia.Soldiers == 0 && persia.Warships == 0 && persia.Reserves == 0 && persia.Commanders.Count == 0, "its army, fleet, reserves and officers are gone");
    Check(!s.MarchingArmies.Any(m => m.TargetNationId == persia.Id || m.AttackerNationId == persia.Id), "no march remains to or from it");
    Check(!s.SpyNetworks.Any(n => n.TargetNationId == persia.Id) && !s.ActiveAssemblyPolicies.Any(a => a.TargetId == persia.Id), "spy networks and Assembly policies about it are removed");
    Check(boughtWheat.Status == TradeStatus.Cancelled && !s.TradeContracts.Any(c => c.Status == TradeStatus.InTransit), "the shipment is settled on the spot, so it cannot be delivered a second time");
    Check(s.NationsAnnexedByPlayer == annexed0 + 1, "counted as one annexation");

    // Annexing again changes nothing: there is nothing left to take, and no second war loss.
    double goldNow = p.Gold; long popNow = p.Population, persiaNow = persia.Population; int soldNow = p.Soldiers, farmsNow = p.Farms;
    Warfare.AnnexNation(s, p, persia);
    Check(p.Gold == goldNow && p.Population == popNow && persia.Population == persiaNow && p.Soldiers == soldNow && p.Farms == farmsNow && s.NationsAnnexedByPlayer == annexed0 + 1,
        "a repeated annexation changes nothing (assets and war losses are not applied twice)");
}

using (var eng = NewDipEngine(141))
{
    // An AI conquest: the shipment the player was buying from the fallen country now comes from the conqueror.
    var s = eng.State; var p = s.PlayerNation; var spain = Ai(eng, "spain"); var france = Ai(eng, "france");
    france.Gold = 4000; spain.Gold = 1000;
    var bought = new TradeContract { BuyerId = p.Id, SellerId = france.Id, ProductId = "dagger", Quantity = 50, TotalValue = 120, IsPlayerBuyer = true,
        CreatedDate = s.CurrentDate, DeliveryDate = s.CurrentDate.AddDays(3), Status = TradeStatus.InTransit };
    s.TradeContracts.Add(bought);
    Warfare.AnnexNation(s, spain, france);
    Check(spain.Gold == 5000 && france.Gold == 0, "the conqueror takes the treasury");
    Check(bought.SellerId == spain.Id && bought.BuyerId == p.Id && bought.Status == TradeStatus.InTransit, "the shipment follows the sale to the conqueror and is still on the road");
    double daggers0 = p.GetProductStock("dagger"), spain0 = spain.Gold;
    for (int i = 0; i < 4; i++) eng.AdvanceOneDay();
    Check(bought.Status == TradeStatus.Delivered && p.GetProductStock("dagger") == daggers0 + 50, "it is delivered once to the player");
    Check(spain.Gold >= spain0 + 120 - 1e-6, "and the conqueror is the one paid");
}

Console.WriteLine("== 66. War, stage 4: Attack is one confirmed action — war is declared when the troops are sent ==");
using (var eng = NewDipEngine(150))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(6000);

    // The popup's numbers before confirming.
    var plan = eng.PlanAttack(persia.Id)!;
    var route = MapGeography.Route(p, persia);
    Check(plan.CanAttack && !plan.AtWar && plan.HomeArmy == 6000 && plan.TravelDays == route.Days && plan.Arrival == s.CurrentDate.AddDays(route.Days),
        "the plan shows the home army, that confirming declares war, and the estimated arrival (from the capitals' distance)");
    Check(!persia.AtWarWithPlayer && s.MarchingArmies.Count == 0 && p.Soldiers == 6000, "(planning changes nothing)");

    var sent = eng.ConfirmAttack(persia.Id, 2000);
    Check(sent.Ok && sent.WarDeclared, "confirming succeeds and says war was declared");
    Check(persia.AtWarWithPlayer, "war is on");
    Check(s.MarchingArmies.Count == 1 && s.MarchingArmies[0].Strength == 2000 && s.MarchingArmies[0].AttackerNationId == p.Id && s.MarchingArmies[0].TargetNationId == persia.Id,
        "and the army is marching — from the same single action");
    Check(p.Soldiers == 4000, "the 2,000 soldiers left the home army");
    var march = s.MarchingArmies[0];
    Check(march.StartDate == s.CurrentDate && march.ArrivalDate == s.CurrentDate.AddDays(plan.TravelDays) && march.TotalDays == plan.TravelDays,
        "it set out today and arrives on the date the plan promised");
    Check(HasLog(eng, "declared war on") && HasLog(eng, "soldiers march on"), "both the declaration and the march are reported");

    // A second army into the war that is already on: no new declaration.
    var more = eng.ConfirmAttack(persia.Id, 1000);
    Check(more.Ok && !more.WarDeclared && s.MarchingArmies.Count == 2 && p.Soldiers == 3000, "with war already on, confirming just sends more troops");
}

using (var eng = NewDipEngine(151))
{
    // Invalid deployments change nothing: no war, no soldiers taken, no cooldown spent.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(3000);
    double rel0 = persia.RelationToPlayer;
    int logs0 = s.EventLog.Count, moves0 = s.Movements.Count;
    foreach (var (troops, why) in new[] { (0, "zero soldiers"), (-5, "a negative number"), (3001, "more than are at home"), (499, "fewer than the minimum army") })
    {
        var r = eng.ConfirmAttack(persia.Id, troops);
        Check(!r.Ok && !r.WarDeclared && !persia.AtWarWithPlayer && p.Soldiers == 3000 && s.MarchingArmies.Count == 0, $"{why}: refused, no war declared, no soldiers leave");
    }
    Check(persia.RelationToPlayer == rel0 && s.EventLog.Count == logs0 && s.Movements.Count == moves0, "nothing was logged and relations are untouched");
    Check(!p.DiplomacyCooldowns.ContainsKey($"attack_{persia.Id}"), "and no cooldown was spent");
    Check(eng.ConfirmAttack("nowhere", 1000) is { Ok: false } && eng.ConfirmAttack(persia.Id, 1000).Ok, "(an unknown country is refused; a valid number then works)");

    // Too small an army at home to march at all.
    using var small = NewDipEngine(152);
    var sp = small.State.PlayerNation; sp.Units = UnitCatalog.SeedArmy(300);
    var smallPlan = small.PlanAttack("persia")!;
    Check(!smallPlan.CanAttack && smallPlan.BlockReason!.Contains("at least"), "with fewer soldiers than the minimum the popup says an army cannot march");
    Check(!small.ConfirmAttack("persia", 300).Ok && !Ai(small, "persia").AtWarWithPlayer && sp.Soldiers == 300, "and confirming does nothing");
}

using (var eng = NewDipEngine(153))
{
    // Treaties, protection and cooldowns stop the attack and say why.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var france = Ai(eng, "france"); var easter = Ai(eng, "easter");
    p.Units = UnitCatalog.SeedArmy(6000);

    TreatyService.Add(s, TreatyType.NonAggression, p, persia, 365);
    var nap = eng.ConfirmAttack(persia.Id, 2000);
    Check(!nap.Ok && nap.Message.Contains("non-aggression") && !persia.AtWarWithPlayer && p.Soldiers == 6000 && s.MarchingArmies.Count == 0, "a non-aggression pact stops the attack and explains it");
    Check(eng.PlanAttack(persia.Id)!.BlockReason!.Contains("non-aggression"), "the popup shows the same reason");

    TreatyService.Add(s, TreatyType.DefensiveAlliance, p, spain);
    Check(eng.ConfirmAttack(spain.Id, 2000).Message.Contains("allied") && !spain.AtWarWithPlayer && p.Soldiers == 6000, "an alliance stops it");

    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, p, france, 730);
    Check(eng.ConfirmAttack(france.Id, 2000).Message.Contains("guaranteed") && !france.AtWarWithPlayer && p.Soldiers == 6000, "a guarantee we gave stops it");

    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "annexation_ban", TargetId = easter.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(90) });
    var banned = eng.ConfirmAttack(easter.Id, 2000);
    Check(!banned.Ok && banned.Message.Contains("Assembly") && !easter.AtWarWithPlayer && p.Soldiers == 6000, "an Assembly ban stops it, and no war is declared");

    // The 30-day cooldown on declaring war on the same country again.
    var hungary = Ai(eng, "nepal");
    Check(eng.ConfirmAttack(hungary.Id, 1000).Ok, "(a free target can be attacked)");
    hungary.AtWarWithPlayer = false; s.MarchingArmies.Clear();   // peace again
    var again = eng.ConfirmAttack(hungary.Id, 1000);
    Check(!again.Ok && again.Message.Contains("again") && !hungary.AtWarWithPlayer, "declaring war on the same country again within 30 days is refused");
    for (int i = 0; i < 31; i++) eng.AdvanceOneDay();
    Check(eng.ConfirmAttack(hungary.Id, 1000).Ok && hungary.AtWarWithPlayer, "after the cooldown it works again");

    // A won battle waits for its decision first.
    var arabia = Ai(eng, "japan");
    arabia.AtWarWithPlayer = true;
    s.PendingVictories.Add(new VictoryDecision { LoserId = arabia.Id, LoserName = arabia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    int home = p.Soldiers;
    Check(!eng.ConfirmAttack(arabia.Id, 1000).Ok && p.Soldiers == home, "with a victory over it awaiting a decision, no new army is sent");
}

using (var eng = NewDipEngine(154))
{
    // The vulnerability warning grows with what is sent away.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(2000);
    persia.Units = UnitCatalog.SeedArmy(1000);
    var calm = eng.HomeRiskIfSent(persia.Id, 500);
    Check(calm.Level == "ok" && calm.Remaining == 1500 && calm.Text.Contains("cannot defend your home"), "a small force leaves a comfortable home army (the general warning is still shown)");
    persia.Units = UnitCatalog.SeedArmy(5000);
    var caution = eng.HomeRiskIfSent(persia.Id, 1000);
    Check(caution.Level == "caution" && caution.Text.Contains(persia.Name) && caution.Text.Contains("2,500"), "an enemy that would outnumber the home army is named, with the size of the strike it could answer with");
    var danger = eng.HomeRiskIfSent(persia.Id, 1800);
    Check(danger.Level == "danger" && danger.Remaining == 200 && danger.Text.Contains("overrun"), "sending nearly everything warns that the country could be overrun");
    Check(eng.HomeRiskIfSent(persia.Id, 2000).Remaining == 0 && eng.HomeRiskIfSent(persia.Id, 9999).Remaining == 0, "the remaining army is never negative");
}

Console.WriteLine("== 67. War, stage 5: peace is negotiated, not bought ==");
Check(typeof(GameEngine).GetMethod("SueForPeace") is null && typeof(Balance).GetField("PeaceTributeCost") is null && typeof(Balance).GetField("AiSueForPeaceChancePerDay") is null,
    "the unconditional 10-gold Sue for Peace is gone (no fixed-price peace, no unilateral AI peace)");

using (var eng = NewDipEngine(160))
{
    // A proposal moves nothing and ends nothing; what is offered must really be owned.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 200_000; persia.Gold = 200_000; p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace() }).Outcome == DipOutcome.Invalid, "there are no talks without a war");
    Check(eng.DeclareWar(persia.Id) is null, "(war declared)");

    double pg = p.Gold, ag = persia.Gold;
    var sent = eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace(), PeaceTerm.Gold(p.Id, persia.Id, 5000) });
    Check(sent.Ok, "terms are proposed");
    var offer = eng.PendingPeaceOffer(persia.Id)!;
    Check(offer.IsPending && offer.ProposerId == p.Id && offer.ReceiverId == persia.Id && offer.Round == 1 && offer.ParentId is null, "the offer waits for the receiver (round 1)");
    Check(persia.AtWarWithPlayer && p.Gold == pg && persia.Gold == ag && s.WarFronts.Count == 1, "proposing neither ends the war nor moves any gold");
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace() }).Outcome == DipOutcome.Invalid, "only one proposal is open at a time");
    Check(eng.WithdrawPeaceOffer(offer.Id).Ok && offer.Status == PeaceOfferStatus.Withdrawn && eng.PendingPeaceOffer(persia.Id) is null, "a proposer can take its offer back");

    int offers0 = s.PeaceOffers.Count;
    string? Refused(params PeaceTerm[] terms) { var r = eng.ProposePeace(persia.Id, terms); return r.Outcome == DipOutcome.Invalid ? r.Message : null; }
    Check(Refused(PeaceTerm.Gold(p.Id, persia.Id, p.Gold + 1))?.Contains("cannot give") == true, "no one can offer more gold than it has");
    Check(Refused(PeaceTerm.Gold(p.Id, persia.Id, 150_000), PeaceTerm.Gold(p.Id, persia.Id, 150_000))?.Contains("cannot give") == true, "the same gold cannot be promised twice in one offer");
    Check(Refused(PeaceTerm.Gold(persia.Id, p.Id, persia.Gold + 1))?.Contains("Iran") == true, "nor can the other side be asked for gold it does not hold");
    p.Helmets = 10;
    Check(Refused(PeaceTerm.Goods(p.Id, persia.Id, "helmet", 100))?.Contains("cannot give") == true, "goods beyond the stock are refused");
    Check(Refused(PeaceTerm.Goods(p.Id, persia.Id, "unobtainium", 1))?.Contains("Unknown") == true, "unknown goods are refused");
    Check(Refused(PeaceTerm.Gold(p.Id, persia.Id, 0)) is not null && Refused(PeaceTerm.Gold(p.Id, persia.Id, -50)) is not null, "a payment must be above zero");
    Check(Refused(PeaceTerm.Gold(p.Id, p.Id, 10)) is not null, "a term must pass something from one country to the other");
    Check(Refused() is not null && Refused(PeaceTerm.Peace(), PeaceTerm.Peace()) is not null, "an offer needs terms, and peace is listed once");
    s.Colonies.Add(new Colony { Id = "col-persia", Name = "Hormuz", OwnerId = persia.Id, Population = 100, Farms = 1, Mines = 0 });
    Check(Refused(PeaceTerm.Colony(p.Id, persia.Id, "col-persia"))?.Contains("does not own") == true, "a colony the giver does not own cannot be offered");
    Check(Refused(PeaceTerm.Colony(persia.Id, p.Id, "nowhere"))?.Contains("does not exist") == true, "nor one that does not exist");
    Check(Refused(PeaceTerm.Annexation(p.Id, persia.Id))?.Contains("own country") == true, "the player cannot offer its own country for annexation");
    Check(Refused(PeaceTerm.Annexation(persia.Id, p.Id), PeaceTerm.Gold(p.Id, persia.Id, 10))?.Contains("no other terms") == true, "an annexation stands alone");
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "annexation_ban", TargetId = persia.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(90) });
    Check(Refused(PeaceTerm.Annexation(persia.Id, p.Id))?.Contains("Assembly") == true, "an annexation the Assembly has banned is refused (the usual ownership rules apply)");
    s.ActiveAssemblyPolicies.Clear();
    Check(s.PeaceOffers.Count == offers0 && p.Gold == pg && persia.Gold == ag && persia.AtWarWithPlayer, "every refused proposal changed nothing");
}

using (var eng = NewDipEngine(161))
{
    // The receiver can accept, reject or counter. Here the player receives the AI's terms.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 200_000; persia.Gold = 200_000; p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    eng.DeclareWar(persia.Id);
    PeaceOffer AiOffers(double gold) => PeaceService.Propose(s, persia.Id, persia, new[] { PeaceTerm.Peace(), PeaceTerm.Gold(p.Id, persia.Id, gold) }).Offer!;

    var o1 = AiOffers(3000);
    Check(o1.ProposerId == persia.Id && o1.ReceiverId == p.Id && eng.PeaceOffersForPlayer.Any(o => o.Id == o1.Id), "the AI can propose: the offer waits for the player");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer && m.CountryId == persia.Id && m.Details.Contains("pays 3,000 Gold to Iran")), "the proposal is in the inbox, listing who pays whom");
    Check(persia.AtWarWithPlayer && p.Gold == 200_000 && persia.Gold == 200_000, "(nothing moved when it was proposed)");

    // Reject: the war goes on, nothing is transferred.
    Check(eng.RejectPeaceOffer(o1.Id).Ok && o1.Status == PeaceOfferStatus.Rejected, "the player rejects");
    Check(persia.AtWarWithPlayer && p.Gold == 200_000 && persia.Gold == 200_000, "a rejected offer leaves the war on and moves nothing");
    Check(eng.AcceptPeaceOffer(o1.Id).Outcome == DipOutcome.Invalid && persia.AtWarWithPlayer, "a settled offer cannot be accepted afterwards");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer && m.Summary.Contains("rejected")), "the rejection is in the inbox");

    // Counter: different terms go back, one round later.
    var o2 = AiOffers(3000);
    var counter = eng.CounterPeaceOffer(o2.Id, new[] { PeaceTerm.Peace(), PeaceTerm.Gold(p.Id, persia.Id, 1000) });
    var o3 = eng.PendingPeaceOffer(persia.Id)!;
    Check(counter.Ok && o2.Status == PeaceOfferStatus.Countered, "the player counters: their offer is answered, not accepted");
    Check(o3.Round == 2 && o3.ParentId == o2.Id && o3.ProposerId == p.Id && o3.ReceiverId == persia.Id && o3.Terms.Single(t => t.Kind == PeaceTermKind.Gold).Amount == 1000, "the counteroffer is round 2, from the player, with the new terms");
    Check(persia.AtWarWithPlayer && p.Gold == 200_000, "a counteroffer moves nothing either");
    Check(eng.RejectPeaceOffer(o3.Id).Outcome == DipOutcome.Invalid, "(the player cannot answer its own counteroffer)");
    eng.WithdrawPeaceOffer(o3.Id);

    // The last round has no counter.
    var last = AiOffers(500); last.Round = Balance.PeaceMaxRounds;
    Check(eng.CounterPeaceOffer(last.Id, new[] { PeaceTerm.Peace() }).Message.Contains("last round"), "in the last round the only answers are accept or reject");
    eng.RejectPeaceOffer(last.Id);

    // Accept: every term applied once, the war ends.
    var o4 = AiOffers(3000);
    double sum0 = p.Gold + persia.Gold;
    Check(eng.AcceptPeaceOffer(o4.Id).Ok, "the player accepts");
    Check(!persia.AtWarWithPlayer && p.Gold == 197_000 && persia.Gold == 203_000 && p.Gold + persia.Gold == sum0, "the terms are applied exactly: 3,000 gold moved, none created or lost");
    Check(s.PeaceOffers.All(o => o.NationId != persia.Id) && s.WarFronts.All(f => f.NationId != persia.Id), "the war's talks and record are cleared");
    Check(eng.AcceptPeaceOffer(o4.Id).Outcome == DipOutcome.Invalid && p.Gold == 197_000 && persia.Gold == 203_000, "accepting again does nothing: the terms apply only once");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceAgreement && m.Details.Contains("pays 3,000 Gold to Iran") && m.Details.Contains("The war ends")), "the final agreement is in the inbox with every term");
    Check(DiplomacyService.ToDisplayRating(persia.RelationToPlayer) == Balance.PeaceAgreementRating, "a small payment on top of peace leaves the country with the ordinary post-peace regard");
}

foreach (bool aiPays in new[] { false, true })
{
    // How the other country regards the player afterwards depends on who paid.
    using var eng = NewDipEngine(168);
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 200_000; persia.Gold = 200_000; p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    eng.DeclareWar(persia.Id);
    double big = PeaceService.LeverageOf(s, persia).Scale * 0.5;
    var terms = new[] { PeaceTerm.Peace(), aiPays ? PeaceTerm.Gold(persia.Id, p.Id, big) : PeaceTerm.Gold(p.Id, persia.Id, big) };
    Check(PeaceService.Accept(s, PeaceService.Propose(s, persia.Id, persia, terms).Offer!).Ok, aiPays ? "(the AI pays a large sum for peace)" : "(the AI is paid a large sum for peace)");
    Check(DiplomacyService.ToDisplayRating(persia.RelationToPlayer) == (aiPays ? Balance.PeaceHumbledRating : Balance.PeaceSatisfiedRating),
        aiPays ? "a country that paid heavily regards the player coldly" : "a country that was paid heavily is content");
}

using (var eng = NewDipEngine(162))
{
    // Goods and a colony change hands by agreement; if the giver can no longer pay at the time of acceptance, nothing moves.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 100_000; persia.Gold = 100_000; p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    p.Iron = 2000; persia.Iron = 500; p.Population = 20_000_000;
    s.Colonies.Add(new Colony { Id = "col-p", Name = "Cape Town", OwnerId = p.Id, Population = 4000, Farms = 3, Mines = 2 });
    p.Farms += 3; p.Mines += 2;
    eng.DeclareWar(persia.Id);

    var terms = new[] { PeaceTerm.Peace(), PeaceTerm.Gold(p.Id, persia.Id, 1000), PeaceTerm.Goods(p.Id, persia.Id, "iron", 800), PeaceTerm.Colony(p.Id, persia.Id, "col-p") };
    var offer = PeaceService.Propose(s, persia.Id, persia, terms).Offer!;
    double pPop = p.Population, aPop = persia.Population; int pFarms = p.Farms, aFarms = persia.Farms, pMines = p.Mines, aMines = persia.Mines;
    double pIron = p.Iron, aIron = persia.Iron;

    // The player spends the gold before accepting: the terms can no longer be carried out, so none of them is.
    double saved = p.Gold; p.Gold = 100;
    var failed = eng.AcceptPeaceOffer(offer.Id);
    Check(failed.Outcome == DipOutcome.Invalid && persia.AtWarWithPlayer && p.Gold == 100 && p.Iron == pIron && persia.Iron == aIron && s.Colonies.Single().OwnerId == p.Id,
        "terms that can no longer be met are refused as a whole: no gold, goods or colony moves and the war goes on");
    Check(offer.Status == PeaceOfferStatus.Expired, "the offer falls through");

    p.Gold = saved;
    var again = PeaceService.Propose(s, persia.Id, persia, terms).Offer!;
    Check(eng.AcceptPeaceOffer(again.Id).Ok, "with the gold back, the same terms are accepted");
    Check(p.Gold == saved - 1000 && persia.Gold == 101_000, "the gold moved once");
    Check(p.Iron == pIron - 800 && persia.Iron == aIron + 800, "the iron moved once");
    var colony = s.Colonies.Single();
    Check(colony.OwnerId == persia.Id && p.Population == pPop - 4000 && persia.Population == aPop + 4000 && p.Farms == pFarms - 3 && persia.Farms == aFarms + 3
          && p.Mines == pMines - 2 && persia.Mines == aMines + 2, "the colony changed owner with its people and buildings, leaving the giver's totals correct");
    Check(!persia.AtWarWithPlayer, "and the war is over");
}

using (var eng = NewDipEngine(163))
{
    // AI answers: the three answers all occur, and the price follows how the war stands.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 200_000; persia.Gold = 200_000;
    p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    eng.DeclareWar(persia.Id);
    var lev = PeaceService.LeverageOf(s, persia);
    PeaceVerdict Ask(double goldTheyPay) => PeaceService.Evaluate(s, persia, new PeaceOffer
    {
        NationId = persia.Id, ProposerId = p.Id, ReceiverId = persia.Id, CreatedDate = s.CurrentDate,
        Terms = new List<PeaceTerm> { PeaceTerm.Peace(), PeaceTerm.Gold(persia.Id, p.Id, goldTheyPay) },
    });
    var ladder = Enumerable.Range(0, 16).Select(i => (K: i * 0.1, V: Ask(i * 0.1 * lev.Scale).Decision)).ToList();
    Check(Math.Abs(lev.Lean) < 0.1, "(even armies: the war is balanced)");
    Check(ladder.Any(x => x.V == PeaceDecision.Accept) && ladder.Any(x => x.V == PeaceDecision.Counter) && ladder.Any(x => x.V == PeaceDecision.Reject), "an even war: small demands are accepted, middling ones countered, greedy ones rejected");
    Check(ladder.First().V == PeaceDecision.Accept && ladder.Last().V == PeaceDecision.Reject, "from a plain peace to a ruinous demand the answer only gets harder");
    int firstReject = ladder.FindIndex(x => x.V == PeaceDecision.Reject);
    Check(ladder.Skip(firstReject).All(x => x.V == PeaceDecision.Reject), "once they refuse, bigger demands are refused too");

    // Winning vs losing: how much will they pay for peace?
    double MaxAccepted(int playerArmy, int aiArmy)
    {
        p.Units = UnitCatalog.SeedArmy(playerArmy); persia.Units = UnitCatalog.SeedArmy(aiArmy);
        var l = PeaceService.LeverageOf(s, persia);
        double best = -1;
        for (int i = 0; i <= 30; i++) if (Ask(i * 0.05 * l.Scale).Decision == PeaceDecision.Accept) best = i * 0.05;
        return best;
    }
    double losing = MaxAccepted(20_000, 2_000), winning = MaxAccepted(1_500, 15_000);
    Check(losing > winning + 0.3, $"the losing side concedes more than the winning side (it would pay up to {losing:0.00}x the stakes, against {winning:0.00}x)");
    Check(winning < 0, "a winning country accepts no payment from its own pocket: it wants peace to cost the other side");
    p.Units = UnitCatalog.SeedArmy(1_500); persia.Units = UnitCatalog.SeedArmy(15_000);
    var plainPeace = PeaceService.Evaluate(s, persia, new PeaceOffer { NationId = persia.Id, ProposerId = p.Id, ReceiverId = persia.Id, CreatedDate = s.CurrentDate, Terms = new List<PeaceTerm> { PeaceTerm.Peace() } });
    Check(plainPeace.Decision == PeaceDecision.Counter && plainPeace.Counter!.Any(t => t.Kind == PeaceTermKind.Gold && t.GiverId == p.Id && t.ReceiverId == persia.Id), "a winning country answers a plain peace by asking for gold from the player");
    p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(2_000);
    Check(PeaceService.Evaluate(s, persia, new PeaceOffer { NationId = persia.Id, ProposerId = p.Id, ReceiverId = persia.Id, CreatedDate = s.CurrentDate, Terms = new List<PeaceTerm> { PeaceTerm.Peace() } }).Decision == PeaceDecision.Accept,
        "a country that is being beaten accepts a plain peace");

    // Battles, losses and exhaustion move the balance.
    p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    double before = PeaceService.LeverageOf(s, persia).Lean;
    PeaceService.RecordBattle(s, persia, playerWon: false, playerLosses: 1500, enemyLosses: 300);
    PeaceService.RecordBattle(s, persia, playerWon: false, playerLosses: 1200, enemyLosses: 200);
    double afterWins = PeaceService.LeverageOf(s, persia).Lean;
    Check(afterWins > before + 0.1, "battles the enemy won, and the soldiers each side lost, make it more demanding");
    persia.Units = UnitCatalog.SeedArmy(300);
    Check(PeaceService.LeverageOf(s, persia).Exhausted && PeaceService.LeverageOf(s, persia).Lean < afterWins - 0.2, "an army too small to go on fighting weakens its hand");
    p.Units = UnitCatalog.SeedArmy(1000);
    var withMarch = PeaceService.LeverageOf(s, persia).EnemyPower;
    s.MarchingArmies.Add(new MarchingArmy { AttackerNationId = p.Id, TargetNationId = persia.Id, Force = UnitCatalog.SeedArmy(4000) });
    Check(PeaceService.LeverageOf(s, persia).EnemyPower > withMarch + 1000, "armies already on the road count toward the ability to fight on");
}

using (var eng = NewDipEngine(164))
{
    // Through the daily step: the AI answers a day later — accepting, countering or rejecting — and a rejection has a pause.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 200_000; persia.Gold = 200_000;
    p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    eng.DeclareWar(persia.Id);
    var lev = PeaceService.LeverageOf(s, persia);
    PeaceOffer Offer(double goldTheyPay) => new() { NationId = persia.Id, ProposerId = p.Id, ReceiverId = persia.Id, CreatedDate = s.CurrentDate,
        Terms = new List<PeaceTerm> { PeaceTerm.Peace(), PeaceTerm.Gold(persia.Id, p.Id, goldTheyPay) } };
    double counterAsk = Enumerable.Range(1, 20).Select(i => i * 0.05 * lev.Scale).First(g => PeaceService.Evaluate(s, persia, Offer(g)).Decision == PeaceDecision.Counter);
    double rejectAsk = lev.Scale * 1.4;
    Check(PeaceService.Evaluate(s, persia, Offer(rejectAsk)).Decision == PeaceDecision.Reject, "(a demand of 1.4x the stakes would be refused)");

    // A greedy demand: refused a day later.
    Check(eng.ProposePeace(persia.Id, Offer(rejectAsk).Terms).Ok, "a greedy proposal is sent");
    var greedy = eng.PendingPeaceOffer(persia.Id)!;
    Check(greedy.DueDate == s.CurrentDate.AddDays(Balance.PeaceReplyDays), "(the envoys answer after the reply time)");
    eng.AdvanceOneDay();
    Check(greedy.Status == PeaceOfferStatus.Rejected && persia.AtWarWithPlayer, "the AI rejects it: the war goes on");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer && m.Summary.Contains("rejected your terms")), "its rejection is in the inbox");
    Check(eng.ProposePeace(persia.Id, Offer(0).Terms).Message.Contains("turned your terms down"), "after a rejection there is a short pause before the next opening proposal");
    for (int i = 0; i < Balance.PeaceRetryDays; i++) { p.Gold = 200_000; eng.AdvanceOneDay(); }

    // A middling demand: countered with terms the AI itself would take.
    lev = PeaceService.LeverageOf(s, persia);
    counterAsk = Enumerable.Range(1, 20).Select(i => i * 0.05 * lev.Scale).First(g => PeaceService.Evaluate(s, persia, Offer(g)).Decision == PeaceDecision.Counter);
    Check(eng.ProposePeace(persia.Id, Offer(counterAsk).Terms).Ok, "a middling proposal is sent");
    var middling = eng.PendingPeaceOffer(persia.Id)!;
    eng.AdvanceOneDay();
    var theirCounter = eng.PendingPeaceOffer(persia.Id);
    Check(middling.Status == PeaceOfferStatus.Countered && theirCounter is not null && theirCounter.ProposerId == persia.Id && theirCounter.ReceiverId == p.Id && theirCounter.Round == 2 && theirCounter.ParentId == middling.Id,
        "the AI counters: a round-2 offer from it, waiting for the player");
    var counterGold = theirCounter!.Terms.Where(t => t.Kind == PeaceTermKind.Gold).Sum(t => (t.ReceiverId == p.Id ? 1 : -1) * t.Amount);
    Check(counterGold < counterAsk, "it pays less than the player asked");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer && m.Summary.Contains("counteroffer")), "the counteroffer is in the inbox");
    Check(persia.AtWarWithPlayer, "(still at war: an offer is not an agreement)");
    double p1 = p.Gold, a1 = persia.Gold;
    Check(eng.AcceptPeaceOffer(theirCounter.Id).Ok && !persia.AtWarWithPlayer, "the player accepts their counter: the war ends");
    Check(Math.Abs((p.Gold - p1) - counterGold) < 0.01 && Math.Abs((persia.Gold - a1) + counterGold) < 0.01, "and exactly the countered gold moved");

}

using (var eng = NewDipEngine(165))
{
    // A country being beaten accepts a plain peace through the daily step, and a desperate one even annexation.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    p.Gold = 1e9; persia.Gold = 50_000;
    p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(2_000);
    eng.DeclareWar(persia.Id);
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace() }).Ok, "a plain peace is proposed to a country that is being beaten");
    eng.AdvanceOneDay();
    Check(!persia.AtWarWithPlayer && s.Inbox.Any(m => m.Topic == InboxTopic.PeaceAgreement), "it accepts: the war ends and the agreement is in the inbox");

    spain.AtWarWithPlayer = true; PeaceService.OnWarStarted(s, spain, playerStarted: true);
    spain.Units = UnitCatalog.SeedArmy(100);
    var front = PeaceService.Front(s, spain); front.PlayerBattlesWon = 6; front.EnemyLosses = 5000;
    Check(PeaceService.LeverageOf(s, spain).PLose >= 0.99, "(a country that is destroyed expects to lose everything)");
    double gold0 = p.Gold, pop0 = p.Population; long spainPop = spain.Population; double spainGold = spain.Gold;
    Check(eng.ProposePeace(spain.Id, new[] { PeaceTerm.Annexation(spain.Id, p.Id) }).Ok, "the player proposes to annex it");
    Check(!spain.IsEliminated && spain.AtWarWithPlayer, "(proposing annexes nothing)");
    eng.AdvanceOneDay();
    Check(spain.IsEliminated && !spain.AtWarWithPlayer && spain.AnnexedById == p.Id && s.TotalPopulation(p) > pop0 + spainPop / 2 && spain.Gold == 0 && p.Gold > gold0, "a destroyed country agrees: it is annexed by agreement, once, its treasury moves and its people live on as a territory");
    Check(s.PeaceOffers.All(o => o.NationId != spain.Id) && s.NationsAnnexedByPlayer == 1, "(counted as one annexation; its talks are cleared)");
}

using (var eng = NewDipEngine(166))
{
    // The AI can start the talks, and an offer left unanswered lapses: it does not end the war.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(2_500); persia.Gold = 40_000;
    eng.DeclareWar(persia.Id);
    PeaceOffer? first = null;
    for (int i = 0; i < 700 && first is null; i++)
    {
        p.Gold = 1e9; p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(2_500);
        eng.AdvanceOneDay();
        first = s.PeaceOffers.FirstOrDefault(o => o.ProposerId == persia.Id);
    }
    Check(first is not null && first.ReceiverId == p.Id && first.IsPending, "a country that is losing the war puts terms to the player by itself");
    Check(persia.AtWarWithPlayer, "its proposal does not end the war");
    Check(first!.Terms.Any(t => t.Kind == PeaceTermKind.Peace) && first.Terms.Where(t => t.Kind == PeaceTermKind.Gold).All(t => t.GiverId == persia.Id && t.ReceiverId == p.Id),
        "the losing side offers peace, paying the player (or plain peace)");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer && m.Summary.Contains("proposes peace terms") && m.Details.Contains("The war ends")), "the proposal is in the inbox");
    double gold = p.Gold, theirGold = persia.Gold;
    for (int i = 0; i < Balance.PeaceOfferDays; i++) { p.Gold = 1e9; p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(2_500); eng.AdvanceOneDay(); }
    Check(first.Status == PeaceOfferStatus.Expired && persia.AtWarWithPlayer, "left unanswered, it lapses and the war goes on: no country makes peace by itself");
    Check(s.Inbox.Any(m => m.Summary.Contains("lapsed")), "the lapse is in the inbox");

    // What a winning country would ask for.

    p.Units = UnitCatalog.SeedArmy(1_500); persia.Units = UnitCatalog.SeedArmy(15_000); p.Gold = 100_000;
    var levWin = PeaceService.LeverageOf(s, persia);
    var win = PeaceService.ProposalFor(s, persia, levWin);
    var ask = win.Where(t => t.Kind == PeaceTermKind.Gold).ToList();
    Check(levWin.Lean > 0.3 && win.Any(t => t.Kind == PeaceTermKind.Peace) && ask.Count == 1 && ask[0].GiverId == p.Id && ask[0].ReceiverId == persia.Id && ask[0].Amount > 0 && ask[0].Amount <= p.Gold * 0.6,
        "a winning country names a price in gold the player can pay");
    p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    Check(PeaceService.ProposalFor(s, persia, PeaceService.LeverageOf(s, persia)).All(t => t.Kind == PeaceTermKind.Peace), "an even war gets a plain peace");
}

using (var eng = NewDipEngine(167))
{
    // Negotiation state survives save / load: the war, the marching army, the record and the offers in play.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 100_000; persia.Gold = 100_000; p.Units = UnitCatalog.SeedArmy(6000); persia.Units = UnitCatalog.SeedArmy(6000);
    var atk = eng.ConfirmAttack(persia.Id, 2000);
    Check(atk.Ok && atk.WarDeclared, "(war declared and the army sent)");
    var front = PeaceService.Front(s, persia); front.PlayerBattlesWon = 2; front.EnemyBattlesWon = 1; front.PlayerLosses = 900; front.EnemyLosses = 1500; front.LastBattleDate = s.CurrentDate;
    var ai1 = PeaceService.Propose(s, persia.Id, persia, new[] { PeaceTerm.Peace(), PeaceTerm.Gold(p.Id, persia.Id, 2500) }).Offer!;
    persia.Iron = 100;
    var mine = eng.CounterPeaceOffer(ai1.Id, new[] { PeaceTerm.Peace(), PeaceTerm.Goods(persia.Id, p.Id, "iron", 5) }).Ok;
    var pending = eng.PendingPeaceOffer(persia.Id)!;
    var march = s.MarchingArmies.Single();
    Check(mine && pending.Round == 2, "(a counteroffer is in play)");

    await eng.SaveAsync();
    s.PeaceOffers.Clear(); s.WarFronts.Clear(); s.MarchingArmies.Clear(); persia.AtWarWithPlayer = false;
    await eng.LoadAsync();
    var s2 = eng.State; var persia2 = Ai(eng, "persia");
    Check(persia2.AtWarWithPlayer, "the war survives save / load");
    var march2 = s2.MarchingArmies.Single();
    Check(march2.Strength == 2000 && march2.StartDate == march.StartDate && march2.ArrivalDate == march.ArrivalDate && march2.TotalDays == march.TotalDays && march2.TargetNationId == persia.Id,
        "the marching army keeps its force, start date and arrival date");
    Check(s2.PeaceOffers.Count == 2, "every offer survives (the countered one and the counteroffer)");
    var p2 = s2.PeaceOffers.Single(o => o.IsPending);
    Check(p2.Id == pending.Id && p2.Round == 2 && p2.ParentId == ai1.Id && p2.ProposerId == p.Id && p2.ReceiverId == persia.Id && p2.DueDate == pending.DueDate
          && p2.Terms.Count == 2 && p2.Terms.Any(t => t.Kind == PeaceTermKind.Goods && t.ProductId == "iron" && t.Amount == 5 && t.GiverId == persia.Id && t.ReceiverId == p.Id),
        "the pending counteroffer keeps its round, parent, receiver, due date and terms");
    Check(s2.PeaceOffers.Single(o => o.Id == ai1.Id).Status == PeaceOfferStatus.Countered, "and the offer it answered stays countered");
    var f2 = s2.WarFronts.Single();
    Check(f2.NationId == persia.Id && f2.PlayerBattlesWon == 2 && f2.EnemyBattlesWon == 1 && f2.PlayerLosses == 900 && f2.EnemyLosses == 1500 && f2.StartDate == front.StartDate && f2.LastBattleDate == front.LastBattleDate,
        "the war record survives");
    Check(s2.Inbox.Any(m => m.Topic == InboxTopic.PeaceOffer), "and so do the negotiation messages");
    // The restored negotiation still works: the AI answers the pending counteroffer, once.
    eng.AdvanceOneDay();
    Check(!s2.PeaceOffers.Any(o => o.Id == pending.Id && o.IsPending), "after loading, the AI still answers the pending offer when its time comes");
}

Console.WriteLine("== 68. War, review fixes: colonies are checked together, annexation may carry the peace, a fallen player has no talks, no empty army ==");
using (var eng = NewDipEngine(170))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 100_000; persia.Gold = 100_000; p.Units = UnitCatalog.SeedArmy(5000); persia.Units = UnitCatalog.SeedArmy(5000);
    eng.DeclareWar(persia.Id);

    // Two colonies that each fit the giver's totals, but not together.
    p.Population = 5000; p.Farms = 10; p.Mines = 3;
    s.Colonies.Add(new Colony { Id = "ca", Name = "Alpha", OwnerId = p.Id, Population = 3000, Farms = 8, Mines = 2 });
    s.Colonies.Add(new Colony { Id = "cb", Name = "Beta", OwnerId = p.Id, Population = 3000, Farms = 8, Mines = 2 });
    var both = eng.ProposePeace(persia.Id, new[] { PeaceTerm.Colony(p.Id, persia.Id, "ca"), PeaceTerm.Colony(p.Id, persia.Id, "cb") });
    Check(both.Outcome == DipOutcome.Invalid && both.Message.Contains("no longer all"), "two colonies are checked together against the giver's people and buildings");
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Colony(p.Id, persia.Id, "ca") }).Ok, "one of them is fine");
    var offer = eng.PendingPeaceOffer(persia.Id)!;
    eng.WithdrawPeaceOffer(offer.Id);

    // The peace itself may be listed with an annexation (the player does not have to untick it).
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace(), PeaceTerm.Annexation(persia.Id, p.Id) }).Ok, "an annexation may be proposed together with the peace");
    eng.WithdrawPeaceOffer(eng.PendingPeaceOffer(persia.Id)!.Id);
    Check(eng.ProposePeace(persia.Id, new[] { PeaceTerm.Gold(p.Id, persia.Id, 10), PeaceTerm.Annexation(persia.Id, p.Id) }).Message.Contains("no other terms"), "but no other term can ride along with an annexation");

    // No empty army, and none sent against a country whose fate is waiting.
    Check(!eng.LaunchInvasion(persia.Id, 0).ok && !eng.LaunchInvasion(persia.Id, -3).ok && s.MarchingArmies.Count == 0, "an invasion of no soldiers is refused");
    s.PendingVictories.Add(new VictoryDecision { LoserId = persia.Id, LoserName = persia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    int home = p.Soldiers;
    Check(!eng.LaunchInvasion(persia.Id, 1000).ok && p.Soldiers == home, "nor is an invasion launched while a victory over it awaits its decision");
    s.PendingVictories.Clear();
}

using (var eng = NewDipEngine(171))
{
    // Once the player's country has fallen there are no more talks, offers or warnings.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    p.Units = UnitCatalog.SeedArmy(2000); persia.Units = UnitCatalog.SeedArmy(15_000); spain.Units = UnitCatalog.SeedArmy(15_000);
    eng.DeclareWar(persia.Id); eng.DeclareWar(spain.Id);
    Warfare.AnnexNation(s, persia, p);
    int offers = s.PeaceOffers.Count, inbox = s.Inbox.Count, warnings = s.ActiveWarnings.Count;
    for (int i = 0; i < 400; i++) { s.CurrentDate = s.CurrentDate.AddDays(1); PeaceService.Advance(s); }
    Check(p.IsEliminated && s.PeaceOffers.Count == offers && s.Inbox.Count == inbox && s.ActiveWarnings.Count == warnings, "after the player falls, nobody keeps proposing peace to a dead country");
}

Console.WriteLine("== 69. Annexation: the territory goes on living for its master — its tax, votes and records belong to the master; it earns nothing as a country ==");
using (var eng = NewDipEngine(180))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.EnsureTaxationInitialized(); persia.EnsureTaxationInitialized();
    p.TaxRates = new TaxRates(); persia.TaxRates = new TaxRates();   // (the same rates, so the comparison is about people)
    persia.Gold = 7000;
    Check(p.CrownDomains == 1 && persia.CrownDomains == 1, "(before: each country has one crown domain)");
    int polygons = persia.Territory.Count, homePolygons = p.Territory.Count;

    using var control = NewDipEngine(180);   // the same world, without the annexation
    var pc = control.State.PlayerNation; var persiaC = Ai(control, "persia");
    pc.EnsureTaxationInitialized(); pc.TaxRates = new TaxRates(); persiaC.Gold = 7000;

    Warfare.AnnexNation(s, p, persia);
    double territoryTax = TaxationService.TotalRevenue(persia.Workforce, persia.TaxRates) * CommandStaffService.TaxMult(p) * p.TaxMult;
    Check(territoryTax > 0 && persia.Workforce.Peasants > 0, "(the territory keeps its own taxed workforce)");
    Check(p.CrownDomains == 2 && persia.CrownDomains == 0, "its crown domain became the winner's");
    Check(StatisticsService.IncomeBreakdown(p).Single(x => x.Name.StartsWith("Crown Domain")).Amount == 2 * Balance.CrownDomainIncomePerDay, "the winner's income statement shows both crown domains");

    // It is under the winner: recorded, on the map, and out of the independent countries.
    Check(persia.AnnexedById == p.Id && persia.AnnexedDate == s.CurrentDate && s.AnnexedBy(p.Id).Single().Id == persia.Id, "the annexed country records who annexed it, and when");
    Check(p.AnnexedPolygonCount == polygons && p.Territory.Count == homePolygons + polygons && persia.Territory.Count == 0, "its lands are part of the winner's realm on the map");
    Check(!s.LiveOtherNations().Any(n => n.Id == persia.Id) && s.LiveOtherNations().Count() == s.OtherNations.Count - 1, "it is no longer listed among the independent countries");

    // One day: the master collects the territory's tax (and its crown domain); the territory itself holds no gold; the control does not.
    double pBefore = p.Gold, pcBefore = pc.Gold;
    long popBefore = persia.Population;
    eng.AdvanceOneDay(); control.AdvanceOneDay();
    double extra = (p.Gold - pBefore) - (pc.Gold - pcBefore);
    Check(Math.Abs(extra - (territoryTax + Balance.CrownDomainIncomePerDay)) < 1.0, $"the winner collects the territory's tax and crown domain: {extra:N1} a day more than without the annexation (tax {territoryTax:N1} + domain {Balance.CrownDomainIncomePerDay:N0})");
    Check(persia.Gold == 0 && persiaC.Gold > 7000, "the territory keeps no treasury; the unannexed country (control) goes on earning for itself");
    Check(persia.Population != popBefore, "the territory's people go on living: births and deaths continue");

    // Weeks later: still a living territory for its master, never a country again.
    for (int i = 0; i < 40; i++) { p.Gold = Math.Max(p.Gold, 1_000_000); eng.AdvanceOneDay(); }
    Check(persia.IsEliminated && persia.AnnexedById == p.Id && persia.Gold == 0 && persia.Soldiers == 0 && persia.Units.Count == 0, "it is still annexed, holds no gold and has no army");
    Check(!s.Wars.Any(w => w.Involves(persia.Id)) && !persia.AtWarWithPlayer && !s.Treaties.Any(t => t.Involves(persia.Id)), "and takes no part in diplomacy or war");
}

using (var eng = NewDipEngine(181))
{
    // The territory runs by the existing rules: its people eat its stocks, and a shortage shows when they run out.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    Warfare.AnnexNation(s, p, persia);
    long pop0 = persia.Population;
    persia.ProductionBuildings.Remove("farm"); persia.GoodsInventory["Wheat"] = 1000;
    for (int i = 0; i < 30; i++) { p.Gold = Math.Max(p.Gold, 1_000_000); eng.AdvanceOneDay(); }
    Check(persia.GetProduct("Wheat") < 1000 && persia.ShortagePct.Count > 0, "the territory's own consumption drains its own stock, by the existing per-person rule");
    Check(persia.Population != pop0, "its population keeps changing under the existing birth and shortage rules");
    Check(persia.Gold == 0, "(and the territory itself never holds gold)");
}

using (var eng = NewDipEngine(182))
{
    // Between AI countries: income, territories and mastery pass up the chain; the territories keep their own economies, counted once.
    var s = eng.State; var spain = Ai(eng, "spain"); var france = Ai(eng, "france"); var persia = Ai(eng, "persia");
    int franceLands = france.Territory.Count;
    long francePop = france.Population, spainPop = spain.Population, persiaPop = persia.Population;
    Warfare.AnnexNation(s, spain, france);
    Check(spain.CrownDomains == 2 && france.CrownDomains == 0 && france.AnnexedById == spain.Id && spain.AnnexedPolygonCount == franceLands, "(spain annexes france)");
    int spainPolys = spain.Territory.Count;
    Warfare.AnnexNation(s, persia, spain);
    Check(persia.CrownDomains == 3 && spain.CrownDomains == 0, "when the winner is annexed in turn, all its crown domains go with it");
    Check(france.AnnexedById == persia.Id && spain.AnnexedById == persia.Id && s.AnnexedBy(persia.Id).Count == 2, "and every country it had annexed is now under the new master");
    Check(persia.AnnexedPolygonCount >= spainPolys && spain.Territory.Count == 0, "the whole realm, annexed lands included, is drawn as part of the new master's");
    Check(s.TotalPopulation(persia) == persia.Population + spain.Population + france.Population && persia.Population == persiaPop, "its people and the two territories' are counted once in the master's realm");
    for (int i = 0; i < 30; i++) eng.AdvanceOneDay();
    Check(france.Gold == 0 && spain.Gold == 0, "neither territory holds any gold: what they earn is their master's");
    Check(france.Population != francePop && spain.Population != spainPop, "...while both go on living");
}

using (var eng = NewDipEngine(184))
{
    // Assembly: an annexed country has no vote; its people add to its master's voting power (one vote per 500,000), counted once.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var all = s.AllNations().ToList();
    int own = AssemblyService.VotingPower(p), before = AssemblyService.VotingPower(p, all);
    Check(own == before && AssemblyService.VotingPower(persia, all) > 0, "(before: each country votes with its own people)");
    Warfare.AnnexNation(s, p, persia);
    int after = AssemblyService.VotingPower(p, all);
    Check(AssemblyService.VotingPower(persia, all) == 0 && AssemblyService.VotingPower(persia) == 0, "the annexed country has no vote of its own");
    Check(after == Math.Max(1, (int)((p.Population + persia.Population) / 500_000)) && after > own, "its people's votes are its master's now");
    Check(s.VotingPowerOf(p) == after, "the page-level helper agrees");
    var proposal = new AssemblyProposal { TypeId = "annexation_ban", TargetId = spain.Id, ProposerId = p.Id };
    proposal.Votes[p.Id] = true; proposal.Votes[persia.Id] = true;
    Check(proposal.VotesFor(all) == after, "a ballot counts the master's vote with the territory's people, and nothing for the territory itself");
}

using (var eng = NewDipEngine(183))
{
    // Save / load keeps who is under whom, the income and the map realm.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    Warfare.AnnexNation(s, p, persia);
    for (int i = 0; i < 5; i++) eng.AdvanceOneDay();
    int terr = p.Territory.Count, annexedPolys = p.AnnexedPolygonCount;
    long persiaPop = persia.Population; double persiaWood = persia.Wood; int persiaFarms = persia.Farms; double peasantRate = persia.TaxRates.Peasants;
    await eng.SaveAsync();
    persia.AnnexedById = null; p.CrownDomains = 1; p.AnnexedPolygonCount = 0; persia.Population = 1; persia.Wood = 0; persia.Farms = 0;
    await eng.LoadAsync();
    var p2 = eng.State.PlayerNation; var persia2 = Ai(eng, "persia");
    Check(persia2.IsEliminated && persia2.AnnexedById == p2.Id && persia2.AnnexedDate == persia.AnnexedDate && persia2.CrownDomains == 0, "the annexed country's master survives save / load");
    Check(persia2.Population == persiaPop && persia2.Wood == persiaWood && persia2.Farms == persiaFarms && persia2.TaxRates.Peasants == peasantRate, "and so do the territory's people, stocks, buildings and tax settings");
    Check(p2.CrownDomains == 2 && p2.AnnexedPolygonCount == annexedPolys && p2.Territory.Count == terr && persia2.Territory.Count == 0, "and the winner's income and realm");
    long popAfterLoad = persia2.Population;
    for (int i = 0; i < 5; i++) eng.AdvanceOneDay();
    Check(persia2.Population != popAfterLoad, "after loading, the territory goes on living");
}

Console.WriteLine("== 70. Command staff: six appointments, each a real effect, for every country ==");
using (var eng = NewDipEngine(190))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");

    // The six posts, in two groups, in the order of the screen.
    var posts = CommandStaffService.Posts;
    Check(posts.Select(x => x.Role).SequenceEqual(new[] { CommanderRole.FleetCommander, CommanderRole.LandCommander, CommanderRole.CommanderInChief,
              CommanderRole.ChiefTributeCollector, CommanderRole.HeadOfTrade, CommanderRole.HeadOfConstruction }), "six posts: fleet, land, commander-in-chief, tribute collector, head of trade, head of construction");
    Check(posts.Take(3).All(x => x.Group == StaffGroup.MilitaryLeadership) && posts.Skip(3).All(x => x.Group == StaffGroup.CivilAdministration), "three military posts and three civil ones");
    Check(posts.All(x => x.Title.Length > 0 && x.Icon.Length > 0 && x.Duty.Length > 0), "each has a title, icon and duty");
    Check((int)CommanderRole.CommanderInChief == 0 && (int)CommanderRole.LandCommander == 1 && (int)CommanderRole.FleetCommander == 2, "the old roles keep their saved numbers (new posts were appended)");

    // Candidates: three per post, one of each tier, stable, culturally drawn, never the same person twice across the staff.
    var all = posts.SelectMany(x => CommandStaffService.Candidates(p, x.Role)).ToList();
    Check(all.Count == 18 && all.Select(c => c.Name).Distinct().Count() == 18, "18 candidates, no name used twice across the staff");
    Check(posts.All(x => CommandStaffService.Candidates(p, x.Role).Select(c => c.Tier).SequenceEqual(new[] { 1, 2, 3 })), "each post offers a capable, a distinguished and a renowned candidate");
    Check(all.SequenceEqual(posts.SelectMany(x => CommandStaffService.Candidates(p, x.Role))), "the shortlist is the same every time");
    var cands = CommandStaffService.Candidates(p, CommanderRole.LandCommander);
    Check(cands[0].HireCost < cands[1].HireCost && cands[1].HireCost < cands[2].HireCost && cands[0].DailyWage < cands[1].DailyWage && cands[1].DailyWage < cands[2].DailyWage
          && cands[0].Strength == 1.0 && cands[1].Strength > 1.0 && cands[2].Strength > cands[1].Strength, "better candidates cost more, earn more, and work the post harder");
    Check(cands[0].HireCost == Balance.LandCommanderHireCost && cands[0].DailyWage == Balance.LandCommanderWage, "a capable candidate is the post's base cost and wage");
    Check(CommandStaffService.Candidates(persia, CommanderRole.LandCommander).Select(c => c.Name).SequenceEqual(CommandStaffService.Candidates(persia, CommanderRole.LandCommander).Select(c => c.Name))
          && CommandStaffService.Candidates(p, CommanderRole.LandCommander).Select(c => c.Name).SequenceEqual(cands.Select(c => c.Name)), "(and per country)");
    Check(!CommandStaffService.Candidates(persia, CommanderRole.LandCommander).Select(c => c.Name).SequenceEqual(cands.Select(c => c.Name)), "countries have their own candidates");

    // Appointing: paid once, one holder, replaced not doubled, refused when it cannot be done.
    p.Gold = 1000;
    Check(CommandStaffService.Holder(p, CommanderRole.HeadOfTrade) is null, "(vacant at the start)");
    var capable = CommandStaffService.Candidate(p, CommanderRole.HeadOfTrade, 1)!;
    Check(eng.AppointCommander(CommanderRole.HeadOfTrade, 1) is null, "a post is filled");
    var holder = CommandStaffService.Holder(p, CommanderRole.HeadOfTrade)!;
    Check(holder.Name == capable.Name && holder.Skill == 1 && holder.DailyWage == capable.DailyWage && holder.AppointedDate == s.CurrentDate, "the holder is the candidate, with his skill, wage and the date");
    Check(Math.Abs(p.Gold - (1000 - capable.HireCost)) < 1e-9, "the appointment fee was paid");
    Check(eng.AppointCommander(CommanderRole.HeadOfTrade, 1) is { } dup && dup.Contains("already holds"), "the same person cannot be appointed again");
    var renowned = CommandStaffService.Candidate(p, CommanderRole.HeadOfTrade, 3)!;
    double before = p.Gold;
    Check(eng.AppointCommander(CommanderRole.HeadOfTrade, 3) is null, "a better candidate replaces the holder");
    Check(p.Commanders.Count(c => c.Role == CommanderRole.HeadOfTrade) == 1 && CommandStaffService.Holder(p, CommanderRole.HeadOfTrade)!.Name == renowned.Name, "never two holders of one post");
    Check(Math.Abs(before - p.Gold - renowned.HireCost) < 1e-9, "replacing costs the new appointee's fee (nothing is refunded for the old one)");
    Check(!p.Commanders.Any(c => c.Name == capable.Name), "the old holder is gone");
    p.Gold = 1;
    double poor = p.Gold;
    Check(eng.AppointCommander(CommanderRole.HeadOfConstruction, 3) is { } nope && nope.Contains("costs") && p.Gold == poor && CommandStaffService.Holder(p, CommanderRole.HeadOfConstruction) is null,
        "without the gold, nothing happens");
    Check(eng.AppointCommander(CommanderRole.HeadOfConstruction, 0) is not null && eng.AppointCommander(CommanderRole.HeadOfConstruction, 4) is not null, "only the three candidates can be chosen");
    p.Gold = 1000;
    eng.DismissCommander(CommanderRole.HeadOfTrade);
    Check(CommandStaffService.Holder(p, CommanderRole.HeadOfTrade) is null && p.Gold == 1000, "a dismissed holder leaves the post vacant (and nothing is refunded)");
    Check(CommandStaffService.Strength(p, CommanderRole.HeadOfTrade) == 0 && CommandStaffService.ImportPriceMult(p) == 1.0, "a vacant post has no effect");

    // Countries keep their own appointments.
    Check(CommandStaffService.Appoint(s, persia, CommanderRole.LandCommander, 2) is null && CommandStaffService.Holder(persia, CommanderRole.LandCommander) is { Skill: 2 }, "another country appoints its own");
    Check(CommandStaffService.Holder(p, CommanderRole.LandCommander) is null && CommandStaffService.Holder(france, CommanderRole.LandCommander) is null, "and no one else's staff changes");
    var ghost = Ai(eng, "spain"); ghost.IsEliminated = true;
    Check(CommandStaffService.Appoint(s, ghost, CommanderRole.LandCommander, 1) is { } gone && gone.Contains("no longer exists"), "an annexed country appoints no one");
}

using (var eng = NewDipEngine(191))
{
    // The effects, one by one, each read by the rule it changes.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 1_000_000; p.Wood = 100_000; p.Iron = 100_000; p.Stone = 100_000;
    p.Units = UnitCatalog.SeedArmy(6000); p.Warships = 20;
    double Staff(CommanderRole role, int tier) { Check(eng.AppointCommander(role, tier) is null, $"(appointed {role} tier {tier})"); return CommandStaffService.Strength(tier); }

    // -- Land commander: upkeep, recruit cost, battle strength.
    double landUpkeep = p.Units.Sum(u => u.Count * UnitCatalog.Get(u.Type).UpkeepPerDay);
    double Accrual() { p.UpkeepAccrued = 0; eng.AdvanceOneDay(); return p.UpkeepAccrued - CommandStaffService.DailyWages(p); }
    double navalUpkeep = p.Warships * WarshipSpec.UpkeepPerDay;
    double none = Accrual();
    Check(Math.Abs(none - (landUpkeep + navalUpkeep)) < 0.05, "(the daily bill without staff is the plain upkeep)");
    Staff(CommanderRole.LandCommander, 1);
    double withLand = Accrual();
    Check(Math.Abs(withLand - (landUpkeep * Balance.LandCommanderUpkeepMult + navalUpkeep)) < 0.05, "a capable land commander cuts land upkeep by 15% and leaves the navy's alone");
    Staff(CommanderRole.LandCommander, 3);
    double withRenowned = Accrual();
    Check(withRenowned < withLand - 0.01 && Math.Abs(withRenowned - (landUpkeep * (1 - 0.15 * Balance.StaffTier3Strength) + navalUpkeep)) < 0.05, "a renowned one cuts it by more (15% x the tier's strength)");
    double g0 = p.Gold; eng.Recruit(UnitType.Musketeer, 100);
    double paid = g0 - p.Gold;
    Check(Math.Abs(paid - UnitCatalog.Get(UnitType.Musketeer).GoldCost * 100 * (1 - 0.10 * Balance.StaffTier3Strength)) < 1e-6, "recruiting costs less by the same strength (10% x 1.7)");
    Check(Math.Abs(p.BattleStrengthMult - (1 + 0.05 * Balance.StaffTier3Strength) * ResearchService.BattleStrengthMult(p)) < 1e-9, "its armies fight harder (+5% x 1.7, on the existing battle-strength rule)");
    eng.DismissCommander(CommanderRole.LandCommander);

    // A borderline battle that only the land commander's strength wins (the existing combat model, unchanged).
    var attacker = Ai(eng, "france"); var defender = Ai(eng, "spain");
    defender.Units = UnitCatalog.SeedArmy(3000);
    double defence = ArmyHelper.ArmyPower(defender.Units) * Balance.GarrisonFraction * Balance.HomeAdvantageMult;
    int need = 1; while (ArmyHelper.ArmyPower(UnitCatalog.SeedArmy(need)) * 1.0 <= defence) need += 10;   // the smallest force that wins as it is
    int borderline = need - 40;
    var forceA = UnitCatalog.SeedArmy(borderline);
    double plain = ArmyHelper.ArmyPower(forceA);
    Check(plain < defence && plain * 1.05 > defence, "(a force that loses as it is but would win with +5%)");
    bool Fight() { defender.Units = UnitCatalog.SeedArmy(3000); return Warfare.ResolveBattle(UnitCatalog.SeedArmy(borderline), attacker, defender, attacker.BattleStrengthMult, new Random(1)).AttackerWon; }   // (a fresh garrison each time: a battle costs the defender men too)
    Check(!Fight(), "(without a commander it loses)");
    CommandStaffService.Appoint(s, attacker, CommanderRole.LandCommander, 1);
    Check(Fight(), "with a land commander the same army wins: the bonus acts on the real combat model");

    // -- Fleet commander: naval upkeep and warship cost; no fighting bonus (stated on the screen).
    Staff(CommanderRole.FleetCommander, 1);
    double withFleet = Accrual();
    Check(Math.Abs(withFleet - (landUpkeep + navalUpkeep * Balance.FleetCommanderUpkeepMult)) < 0.05, "a fleet commander cuts naval upkeep by 15% and leaves the army's alone");
    g0 = p.Gold; eng.RecruitWarships(5);
    Check(Math.Abs((g0 - p.Gold) - WarshipSpec.GoldCost * 5 * Balance.FleetCommanderRecruitMult) < 1e-6, "and warships cost 10% less");
    Check(CommandStaffService.Limitation(CommanderRole.FleetCommander) is { } note && note.Contains("Naval battles") && CommandStaffService.Limitation(CommanderRole.LandCommander) is null, "the screen says plainly that naval combat is not modelled");

    // -- Commander-in-Chief: a lighter bill for every force, and less war attrition (no tax: that is the tribute collector's).
    Staff(CommanderRole.CommanderInChief, 1);
    double withBoth = Accrual();
    Check(Math.Abs(withBoth - (landUpkeep * Balance.CinCUpkeepMult + navalUpkeep * Balance.FleetCommanderUpkeepMult * Balance.CinCUpkeepMult)) < 0.05,
        "the commander-in-chief's 5% applies to every force and stacks with the fleet commander's, rather than repeating it");
    double taxBefore = EngineTax(p);
    double EngineTax(Nation n) => GameEngine.EstimateDailyIncome(n);
    Check(CommandStaffService.TaxMult(p) == 1.0, "(and it gives no tax bonus any more)");
    var west = NewDipEngine(191); var wp = west.State.PlayerNation; var wpersia = Ai(west, "persia");
    foreach (var e in new[] { (eng, p, persia), (west, wp, wpersia) })
    {
        e.Item2.Units = UnitCatalog.SeedArmy(10_000); e.Item3.Units = UnitCatalog.SeedArmy(10_000);
        e.Item3.AtWarWithPlayer = true; e.Item3.Gold = 0;   // (a war on: both armies bleed a little every day)
    }
    CommandStaffService.Dismiss(west.State, wp, CommanderRole.CommanderInChief);
    CommandStaffService.Dismiss(eng.State, p, CommanderRole.FleetCommander);
    for (int i = 0; i < 20; i++) { p.Gold = 1e9; wp.Gold = 1e9; persia.Units = UnitCatalog.SeedArmy(10_000); wpersia.Units = UnitCatalog.SeedArmy(10_000); eng.AdvanceOneDay(); west.AdvanceOneDay(); }
    Check(p.Soldiers > wp.Soldiers, "with a commander-in-chief fewer soldiers are lost to attrition in a war");
    west.Dispose();
}

using (var eng = NewDipEngine(192))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    p.Gold = 1_000_000;
    Embassy(eng, france, persia);

    // -- Chief tribute collector: tax revenue, and tribute from other countries.
    double taxPlain = GameEngine.EstimateDailyIncome(p);
    eng.AppointCommander(CommanderRole.ChiefTributeCollector, 1);
    double taxWith = GameEngine.EstimateDailyIncome(p);
    double revenue = TaxationService.TotalRevenue(p.Workforce, p.TaxRates) * p.TaxMult;
    Check(Math.Abs((taxWith - taxPlain) - (revenue * (Balance.TributeCollectorTaxMult - 1))) <= 1.0, "a tribute collector raises tax revenue by 5% (the income the daily tick pays)");
    p.Gold = 0; p.UpkeepAccrued = 0; eng.AdvanceOneDay();
    double dayWith = p.Gold;
    eng.DismissCommander(CommanderRole.ChiefTributeCollector);
    p.Gold = 0; eng.AdvanceOneDay();
    Check(dayWith > p.Gold, "and the daily tick really pays more with one");
    p.Units = UnitCatalog.SeedArmy(20_000); persia.Units = UnitCatalog.SeedArmy(1_000); persia.Gold = 100_000;
    p.Gold = 1_000_000; double g0 = p.Gold;
    Check(eng.DemandTribute(persia.Id) is null, "(tribute demanded)");
    double plainTribute = p.Gold - g0;
    persia.Gold = 100_000; Embassy(eng, persia);
    eng.AppointCommander(CommanderRole.ChiefTributeCollector, 1);
    p.Gold = 1_000_000; g0 = p.Gold; persia.RelationToPlayer = 0;
    Check(eng.DemandTribute(persia.Id) is null, "(demanded again, with a collector)");
    Check(Math.Abs((p.Gold - g0) - plainTribute * Balance.TributeCollectorTributeMult) < 0.01, "a tribute collector gathers 25% more tribute");

    // -- Head of trade: what you buy costs less, what you sell earns more, trade pacts pay more.
    Embassy(eng, france);
    persia.Gold = 100_000; france.Gold = 100_000; france.Wood = 10_000;
    double plainPrice = eng.BuyPricePer1000(france.Id, "wood");
    double plainPact = p.TradeIncomeMult;
    eng.AppointCommander(CommanderRole.HeadOfTrade, 1);
    Check(Math.Abs(eng.BuyPricePer1000(france.Id, "wood") - plainPrice * Balance.TradeHeadImportMult) < 1e-9, "a head of trade lowers the price you pay by 5%");
    Check(Math.Abs(p.TradeIncomeMult - plainPact * Balance.TradeHeadPactIncomeMult) < 1e-9, "and raises trade-pact income by 10%");
    var sell = new TradeContract { BuyerId = france.Id, SellerId = p.Id, ProductId = "wood", Quantity = 100, TotalValue = 1000, IsPlayerBuyer = false,
        CreatedDate = s.CurrentDate, DeliveryDate = s.CurrentDate, Status = TradeStatus.InTransit };
    p.Gold = 0; eng.AdvanceOneDay();   // (a day without the sale: what the crown earns anyway)
    double ordinaryDay = p.Gold;
    s.TradeContracts.Add(sell);
    p.Gold = 0; eng.AdvanceOneDay();
    Check(sell.Status == TradeStatus.Delivered && Math.Abs((p.Gold - ordinaryDay) - 1000 * Balance.TradeHeadExportMult) < 5, "and a sale earns 5% more when it is delivered");

    // -- Head of construction: faster building, never under a day.
    p.Gold = 1e9; p.Wood = 1e6; p.Iron = 1e6; p.Stone = 1e6;
    eng.DismissCommander(CommanderRole.HeadOfConstruction);
    eng.StartConstruction(BuildingType.Mine);
    int plainDays = p.ConstructionQueue.Last().TotalDays;
    p.ConstructionQueue.Clear();
    eng.AppointCommander(CommanderRole.HeadOfConstruction, 1);
    eng.StartConstruction(BuildingType.Mine);
    int fasterDays = p.ConstructionQueue.Last().TotalDays;
    Check(fasterDays < plainDays && fasterDays == Math.Max(1, (int)Math.Round(plainDays * Balance.ConstructionHeadTimeMult, MidpointRounding.AwayFromZero)), "a head of construction shortens a building's time by 15% (to the nearest day)");
    var mill = ProductionCatalog.All.First(m => m.BuildDays >= 4);
    p.ConstructionQueue.Clear();
    Check(eng.BuildProduction(mill.Id, 2) is null && p.ConstructionQueue.All(x => x.TotalDays == eng.ProductionBuildDays(mill)) && eng.ProductionBuildDays(mill) < mill.BuildDays,
        "production buildings are built sooner too, and the screen's number is the one used");
    eng.DismissCommander(CommanderRole.HeadOfConstruction);
    Check(eng.ProductionBuildDays(mill) == mill.BuildDays, "(vacant: the building's own time)");
    var quick = mill with { BuildDays = 1 };
    eng.AppointCommander(CommanderRole.HeadOfConstruction, 3);
    Check(eng.ProductionBuildDays(quick) == 1, "a one-day build stays at one day");
    p.ConstructionQueue.Clear();
}

using (var eng = NewDipEngine(193))
{
    // The same rules for AI countries: its own staff, its own effects, and it fills posts by itself.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    persia.Units = UnitCatalog.SeedArmy(8000); spain.Units = UnitCatalog.SeedArmy(8000);
    persia.Warships = 0; spain.Warships = 0; persia.Gold = 50_000; spain.Gold = 50_000;
    persia.EnsureTaxationInitialized(); spain.EnsureTaxationInitialized();
    persia.UpkeepAccrued = 0; spain.UpkeepAccrued = 0;
    CommandStaffService.Appoint(s, persia, CommanderRole.LandCommander, 1);
    persia.Gold = 50_000;
    eng.AdvanceOneDay();
    double landBase = persia.Units.Sum(u => u.Count * UnitCatalog.Get(u.Type).UpkeepPerDay);
    double spainBase = spain.Units.Sum(u => u.Count * UnitCatalog.Get(u.Type).UpkeepPerDay);
    Check(Math.Abs((spain.UpkeepAccrued / spainBase) - 1.0) < 0.02 && persia.UpkeepAccrued < landBase * 0.9, "an AI country's upkeep falls with its own land commander, and its neighbour's does not");
    Check(persia.BattleStrengthMult > spain.BattleStrengthMult, "its armies fight harder, by the same rule as the player's");
    double aiTaxPlain = TaxationService.TotalRevenue(spain.Workforce, spain.TaxRates);
    CommandStaffService.Appoint(s, spain, CommanderRole.ChiefTributeCollector, 2);
    Check(CommandStaffService.TaxMult(spain) > 1.05 && CommandStaffService.TaxMult(persia) == 1.0, "tax effects likewise follow the country's own collector");
}

using (var eng = NewDipEngine(194))
{
    // AI countries fill their own staff over time, only for posts they can use, one holder per post, paid for from their own treasury.
    var s = eng.State;
    for (int i = 0; i < 500; i++) { s.PlayerNation.Gold = 1e9; eng.AdvanceOneDay(); }
    var staffed = s.OtherNations.Where(n => !n.IsEliminated && n.Commanders.Count > 0).ToList();
    Check(staffed.Count >= 10, $"AI countries appoint staff by themselves ({staffed.Count} of {s.OtherNations.Count} have some)");
    Check(staffed.All(n => n.Commanders.All(c => c.Role is CommanderRole.LandCommander or CommanderRole.CommanderInChief or CommanderRole.FleetCommander or CommanderRole.ChiefTributeCollector)),
        "...only for posts they have something to run (no trade or construction of their own)");
    Check(s.OtherNations.All(n => n.Commanders.Select(c => c.Role).Distinct().Count() == n.Commanders.Count && n.Commanders.Select(c => c.Name).Distinct().Count() == n.Commanders.Count),
        "...never two holders of a post, never one person in two posts");
    Check(staffed.All(n => n.Commanders.All(c => c.Skill == 1 && CommandStaffService.Candidates(n, c.Role).Any(k => k.Name == c.Name && k.Tier == 1))), "...always the capable candidate from their own shortlist");
    Check(s.OtherNations.Where(n => !n.IsEliminated).All(n => n.Gold >= 0), "(and nobody went broke doing it)");
}

using (var eng = NewDipEngine(195))
{
    // Appointments survive save / load, for the player and the AI; an old save's commanders load as capable; annexation clears the loser's staff.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    p.Gold = 1e6;
    eng.AppointCommander(CommanderRole.HeadOfConstruction, 2);
    eng.AppointCommander(CommanderRole.FleetCommander, 3);
    CommandStaffService.Appoint(s, persia, CommanderRole.CommanderInChief, 2);
    var mine = p.Commanders.Select(c => (c.Role, c.Name, c.Skill, c.DailyWage, c.AppointedDate)).OrderBy(x => x.Role).ToList();
    var theirs = persia.Commanders.Select(c => (c.Role, c.Name, c.Skill, c.DailyWage, c.AppointedDate)).ToList();
    await eng.SaveAsync();
    p.Commanders.Clear(); persia.Commanders.Clear();
    await eng.LoadAsync();
    var p2 = eng.State.PlayerNation; var persia2 = Ai(eng, "persia");
    Check(p2.Commanders.Select(c => (c.Role, c.Name, c.Skill, c.DailyWage, c.AppointedDate)).OrderBy(x => x.Role).SequenceEqual(mine), "the player's appointments survive save / load (post, name, skill, wage, date)");
    Check(persia2.Commanders.Select(c => (c.Role, c.Name, c.Skill, c.DailyWage, c.AppointedDate)).SequenceEqual(theirs), "and so do an AI country's");
    Check(CommandStaffService.ConstructionTimeMult(p2) < 1.0 && CommandStaffService.Strength(persia2, CommanderRole.CommanderInChief) > 1.0, "and their effects work again after loading");
    var old = System.Text.Json.JsonSerializer.Deserialize<Commander>("{\"Id\":\"x\",\"Name\":\"Old Pasha\",\"Role\":1,\"DailyWage\":0.02}")!;
    Check(old.Skill == 1 && old.AppointedDate is null && old.Role == CommanderRole.LandCommander, "a commander from an older save loads as a capable land commander");

    Warfare.AnnexNation(eng.State, p2, persia2);
    Check(persia2.Commanders.Count == 0, "an annexed country's staff is gone with it");
}

Console.WriteLine("== 71. Annexed States: each territory managed separately, by its master only, by the existing rules ==");
using (var eng = NewDipEngine(200))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var france = Ai(eng, "france"); var kaz = Ai(eng, "kazakh");
    Warfare.AnnexNation(s, p, persia); eng.AdvanceOneDay(); Warfare.AnnexNation(s, p, spain);   // (a day apart: the list keeps the order of annexation)
    Warfare.AnnexNation(s, france, kaz);   // an AI master's territory

    // Only the player's own territories, in the order they were annexed.
    Check(eng.AnnexedTerritories.Select(t => t.Id).SequenceEqual(new[] { persia.Id, spain.Id }), "the player's territories are exactly the countries their country annexed");
    Check(eng.Territory(persia.Id) is not null && eng.Territory(kaz.Id) is null && eng.Territory(france.Id) is null && eng.Territory("nowhere") is null,
        "an AI master's territory, an independent country and an unknown id are not the player's to manage");
    Check(eng.TerritoryAuthority(kaz.Id)!.Contains("belongs to") && eng.TerritoryAuthority(france.Id)!.Contains("independent") && eng.TerritoryAuthority(persia.Id) is null, "and the refusal says why");
    Check(eng.SetTerritoryTaxRates(kaz.Id, new TaxRates()) is not null && eng.BuildProductionInTerritory(kaz.Id, "farm", 1) is not null
          && eng.BuildBasicInTerritory(kaz.Id, BuildingType.Farm) is not null && eng.ShipTerritoryStock(kaz.Id, "wheat", 1) is not null, "every operation on another master's territory is refused");

    // A territory is not a country: nothing to declare war on, give to, tax, or deal with.
    Check(eng.DeclareWar(persia.Id) is not null && !persia.AtWarWithPlayer && eng.SendGift(persia.Id) is not null && eng.DemandTribute(persia.Id) is not null
          && eng.SignTradePact(persia.Id) is not null && eng.EstablishNetwork(persia.Id) is not null, "an annexed territory cannot be made war on, given gifts, taxed as tribute or dealt with: it is not a country");
    Check(!eng.ConfirmAttack(persia.Id, 1000).Ok && !eng.ProposePeace(persia.Id, new[] { PeaceTerm.Peace() }).Ok, "no attack, no peace talks");
    Check(!eng.CheckDiplomaticAction(DiplomaticActionCatalog.Gift, persia.Id).Available, "and none of the diplomatic actions are open");

    // The report: what the list and the operations screen show.
    persia.ShortagePct["Salt"] = 0;
    var rep = eng.ReportTerritory(persia.Id)!;
    Check(rep.Id == persia.Id && rep.Name == persia.Name && rep.OwnerId == p.Id && rep.AnnexedDate == persia.AnnexedDate && rep.AnnexedDate is not null && rep.Population == persia.Population, "the report names the territory, its master, the date and its people");
    Check(Math.Abs(rep.TaxRevenuePerDay - TaxationService.TotalRevenue(persia.Workforce, persia.TaxRates) * CommandStaffService.TaxMult(p) * p.TaxMult) < 1e-9 && rep.TaxRevenuePerDay > 0, "the tax figure is the one the daily tick credits to the master");
    Check(rep.Farms == persia.Farms && rep.Mines == persia.Mines && rep.Mills == persia.ProductionBuildings.Values.Sum() && rep.Facilities == rep.Farms + rep.Mines + rep.Sawmills + rep.Workshops + rep.Mills && rep.Facilities > 0, "it counts every facility");
    Check(rep.Items.Count == ConsumptionCatalog.All.Count && rep.Items.All(i => i.NeedPerDay >= 0), "every item the people use is listed with its need, stock and output");
    Check(!rep.Absorbed && rep.Status.StartsWith("Under ") && rep.Status.Contains(p.Name) && rep.ShortItems.Count == 0, "a territory with no shortage reads as under its master's rule, with how its people take the taxes");
    persia.ShortagePct["Wheat"] = 12;
    Check(eng.ReportTerritory(persia.Id)!.Status.StartsWith("Shortages") && eng.ReportTerritory(persia.Id)!.ShortItems.Contains("Wheat"), "a shortage shows in its status and its list");
    persia.ShortagePct.Remove("Wheat");

    // Realm totals count everything once.
    var reports = eng.TerritoryReports();
    Check(Math.Abs(eng.RealmDailyIncome() - (GameEngine.EstimateDailyIncome(p) + (long)reports.Sum(r => r.TotalIncomePerDay))) <= 1, "the realm's income is the country's plus its territories', each once");
    Check(StatisticsService.IncomeBreakdown(s, p).Single(x => x.Name == "Annexed states").Amount == reports.Sum(r => r.TotalIncomePerDay), "and the income statement lists the territories' share");
    Check(eng.RealmPopulation == p.Population + persia.Population + spain.Population, "the realm's people are the country's plus its territories', each once");
}

using (var eng = NewDipEngine(201))
{
    // Tax: the preview is the existing tax rules worked out first; applying it changes the master's income by exactly that, and only that territory's.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    Warfare.AnnexNation(s, p, persia); Warfare.AnnexNation(s, p, spain);
    using var control = NewDipEngine(201);
    Warfare.AnnexNation(control.State, control.State.PlayerNation, Ai(control, "persia")); Warfare.AnnexNation(control.State, control.State.PlayerNation, Ai(control, "spain"));
    var pc = control.State.PlayerNation;

    var current = persia.TaxRates;
    var raised = new TaxRates { Peasants = 100, Craftsmen = 100, MilitaryPersonnel = 100, Merchants = 100, Spies = 100, Saboteurs = 100 };
    var preview = eng.PreviewTerritoryTaxes(persia.Id, raised)!;
    Check(preview.RevenueChange > 5 && preview.ApprovalTarget < eng.ReportTerritory(persia.Id)!.TaxApproval + 0.0001, "the preview shows more revenue and a lower approval before anything is changed");
    Check(persia.TaxRates.Peasants == current.Peasants && preview.ExtraDeathPct >= 0, "(previewing changes nothing)");
    var bad = new TaxRates { Peasants = 101 };
    Check(eng.SetTerritoryTaxRates(persia.Id, bad) is not null && persia.TaxRates.Peasants == current.Peasants, "rates above 100 are refused and nothing changes");
    Check(eng.SetTerritoryTaxRates(persia.Id, new TaxRates { Peasants = -1 }) is not null, "...and so are negative ones");
    Check(eng.SetTerritoryTaxRates(persia.Id, raised) is null && persia.TaxRates.Peasants == 100 && persia.TaxRates.Merchants == 100, "the rates are applied to that territory");
    Check(p.TaxRates.Peasants == pc.TaxRates.Peasants && spain.TaxRates.Peasants == Ai(control, "spain").TaxRates.Peasants, "the national rates and the other territory's are untouched: each is managed separately");

    double before = p.Gold, beforeC = pc.Gold;
    eng.AdvanceOneDay(); control.AdvanceOneDay();
    double delta = (p.Gold - before) - (pc.Gold - beforeC);
    Check(Math.Abs(delta - preview.RevenueChange) < 1.0, $"the master's income rose by what the preview said ({delta:N1} vs {preview.RevenueChange:N1}) and was credited once");
    Check(persia.Gold == 0 && spain.Gold == 0, "(the territories hold none of it)");

    // Effects on the people: above the safe threshold, a shortage kills more.
    persia.ShortagePct["Wheat"] = 20;
    var harsh = new TaxRates { Peasants = 90, Craftsmen = 90, MilitaryPersonnel = 90, Merchants = 90, Spies = 90, Saboteurs = 90 };
    var pv = eng.PreviewTerritoryTaxes(persia.Id, harsh)!;
    Check(pv.ShortageNow && pv.SafeThreshold == Balance.SafeTaxShortage && pv.ExtraDeathPct > 0 && pv.ExtraDeathsPerDay > 0, "with a shortage, taxes above the safe threshold are shown to raise deaths, by the existing rule");
    Check(Math.Abs(pv.ExtraDeathPct - ConsumptionService.TaxDeathIncreasePct(harsh, Balance.SafeTaxShortage)) < 1e-9, "...by exactly the existing formula");
}

using (var eng = NewDipEngine(202))
{
    // Building: gold from the master's treasury, materials from the territory's own stockpile; the finished mill is the territory's.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    Warfare.AnnexNation(s, p, persia); Warfare.AnnexNation(s, p, spain);
    p.Gold = 1_000_000; persia.Wood = 1e7; persia.Stone = 1e7; persia.Iron = 1e7;
    var mill = ProductionCatalog.All.Where(m => m.WoodCost > 0 && m.BuildDays >= 4).OrderBy(m => m.GoldCost).First();

    var quote = eng.QuoteTerritoryProduction(persia.Id, mill.Id, 3)!;
    Check(quote.Block is null && Math.Abs(quote.Gold - mill.GoldCost * 3) < 1e-9 && Math.Abs(quote.Wood - mill.WoodCost * 3) < 1e-9 && quote.Days == mill.BuildDays, "the quote states the cost and the days before anything is spent");
    double pGold = p.Gold, pWood = p.Wood, tWood = persia.Wood, tStone = persia.Stone, tIron = persia.Iron; int ownMills = p.ProductionBuildings.Values.Sum(), spainMills = spain.ProductionBuildings.Values.Sum(), tMills = persia.ProductionBuildings.Values.Sum();
    Check(eng.BuildProductionInTerritory(persia.Id, mill.Id, 3) is null, "the master starts three mills in the territory");
    Check(Math.Abs((pGold - p.Gold) - quote.Gold) < 1e-9 && Math.Abs((tWood - persia.Wood) - quote.Wood) < 1e-9 && Math.Abs((tStone - persia.Stone) - quote.Stone) < 1e-9 && Math.Abs((tIron - persia.Iron) - quote.Iron) < 1e-9 && p.Wood == pWood,
        "gold left the master's treasury and the materials left the territory's stockpile — exactly the quote, and the master's own stock is untouched");
    Check(persia.ConstructionQueue.Count == 3 && persia.ConstructionQueue.All(c => c.TotalDays == quote.Days) && p.ConstructionQueue.Count == 0 && spain.ConstructionQueue.Count == 0, "the builds are queued in that territory only");
    for (int i = 0; i < quote.Days; i++) { p.Gold = Math.Max(p.Gold, 1_000_000); eng.AdvanceOneDay(); }
    Check(persia.ProductionBuildings.Values.Sum() == tMills + 3 && p.ProductionBuildings.Values.Sum() == ownMills && spain.ProductionBuildings.Values.Sum() == spainMills && persia.ConstructionQueue.Count == 0,
        "when they finish, the mills are the territory's: not the master's, not the other territory's, and each counted once");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.Production && m.Summary.StartsWith(persia.Name + ":") && m.Summary.Contains("completed")), "the master is told which territory finished building");

    // Refusals: no materials, no gold, nothing is spent.
    persia.Wood = 0; double g = p.Gold;
    var short1 = eng.QuoteTerritoryProduction(persia.Id, mill.Id, 1)!;
    Check(short1.Block!.Contains("wood") && short1.Block.Contains(persia.Name) && eng.BuildProductionInTerritory(persia.Id, mill.Id, 1) is not null && p.Gold == g && persia.ConstructionQueue.Count == 0,
        "without the territory's own wood the build is refused, naming what is missing, and nothing is spent");
    persia.Wood = 1e7; p.Gold = 0;
    Check(eng.QuoteTerritoryProduction(persia.Id, mill.Id, 1)!.Block!.Contains("treasury") && eng.BuildProductionInTerritory(persia.Id, mill.Id, 1) is not null, "without gold in the master's treasury it is refused too");
    Check(eng.BuildProductionInTerritory(persia.Id, mill.Id, 0) is not null && eng.BuildProductionInTerritory(persia.Id, "unobtainium", 1) is not null, "zero or unknown buildings are refused");
    p.Gold = 1_000_000;
    var max = eng.MaxTerritoryProduction(persia.Id, mill.Id)!.Value;
    Check(max.Max > 0 && eng.QuoteTerritoryProduction(persia.Id, mill.Id, max.Max)!.Block is null && eng.QuoteTerritoryProduction(persia.Id, mill.Id, max.Max + 1)!.Block is not null, "the most that can be built is the most the quote allows");

    // The head of construction (the master's) shortens the time; the farm, mine, sawmill and workshop go up by the same kind of rule.
    p.ConstructionQueue.Clear(); persia.ConstructionQueue.Clear();
    int plainDays = eng.QuoteTerritoryBuilding(persia.Id, BuildingType.Mine)!.Days;
    eng.AppointCommander(CommanderRole.HeadOfConstruction, 3);
    int quicker = eng.QuoteTerritoryBuilding(persia.Id, BuildingType.Mine)!.Days;
    Check(quicker < plainDays, "the master's head of construction shortens building in its territories too");
    double g2 = p.Gold; int farms = persia.Farms, ownFarms = p.Farms;
    var farmQuote = eng.QuoteTerritoryBuilding(persia.Id, BuildingType.Farm)!;
    Check(eng.BuildBasicInTerritory(persia.Id, BuildingType.Farm) is null && Math.Abs((g2 - p.Gold) - farmQuote.Gold) < 1e-9 && persia.ConstructionQueue.Count == 1, "a farm is queued for the quoted gold");
    for (int i = 0; i < farmQuote.Days; i++) { p.Gold = Math.Max(p.Gold, 1_000_000); eng.AdvanceOneDay(); }
    Check(persia.Farms == farms + 1 && p.Farms == ownFarms, "and when it is done it is the territory's farm");
}

using (var eng = NewDipEngine(203))
{
    // Stock: sent to the national stockpile exactly once; the territory's people lose it.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    Warfare.AnnexNation(s, p, persia);
    persia.GoodsInventory["Wheat"] = 500; persia.Iron = 300;
    double ownWheat = p.GetProduct("Wheat"), ownIron = p.Iron;
    Check(AnnexedStateService.Shippable(persia).Any(x => x.Id == "wheat" && x.Quantity == 500), "what the territory holds can be sent");
    Check(eng.ShipTerritoryStock(persia.Id, "wheat", 200) is null && persia.GetProduct("Wheat") == 300 && p.GetProduct("Wheat") == ownWheat + 200, "200 wheat leave the territory and arrive in the national stockpile, once");
    Check(eng.ShipTerritoryStock(persia.Id, "iron", 300) is null && persia.Iron == 0 && p.Iron == ownIron + 300, "so does iron");
    Check(eng.ShipTerritoryStock(persia.Id, "wheat", 301) is { } over && over.Contains("holds only") && persia.GetProduct("Wheat") == 300, "more than it holds is refused");
    Check(eng.ShipTerritoryStock(persia.Id, "wheat", 0) is not null && eng.ShipTerritoryStock(persia.Id, "wheat", -5) is not null && eng.ShipTerritoryStock(persia.Id, "unobtainium", 1) is not null, "nothing for zero, negative or unknown goods");
    Check(p.GetProduct("Wheat") == ownWheat + 200, "(and none of the refused calls moved anything)");
}

using (var eng = NewDipEngine(204))
{
    // The simulation: births, shortages and the master's income continue; an old save's emptied territory is harmless; an AI master governs its own.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var kaz = Ai(eng, "kazakh"); var france = Ai(eng, "france");
    Warfare.AnnexNation(s, p, persia);
    long pop = persia.Population; double income = 0;
    for (int i = 0; i < 20; i++) { double g = p.Gold; eng.AdvanceOneDay(); income += p.Gold - g; p.Gold = Math.Max(p.Gold, 1_000_000); }
    Check(persia.Population != pop && persia.Gold == 0, "the territory's people go on being born (or dying of shortages) and its treasury stays empty");

    // A territory left empty by an older version (it zeroed everything at annexation) shows as absorbed and costs nothing.
    var ghost = Ai(eng, "japan"); Warfare.AnnexNation(s, p, ghost);
    ghost.Population = 0; ghost.Farms = ghost.Mines = ghost.Sawmills = ghost.Workshops = 0; ghost.ProductionBuildings.Clear(); ghost.GoodsInventory.Clear(); ghost.Wood = ghost.Stone = ghost.Iron = ghost.Copper = ghost.Lead = ghost.Goods = 0;
    Check(eng.ReportTerritory(ghost.Id)!.Absorbed && eng.ReportTerritory(ghost.Id)!.Status.Contains("Absorbed"), "a territory emptied by an older version reads as absorbed");
    for (int i = 0; i < 5; i++) eng.AdvanceOneDay();
    Check(ghost.Population == 0 && ghost.Gold == 0, "...and the simulation leaves it harmlessly empty");

    // An AI master governs its territory through the simulation: it keeps its people fed, and nothing leaks to the territory.
    Warfare.AnnexNation(s, france, kaz);
    for (int i = 0; i < 500; i++) { p.Gold = Math.Max(p.Gold, 1_000_000); eng.AdvanceOneDay(); }
    Check(kaz.IsEliminated && kaz.Gold == 0 && kaz.AnnexedById == france.Id && kaz.ShortagePct.Values.All(v => v == 0), "an AI master's territory is fed by its master, holds no gold of its own and stays under it");
    Check(kaz.ProductionBuildings.Count > 0 && ConsumptionCatalog.All.All(spec => ProductionCatalog.All.All(m => m.Produces != spec.Item) ||
          kaz.GetProductionBuilding(ProductionCatalog.All.First(m => m.Produces == spec.Item).Id) > 0), "the master topped up the mills for every item the territory's people use");
}

Console.WriteLine("== 72. Annexed State Operations, end to end: annex -> operate -> advance -> save -> reload -> advance ==");
using (var eng = NewDipEngine(210))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Gold = 1e8;
    // A real victory and the player's choice to annex.
    persia.AtWarWithPlayer = true; persia.RelationToPlayer = -100;
    VictoryService.Begin(s, p, persia);
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Annex).Ok && persia.IsEliminated && persia.AnnexedById == p.Id, "(the player annexes Persia after a victory)");
    Check(eng.AnnexedTerritories.Single().Id == persia.Id, "it appears among the player's annexed states");

    // Operate it: taxes, building, stock.
    persia.Wood = 1e7; persia.Stone = 1e7; persia.Iron = 1e7; persia.GoodsInventory["Wheat"] = 1000;
    var mill = ProductionCatalog.All.Where(m => m.WoodCost > 0 && m.BuildDays >= 4).OrderBy(m => m.GoldCost).First();
    var rates = new TaxRates { Peasants = 55, Craftsmen = 35, MilitaryPersonnel = 5, Merchants = 45, Spies = 12, Saboteurs = 12 };
    Check(eng.SetTerritoryTaxRates(persia.Id, rates) is null, "the player changes its tax rates");
    Check(eng.BuildProductionInTerritory(persia.Id, mill.Id, 2) is null && eng.BuildBasicInTerritory(persia.Id, BuildingType.Farm) is null, "...and starts two mills and a farm in it");
    Check(eng.ShipTerritoryStock(persia.Id, "wheat", 400) is null, "...and sends some wheat to the national stockpile");
    int farms0 = persia.Farms, mills0 = persia.ProductionBuildings.Values.Sum();
    double wheatHome = p.GetProduct("Wheat");

    for (int i = 0; i < 3; i++) eng.AdvanceOneDay();
    long pop3 = persia.Population; var queue3 = persia.ConstructionQueue.Select(c => (c.ProductionBuildingId, c.Building, c.DaysLeft, c.TotalDays)).ToList();
    double wheat3 = persia.GetProduct("Wheat"); double homeWheat3 = p.GetProduct("Wheat");
    Check(queue3.Count == 3 && queue3.All(q => q.DaysLeft < q.TotalDays), "after three days the builds are under way in the territory");

    await eng.SaveAsync();
    persia.TaxRates = new TaxRates(); persia.ConstructionQueue.Clear(); persia.Population = 1; persia.AnnexedById = null; persia.GoodsInventory["Wheat"] = 0;
    await eng.LoadAsync();
    var p2 = eng.State.PlayerNation; var persia2 = Ai(eng, "persia");
    Check(persia2.IsEliminated && persia2.AnnexedById == p2.Id && eng.AnnexedTerritories.Single().Id == persia2.Id, "after reloading it is still the player's annexed state");
    Check(persia2.TaxRates.Peasants == 55 && persia2.TaxRates.Craftsmen == 35 && persia2.TaxRates.Merchants == 45 && persia2.TaxRates.Spies == 12, "its tax settings survive");
    Check(persia2.ConstructionQueue.Select(c => (c.ProductionBuildingId, c.Building, c.DaysLeft, c.TotalDays)).SequenceEqual(queue3), "so do its builds in progress, day for day");
    Check(persia2.Population == pop3 && persia2.GetProduct("Wheat") == wheat3, "and its people and stocks");
    Check(p2.GetProduct("Wheat") == homeWheat3 && p2.GetProduct("Wheat") > wheatHome - 5_000 && eng.State.VotingPowerOf(p2) > AssemblyService.VotingPower(p2), "(the master's stockpile, and the votes its territory's people add, are intact)");

    // Settings can still be changed after loading, and the simulation carries on.
    Check(eng.SetTerritoryTaxRates(persia2.Id, new TaxRates { Peasants = 20, Craftsmen = 20, MilitaryPersonnel = 5, Merchants = 20, Spies = 10, Saboteurs = 10 }) is null && persia2.TaxRates.Peasants == 20, "the settings can be changed again");
    double g = p2.Gold;
    for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
    Check(persia2.ConstructionQueue.Count == 0 && persia2.ProductionBuildings.Values.Sum() == mills0 + 2 && persia2.Farms == farms0 + 1, "the builds finish in the territory (two mills and the farm)");
    Check(p2.ProductionBuildings.Values.Sum() == p.ProductionBuildings.Values.Sum() && persia2.Gold == 0, "they are the territory's, and it still holds no gold");
    Check(persia2.Population != pop3, "its people keep being born and dying");
    Check(p2.Gold > g, "and the master keeps collecting from it");
}

using (var eng = NewDipEngine(211))
{
    // Two territories, two sets of settings: managed one at a time, never as one pooled inventory.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    p.Gold = 1e8;
    Warfare.AnnexNation(s, p, persia); Warfare.AnnexNation(s, p, spain);
    persia.Wood = 1e7; persia.Stone = 1e7; persia.Iron = 1e7; spain.Wood = 0; spain.Stone = 0; spain.Iron = 0;
    var mill = ProductionCatalog.All.Where(m => m.WoodCost > 0 && m.BuildDays >= 4).OrderBy(m => m.GoldCost).First();
    long persiaPeople = persia.Population, spainPeople = spain.Population;
    Check(eng.BuildProductionInTerritory(persia.Id, mill.Id, 1) is null && eng.BuildProductionInTerritory(spain.Id, mill.Id, 1) is not null, "the one with materials can build; the one without cannot (its materials are its own)");
    Check(eng.SetTerritoryTaxRates(spain.Id, new TaxRates { Peasants = 5, Craftsmen = 5, MilitaryPersonnel = 5, Merchants = 5, Spies = 5, Saboteurs = 5 }) is null && persia.TaxRates.Peasants != 5, "settings are per territory");
    Check(eng.ReportTerritory(persia.Id)!.UnderConstruction == 1 && eng.ReportTerritory(spain.Id)!.UnderConstruction == 0, "each reports its own builds");
    Check(eng.ReportTerritory(persia.Id)!.Population == persiaPeople && eng.ReportTerritory(spain.Id)!.Population == spainPeople && eng.RealmPopulation == p.Population + persiaPeople + spainPeople, "each its own people, all counted once in the realm");
}

Console.WriteLine("== 73. Assembly: proposal scope, the production product, and enforcement by the real systems ==");
void PassLaw(GameEngine e, string typeId, string? target = null, string? product = null, int days = 400)
{
    Check(e.SubmitProposal(typeId, target, days, 30, product) is null, $"(proposal {typeId} submitted)");
    var prop = e.State.AssemblyProposals.Last();
    foreach (var n in e.State.AllNations().Where(n => !n.IsEliminated)) prop.Votes[n.Id] = true;
    for (int i = 0; i < 30; i++) { e.State.PlayerNation.Gold = Math.Max(e.State.PlayerNation.Gold, 1_000_000); e.AdvanceOneDay(); }
}

using (var eng = NewDipEngine(220))
{
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");

    // The form offers exactly five proposals, each with the scope the rules give it.
    var offered = AssemblyProposalTypes.Offered.ToList();
    Check(offered.Select(t => t.Id).SequenceEqual(new[] { "military_restriction", "weapon_sales_ban", "production_ban", "annexation_ban", "war_ban" }), "the form offers the five proposals");
    Check(offered[0].Scope == AssemblyScope.Country && offered.Skip(1).All(t => t.Scope == AssemblyScope.Global), "only the military training restriction is about one country; the others are global");
    Check(offered.Where(t => t.NeedsProduct).Select(t => t.Id).SequenceEqual(new[] { "production_ban" }), "only the production ban needs a product");
    Check(AssemblyProposalTypes.Get("weapon_embargo") is { Offered: false } && !offered.Any(t => t.Id == "weapon_embargo"), "the retired embargo is not offered, but laws already passed still read");
    Check(eng.SubmitProposal("weapon_embargo", persia.Id, 60) == "Invalid proposal type." && eng.SubmitProposal("nonsense", persia.Id, 60) == "Invalid proposal type.", "so it cannot be proposed again");

    // Military Training Restriction: one valid country, required and stored.
    Check(eng.SubmitProposal("military_restriction", null, 60) is { } noTarget && noTarget.Contains("Choose the country"), "a military training restriction needs a country");
    Check(eng.SubmitProposal("military_restriction", "", 60) is not null && eng.SubmitProposal("military_restriction", "nowhere", 60) is not null && eng.SubmitProposal("military_restriction", p.Id, 60) is not null, "an unknown country, no country, or oneself is refused");
    Check(Ai(eng, "japan") is { } jp && (jp.IsEliminated = true) && eng.SubmitProposal("military_restriction", jp.Id, 60) is not null && !(jp.IsEliminated = false), "so is an annexed country");
    Check(s.AssemblyProposals.Count == 0, "(nothing was stored by the refusals)");
    Check(eng.SubmitProposal("military_restriction", persia.Id, 60, 30, "wheat") is null, "a valid country is accepted");
    var mil = s.AssemblyProposals.Single();
    Check(mil.TargetId == persia.Id && mil.ProductId == "" && mil.TypeId == "military_restriction", "it stores its country, and no product");
    s.AssemblyProposals.Clear();

    // The global proposals need no country, and store none (even if one is offered).
    foreach (var type in new[] { "weapon_sales_ban", "annexation_ban", "war_ban" })
    {
        Check(eng.SubmitProposal(type, null, 60) is null && eng.SubmitProposal(type, "", 60) is null && eng.SubmitProposal(type, spain.Id, 60, 30, "wheat") is null, $"{type}: needs no country");
        Check(s.AssemblyProposals.All(x => x.TargetId == "" && x.ProductId == ""), $"{type}: stores no country and no product");
        s.AssemblyProposals.Clear();
    }

    // The production ban needs a product from the existing catalogue, and no country.
    Check(eng.SubmitProposal("production_ban", null, 60) is { } noProduct && noProduct.Contains("Choose the product"), "a production ban needs a product");
    Check(eng.SubmitProposal("production_ban", null, 60, 30, "") is not null && eng.SubmitProposal("production_ban", null, 60, 30, "unobtainium") is { } unknown && unknown.Contains("Unknown"), "an empty or unknown product is refused");
    Check(eng.SubmitProposal("production_ban", persia.Id, 60, 30, "wheat") is null, "a product is accepted (a country offered with it is ignored)");
    var prod = s.AssemblyProposals.Single();
    Check(prod.ProductId == "wheat" && prod.TargetId == "", "it stores the product and no country");
    Check(TradeCatalog.All.All(t => eng.SubmitProposal("production_ban", null, 60, 30, t.Id) is null), "every product of the trade catalogue can be chosen");
    s.AssemblyProposals.Clear();

    // The estimate for a global proposal counts everyone (there is no target to leave out).
    var global = new AssemblyProposal { TypeId = "war_ban", TargetId = "", ProposerId = p.Id };
    var all = s.AllNations().ToList();
    var est = AssemblyService.EstimateVotes(global, all, p.Id);
    Check(est.forVotes + est.againstVotes == all.Where(n => !n.IsEliminated).Sum(n => AssemblyService.VotingPower(n, all)), "estimated votes for a global law count every country");
    var local = new AssemblyProposal { TypeId = "military_restriction", TargetId = persia.Id, ProposerId = p.Id };
    var est2 = AssemblyService.EstimateVotes(local, all, p.Id);
    Check(est2.forVotes + est2.againstVotes == est.forVotes + est.againstVotes - AssemblyService.VotingPower(persia, all), "...while a country-scoped one leaves out its target, as before");
}

using (var eng = NewDipEngine(221))
{
    // Voting resolution keeps its rules; the law it makes carries exactly the proposal's scope. Rejected and expired laws are not in force.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    PassLaw(eng, "production_ban", null, "wheat", 100);
    var law = s.ActiveAssemblyPolicies.Single();
    Check(law.TypeId == "production_ban" && law.TargetId == "" && law.ProductId == "wheat" && law.ExpirationDate == law.ActivationDate.AddDays(100), "an approved production ban becomes a law on that product, with no country");
    Check(s.AssemblyProposals.Single().Status == ProposalStatus.Active, "(the proposal is active)");
    var approvedLine = s.Movements.Last(m => m.Kind == MovementKind.Assembly && m.Status == MovementStatus.Completed);
    Check(approvedLine.Text.StartsWith("Assembly APPROVED") && approvedLine.Text.Contains("Wheat") && !approvedLine.Text.Contains("against"), "the report names the product and no country");
    Check(AssemblyService.ProductionBan(s.ActiveAssemblyPolicies, "wheat", s.CurrentDate) is not null && AssemblyService.ProductionBan(s.ActiveAssemblyPolicies, "bread", s.CurrentDate) is null, "it holds for wheat only");
    for (int i = 0; i < 101; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(s.ActiveAssemblyPolicies.Count == 0 && s.AssemblyProposals.Single().Status == ProposalStatus.Expired, "after its term the law is gone");
    Check(AssemblyService.ProductionBan(s.ActiveAssemblyPolicies, "wheat", s.CurrentDate) is null && s.Movements.Last(m => m.Kind == MovementKind.Assembly).Text.StartsWith("Assembly policy expired"), "and recorded as expired");

    // A rejected proposal makes no law.
    s.AssemblyProposals.Clear();
    Check(eng.SubmitProposal("war_ban", null, 100) is null, "(a war ban is proposed)");
    var prop = s.AssemblyProposals.Single();
    foreach (var n in s.AllNations().Where(n => n.Id != p.Id && !n.IsEliminated)) prop.Votes[n.Id] = false;
    for (int i = 0; i < 31; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(prop.Status == ProposalStatus.Rejected && s.ActiveAssemblyPolicies.Count == 0, "a rejected proposal passes no law");
    Check(TreatyService.WarDeclarationBlock(s) is null && eng.DeclareWar(persia.Id) is null, "and nothing is enforced: war can be declared");
}

using (var eng = NewDipEngine(222))
{
    // Persistence: proposals, product and the law in force survive save / load, and still bind afterwards.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    eng.SubmitProposal("military_restriction", persia.Id, 200);
    eng.SubmitProposal("production_ban", null, 200, 30, "wheat");
    eng.SubmitProposal("war_ban", persia.Id, 200);
    var before = s.AssemblyProposals.Select(x => (x.TypeId, x.TargetId, x.ProductId, x.EffectDurationDays, x.Status)).ToList();
    Check(before.Select(x => x.TargetId).SequenceEqual(new[] { persia.Id, "", "" }) && before[1].ProductId == "wheat", "(stored: one country, one product, no stray country)");
    var ban = s.AssemblyProposals[1]; foreach (var n in s.AllNations().Where(n => !n.IsEliminated)) ban.Votes[n.Id] = true;
    for (int i = 0; i < 30; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    var lawsBefore = s.ActiveAssemblyPolicies.Select(x => (x.TypeId, x.TargetId, x.ProductId, x.ExpirationDate)).ToList();
    Check(lawsBefore.Count(l => l.TypeId == "production_ban" && l.ProductId == "wheat" && l.TargetId == "") == 1, "(the production ban passed)");
    await eng.SaveAsync();
    s.AssemblyProposals.Clear(); s.ActiveAssemblyPolicies.Clear();
    await eng.LoadAsync();
    var s2 = eng.State;
    Check(s2.AssemblyProposals.Select(x => (x.TypeId, x.TargetId, x.ProductId, x.EffectDurationDays)).SequenceEqual(before.Select(x => (x.TypeId, x.TargetId, x.ProductId, x.EffectDurationDays))), "proposals keep their type, country, product and term");
    Check(s2.ActiveAssemblyPolicies.Select(x => (x.TypeId, x.TargetId, x.ProductId, x.ExpirationDate)).SequenceEqual(lawsBefore), "the law keeps its scope, its product and its end date");
    Check(AssemblyService.ProductionBlock(s2, "wheat") is not null && AssemblyService.ProductionBlock(s2, "bread") is null, "and still binds wheat only after loading");
}

using (var eng = NewDipEngine(223))
{
    // Military Training Restriction affects only its country: the AI's recruiting is halved, the player's training takes twice as long.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    foreach (var n in new[] { persia, spain }) { n.BaseSoldiers = 20_000; n.Units = UnitCatalog.SeedArmy(1000); n.Gold = 1e9; }
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "military_restriction", TargetId = persia.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(100) });
    int p0 = persia.Soldiers, s0 = spain.Soldiers;
    AiWorldService.Economy(s, persia); AiWorldService.Economy(s, spain);
    int addedPersia = persia.Soldiers - p0, addedSpain = spain.Soldiers - s0;
    Check(addedSpain > 40 && Math.Abs(addedPersia - addedSpain / 2) <= 1, $"the restricted country recruits half as much ({addedPersia} against {addedSpain}); the other is untouched");

    p.Gold = 1e9; p.Wood = 1e7; p.Iron = 1e7; p.AddMilitaryItem("pike", 10_000); p.AddMilitaryItem("helmet", 10_000);
    Check(eng.StartRecruitment(UnitType.Pikeman, 1000) is null, "(the player recruits normally)");
    double normalDays = p.RecruitmentQueue.Last().TotalDays; p.RecruitmentQueue.Clear();
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "military_restriction", TargetId = p.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(100) });
    Check(eng.StartRecruitment(UnitType.Pikeman, 1000) is null, "(and under the restriction too)");
    Check(p.RecruitmentQueue.Last().TotalDays > normalDays, "a restriction on the player makes its soldiers train for longer");
    s.ActiveAssemblyPolicies.Clear();
    p.RecruitmentQueue.Clear();
    Check(eng.StartRecruitment(UnitType.Pikeman, 1000) is null, "(recruiting again)");
    Check(p.RecruitmentQueue.Last().TotalDays == normalDays, "and once the law is gone, training is normal again");
}

using (var eng = NewDipEngine(224))
{
    // Weapon sales: one law stops every purchase and sale of military equipment, with any country.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var france = Ai(eng, "france");
    p.Gold = 1e9; p.Helmets = 1000; persia.Helmets = 5000; france.Helmets = 5000; persia.Gold = 1e9; france.Gold = 1e9; persia.Wood = 1e6; france.Wood = 1e6; p.Wood = 1e6;
    Embassy(eng, persia, france);
    Check(eng.BuyProduct(persia.Id, "helmet", 10) is null && eng.SellProduct(france.Id, "helmet", 10, 500) is null, "(before the law: equipment trades normally)");
    PassLaw(eng, "weapon_sales_ban", null, null, 100);
    Check(eng.BuyProduct(persia.Id, "helmet", 10) is { } b1 && b1.Contains("prohibited") && eng.BuyProduct(france.Id, "pike", 10) is { } b2 && b2.Contains("Assembly"), "buying military equipment is refused from every country");
    Check(eng.SellProduct(persia.Id, "helmet", 10, 500) is { } sl1 && sl1.Contains("prohibited") && eng.SellProduct(france.Id, "dagger", 10, 500) is not null, "selling it is refused to every country");
    double gold = p.Gold, helmets = p.Helmets;
    eng.SellProduct(persia.Id, "helmet", 10, 500);
    Check(p.Gold == gold && p.Helmets == helmets, "(a refused sale moved nothing)");
    Check(eng.BuyProduct(persia.Id, "wood", 10) is null && eng.SellProduct(france.Id, "wood", 10, 100) is null, "everything that is not military equipment still trades");
    for (int i = 0; i < 101; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(eng.BuyProduct(persia.Id, "helmet", 10) is null, "after the term, equipment trades again");
    // A retired country-by-country embargo from an older save still holds for its own country only.
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "weapon_embargo", TargetId = persia.Id, ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(50) });
    Check(eng.BuyProduct(persia.Id, "helmet", 10) is not null && eng.BuyProduct(france.Id, "helmet", 10) is null, "an old embargo still holds, for its country only");
}

using (var eng = NewDipEngine(225))
{
    // Production ban: only the chosen product, in every country's production — the player's, an AI country's and an annexed territory's.
    using var control = NewDipEngine(225);
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    var sc = control.State; var pc = sc.PlayerNation; var persiaC = Ai(control, "persia"); var spainC = Ai(control, "spain");
    foreach (var (st, player, ai, terr) in new[] { (s, p, persia, spain), (sc, pc, persiaC, spainC) })
    {
        player.ProductionBuildings["farm"] = 50; player.ProductionBuildings["bakery"] = 50; ai.ProductionBuildings["farm"] = 50; ai.ProductionBuildings["bakery"] = 50;
        player.Mines = 10; player.Iron = 1000; player.Helmets = 0;
        Warfare.AnnexNation(st, player, terr);
        terr.ProductionBuildings["farm"] = 40; terr.ProductionBuildings["bakery"] = 40;
        player.MilitaryCraftQueue.Add(new MilitaryCraftProject { RecipeId = "helmet", DaysLeft = 1, TotalDays = 5 });
    }
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "production_ban", TargetId = "", ProductId = "wheat", ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(60) });
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "production_ban", TargetId = "", ProductId = "iron", ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(60) });
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "production_ban", TargetId = "", ProductId = "helmet", ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(60) });
    eng.AdvanceOneDay(); control.AdvanceOneDay();

    Check(pc.GetProduct("Wheat") - p.GetProduct("Wheat") > 400, "the player's farms make no wheat while it is prohibited");
    Check(persiaC.GetProduct("Wheat") - persia.GetProduct("Wheat") > 400, "nor do an AI country's");
    Check(spainC.GetProduct("Wheat") - spain.GetProduct("Wheat") > 300, "nor an annexed territory's");
    Check(Math.Abs(pc.GetProduct("Bread") - p.GetProduct("Bread")) < 1e-6 && Math.Abs(persiaC.GetProduct("Bread") - persia.GetProduct("Bread")) < 1e-6 && Math.Abs(spainC.GetProduct("Bread") - spain.GetProduct("Bread")) < 1e-6,
        "bread, which is not prohibited, is made exactly as before everywhere");
    Check(pc.Helmets - p.Helmets >= 10 && p.Helmets == 0 && p.MilitaryCraftQueue.Count == 0, "a batch of helmets finishing under the ban comes to nothing (and the order ends)");
    Check(s.Inbox.Any(m => m.Topic == InboxTopic.Production && m.Summary.Contains("prohibited")), "the player is told");
    Check(Math.Abs(pc.Wood - p.Wood) < 1e-6 && Math.Abs(pc.Stone - p.Stone) < 1e-6, "other products are untouched");

    // Cannot start what cannot be made.
    p.Gold = 1e9; p.Wood = 1e7; p.Stone = 1e7; p.Iron = 1e7; p.Copper = 1e7; p.Lead = 1e7;
    Check(eng.StartMilitaryCraft("helmet") is { } c1 && c1.Contains("prohibited") && eng.StartMilitaryCraft("dagger") is null, "crafting a prohibited item cannot be started; another can");
    Check(eng.BuildProduction("farm", 1) is { } b1 && b1.Contains("Wheat") && eng.MaxBuildProduction("farm").Max == 0 && eng.BuildProduction("bakery", 1) is null, "a mill for the prohibited product cannot be built; others can");
    Check(eng.StartConstruction(BuildingType.Mine) is { } m1 && m1.Contains("Iron") && eng.StartConstruction(BuildingType.Farm) is null, "nor a mine while iron is prohibited, but a farm is fine");
    spain.GoodsInventory["Wheat"] = 0; p.Gold = 1e9; spain.Wood = 1e7; spain.Stone = 1e7; spain.Iron = 1e7;
    Check(eng.BuildProductionInTerritory(spain.Id, "farm", 1) is { } t1 && t1.Contains("Wheat") && eng.BuildProductionInTerritory(spain.Id, "bakery", 1) is null, "the same in an annexed territory");
    Check(eng.ReportTerritory(spain.Id)!.MillList.Single(m => m.Id == "farm").OutputPerDay == 0 && eng.ReportTerritory(spain.Id)!.MillList.Single(m => m.Id == "bakery").OutputPerDay > 0, "and its report shows the prohibited mills idle");

    // The law ends: production resumes.
    for (int i = 0; i < 61; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(AssemblyService.BannedProducts(s).Count == 0 && eng.BuildProduction("farm", 1) is null, "after its term the product can be made again");
}

using (var eng = NewDipEngine(229))
{
    // Raw materials, measured in isolation: the same world with and without the ban, nothing else different.
    using var control = NewDipEngine(229);
    foreach (var e in new[] { eng, control }) { var pl = e.State.PlayerNation; pl.ProductionBuildings.Clear(); pl.Workshops = 0; pl.Mines = 10; pl.Sawmills = 5; pl.Iron = 1_000_000; pl.Wood = 1_000_000; }   // (a deep stock, so the people's daily need for wood cannot hide the difference)   // (no mills or workshops that use iron and wood either: only the mines and sawmills matter)
    eng.State.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "production_ban", TargetId = "", ProductId = "iron", ActivationDate = eng.State.CurrentDate, ExpirationDate = eng.State.CurrentDate.AddDays(30) });
    double i0 = eng.State.PlayerNation.Iron, w0 = eng.State.PlayerNation.Wood, ci0 = control.State.PlayerNation.Iron, cw0 = control.State.PlayerNation.Wood;
    eng.AdvanceOneDay(); control.AdvanceOneDay();
    // (the two worlds are identical but for the law, so their difference is exactly what the ban took away)
    double ironLost = (control.State.PlayerNation.Iron - ci0) - (eng.State.PlayerNation.Iron - i0);
    Check(Math.Abs(ironLost - 10 * Balance.IronPerMinePerDay) < 1e-3, $"while iron is prohibited the mines' {ironLost:N0} iron a day is not dug");
    Check(Math.Abs((eng.State.PlayerNation.Wood - w0) - (control.State.PlayerNation.Wood - cw0)) < 1e-3, "the sawmills are not affected by an iron ban");
    eng.State.ActiveAssemblyPolicies.Clear();
    eng.State.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "production_ban", TargetId = "", ProductId = "wood", ActivationDate = eng.State.CurrentDate, ExpirationDate = eng.State.CurrentDate.AddDays(30) });
    double w1 = eng.State.PlayerNation.Wood, cw1 = control.State.PlayerNation.Wood;
    eng.AdvanceOneDay(); control.AdvanceOneDay();
    double woodLost = (control.State.PlayerNation.Wood - cw1) - (eng.State.PlayerNation.Wood - w1);
    Check(Math.Abs(woodLost - 5 * Balance.WoodPerSawmillPerDay) < 1e-3, $"while wood is prohibited the sawmills' {woodLost:N0} wood a day is not cut");
}

using (var eng = NewDipEngine(226))
{
    // Annexation: one law forbids it for everyone, in every validation — and an AI conqueror is bound like the player.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter");
    PassLaw(eng, "annexation_ban", null, null, 200);
    var law = s.ActiveAssemblyPolicies.Single();
    Check(law.TargetId == "" && TreatyService.AnnexationBlock(s, persia.Id) is { } b1 && b1.Contains("any country") && TreatyService.AnnexationBlock(s, p.Id) is not null && TreatyService.AnnexationBlock(s, easter.Id) is not null,
        "no country, the player's included, can be annexed");

    // The player: no invasion, no annexation after a victory.
    p.Units = UnitCatalog.SeedArmy(6000); persia.Units = UnitCatalog.SeedArmy(3500);
    eng.DeclareWar(persia.Id);
    Check(!eng.LaunchInvasion(persia.Id, 3000).ok && !eng.ConfirmAttack(persia.Id, 3000).Ok && p.Soldiers == 6000, "the player cannot launch an invasion");
    s.PendingVictories.Add(new VictoryDecision { LoserId = persia.Id, LoserName = persia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    persia.AtWarWithPlayer = true;
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Annex).Outcome == DipOutcome.Invalid && !persia.IsEliminated && eng.PendingVictory(persia.Id) is not null, "nor choose to annex a beaten country");
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Resources).Ok && !persia.IsEliminated, "(the other choices still work)");
    Check(eng.ProposePeace(spain.Id, new[] { PeaceTerm.Annexation(spain.Id, p.Id) }).Outcome == DipOutcome.Invalid, "nor accept a surrender by treaty");

    // An AI conqueror: the battle is won, the war goes on, nothing is taken.
    var eastSpain = Ai(eng, "france");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    double w0 = spain.Gold, l0 = easter.Gold;
    VictoryService.ResolveForAi(s, spain, easter);
    Check(!easter.IsEliminated && spain.Gold == w0 && easter.Gold == l0 && AiWorldService.AtWar(s, spain.Id, easter.Id) && HasLog(eng, "cannot annex it"), "an AI conqueror cannot annex either: the war goes on and nothing changes hands");

    // An enemy army that breaks the player's home army cannot take the country.
    persia.AtWarWithPlayer = true; p.Units = UnitCatalog.SeedArmy(200); persia.Units = UnitCatalog.SeedArmy(10_000);
    var march = TreatyService.LaunchMarch(s, persia, p, 4000)!;
    for (int i = 0; i < march.TotalDays; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); }
    Check(!p.IsEliminated && !s.Defeated && s.Movements.Any(m => m.Text.Contains("cannot annex")), "an invader that wins the field cannot annex the player's country while the ban holds");

    // Without the law the same invasion ends the country (so the guard above is not vacuous).
    using var control = NewDipEngine(226);
    var cs = control.State; var cp = cs.PlayerNation; var cpersia = Ai(control, "persia");
    cpersia.AtWarWithPlayer = true; cp.Units = UnitCatalog.SeedArmy(200); cpersia.Units = UnitCatalog.SeedArmy(10_000);
    var cmarch = TreatyService.LaunchMarch(cs, cpersia, cp, 4000)!;
    for (int i = 0; i < cmarch.TotalDays; i++) { cp.Gold = 1e9; control.AdvanceOneDay(); }
    Check(cp.IsEliminated, "(without the ban, that army annexes the player)");

    // The law ends.
    for (int i = 0; i < 201; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); if (p.IsEliminated) break; }
    Check(TreatyService.AnnexationBlock(s, persia.Id) is null || p.IsEliminated, "after its term annexation is allowed again");
}

using (var eng = NewDipEngine(227))
{
    // War: one law forbids new declarations by anyone; wars under way go on; it ends with its term.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    p.Units = UnitCatalog.SeedArmy(6000);
    Check(eng.DeclareWar(spain.Id) is null && spain.AtWarWithPlayer, "(a war is already under way)");
    PassLaw(eng, "war_ban", null, null, 1000);
    p.Units = UnitCatalog.SeedArmy(6000);   // the month of voting went by: restore the home army so the checks below mean something
    int wars0 = s.Wars.Count;
    Check(TreatyService.WarDeclarationBlock(s) is { } why && why.Contains("forbidden declaring war"), "the Assembly's ban is in force");

    int soldiers = p.Soldiers;
    Check(soldiers == 6000, "(the home army is 6,000)");
    Check(eng.DeclareWar(persia.Id) is { } refused && refused.Contains("Assembly") && !persia.AtWarWithPlayer, "the player cannot declare war");
    Check(!eng.ConfirmAttack(persia.Id, 2000).Ok && eng.ConfirmAttack(persia.Id, 2000).Message.Contains("forbidden declaring war") && !persia.AtWarWithPlayer && p.Soldiers == soldiers && s.MarchingArmies.Count == 0,
        "nor start one by sending troops: no war, and no soldier leaves");
    Check(eng.PlanAttack(persia.Id)?.BlockReason?.Contains("Assembly") == true, "the attack popup says why");
    var already = eng.ConfirmAttack(spain.Id, 2000);
    Check(spain.AtWarWithPlayer && already.Ok && !already.WarDeclared, "a war already under way goes on, and its armies can still be sent");

    // AI countries: directly, and through the daily simulation.
    var france = Ai(eng, "france");
    AiWorldService.DeclareWarOnPlayer(s, persia);
    AiWorldService.DeclareWar(s, persia, france, new Random(1));
    AiWorldService.DeclareWar(s, france, persia, new Random(2));
    Check(!persia.AtWarWithPlayer && s.Wars.Count == wars0, "AI countries cannot declare war, on the player or on each other");
    for (int i = 0; i < 700; i++) { p.Gold = 1e9; eng.AdvanceOneDay(); p.Units = UnitCatalog.SeedArmy(20_000); }
    Check(s.Wars.Count == wars0 && s.OtherNations.Count(n => n.AtWarWithPlayer) <= 1, "over two years of simulation no AI country starts a war (only the one already under way)");

    using var control = NewDipEngine(227);
    var cs = control.State;
    for (int i = 0; i < 700; i++) { cs.PlayerNation.Gold = 1e9; control.AdvanceOneDay(); cs.PlayerNation.Units = UnitCatalog.SeedArmy(20_000); }
    Check(cs.Wars.Count > 0 || cs.OtherNations.Any(n => n.AtWarWithPlayer), "(without the ban the same world does go to war)");
    Check(s.Wars.Count == wars0, "(and the banned one still has no new war)");

    // Expired: war can be declared again.
    s.ActiveAssemblyPolicies.Clear();
    Check(TreatyService.WarDeclarationBlock(s) is null && eng.DeclareWar(persia.Id) is null && persia.AtWarWithPlayer, "once the law is gone, war can be declared again");
}

using (var eng = NewDipEngine(228))
{
    // The war ban also stops an AI country declaring war over a refused tribute demand.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia");
    p.Units = UnitCatalog.SeedArmy(100); persia.Units = UnitCatalog.SeedArmy(20_000);
    s.ActiveAssemblyPolicies.Add(new ActiveAssemblyPolicy { TypeId = "war_ban", TargetId = "", ActivationDate = s.CurrentDate, ExpirationDate = s.CurrentDate.AddDays(1000) });
    for (int i = 0; i < 40; i++) { s.PlayerNation.DiplomacyCooldowns.Clear(); eng.DemandTribute(persia.Id); }
    Check(!persia.AtWarWithPlayer, "a refused tribute demand never leads to a war while the ban holds");
    s.ActiveAssemblyPolicies.Clear();
    for (int i = 0; i < 80 && !persia.AtWarWithPlayer; i++) { s.PlayerNation.DiplomacyCooldowns.Clear(); eng.DemandTribute(persia.Id); }
    Check(persia.AtWarWithPlayer, "(without it, the same demands do)");
}

Console.WriteLine("== 74. The political map: natural coasts, exact shared borders, islands, capitals, annexation, old saves ==");
{
    // ---- helpers over the polygons the game really has
    static bool MgInRing(double x, double y, List<MapPoint> ring)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            double xi = ring[i].X, yi = ring[i].Y, xj = ring[j].X, yj = ring[j].Y;
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
    static bool MgInShape(double x, double y, List<List<MapPoint>> polygons) => polygons.Any(r => MgInRing(x, y, r));
    static string MgEdgeKey(MapPoint a, MapPoint b) =>
        (a.X < b.X || (a.X == b.X && a.Y <= b.Y)) ? $"{a.X:R},{a.Y:R}|{b.X:R},{b.Y:R}" : $"{b.X:R},{b.Y:R}|{a.X:R},{a.Y:R}";

    var eng = new GameEngine(new SimulationService(74), new SaveService(saveFolder));
    var st = eng.State;
    var shapes = new List<(string Id, string Kind, List<List<MapPoint>> Polys)>();
    foreach (var n in st.AllNations()) shapes.Add((n.Id, "nation", n.Territory));
    foreach (var r in st.NeutralRegions) shapes.Add(("n:" + r.Name, "neutral", r.Polygons));
    foreach (var r in st.FrontierRegions) shapes.Add(("f:" + r.Name, "frontier", r.Polygons));
    var allRings = shapes.SelectMany(s => s.Polys.Select(p => (s.Id, s.Kind, Ring: p))).ToList();

    Check(st.MapVersion == WorldGeometry.Version && st.NeutralRegions.Count == 13 && st.FrontierRegions.Count == 3, "a new game carries the current map edition: 41 countries, 13 neutral territories (Greenland and Svalbard are new), 3 frontier regions");
    Check(allRings.All(r => r.Ring.Count >= 4 && r.Ring[0].X == r.Ring[^1].X && r.Ring[0].Y == r.Ring[^1].Y), "every polygon is a closed ring of at least three corners");
    Check(allRings.All(r => r.Ring.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X >= 0 && p.X <= 2200 && p.Y >= 0 && p.Y <= 1150)), "all points lie inside the 2200 x 1150 map");

    // ---- shared borders are the same points on both sides
    var uses = new Dictionary<string, List<string>>();
    foreach (var (id, _, ring) in allRings)
        for (int i = 0; i < ring.Count - 1; i++)
        {
            if (ring[i].X == ring[i + 1].X && ring[i].Y == ring[i + 1].Y) continue;
            var k = MgEdgeKey(ring[i], ring[i + 1]);
            if (!uses.TryGetValue(k, out var l)) uses[k] = l = new();
            l.Add(id);
        }
    int sharedEdges = uses.Values.Count(u => u.Count == 2 && u[0] != u[1]);
    Check(uses.Values.All(u => u.Count <= 2), "no border segment is claimed by three polygons");
    Check(sharedEdges > 3000, $"neighbours meet along identical segments ({sharedEdges:N0} shared border segments, each the same two points in both realms)");

    // no T-junction defects: a corner of one realm never sits on, or beside, another realm's edge without being one of that edge's own corners
    {
        var segs = new List<(string Id, MapPoint A, MapPoint B)>();
        foreach (var (id, _, ring) in allRings) for (int i = 0; i < ring.Count - 1; i++) segs.Add((id, ring[i], ring[i + 1]));
        var grid = new Dictionary<(int, int), List<int>>();
        for (int s = 0; s < segs.Count; s++)
            for (int gx = (int)Math.Floor(Math.Min(segs[s].A.X, segs[s].B.X) / 8); gx <= (int)Math.Floor(Math.Max(segs[s].A.X, segs[s].B.X) / 8); gx++)
                for (int gy = (int)Math.Floor(Math.Min(segs[s].A.Y, segs[s].B.Y) / 8); gy <= (int)Math.Floor(Math.Max(segs[s].A.Y, segs[s].B.Y) / 8); gy++)
                { if (!grid.TryGetValue((gx, gy), out var l)) grid[(gx, gy)] = l = new(); l.Add(s); }
        int defects = 0;
        foreach (var (id, _, ring) in allRings)
            foreach (var p in ring.Take(ring.Count - 1))
                if (grid.TryGetValue(((int)Math.Floor(p.X / 8), (int)Math.Floor(p.Y / 8)), out var near))
                    foreach (int s in near)
                    {
                        var (sid, a, b) = segs[s]; if (sid == id) continue;
                        if ((p.X == a.X && p.Y == a.Y) || (p.X == b.X && p.Y == b.Y)) continue;
                        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy; if (len2 == 0) continue;
                        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2; if (t <= 0 || t >= 1) continue;
                        double dist = Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / Math.Sqrt(len2);
                        if (dist < 0.04) defects++;
                    }
        Check(defects == 0, $"no gap or overlap at a junction: no corner lies on another realm's border ({defects} found)");
    }

    // ---- no overlaps, almost no gaps: probe the whole map on a regular grid
    {
        int probes = 0, overlaps = 0, covered = 0;
        for (double y = 3; y < 1150; y += 5)
            for (double x = 3; x < 2200; x += 5)
            {
                probes++;
                int owners = shapes.Count(s => s.Polys.Any(r => MgInRing(x, y, r)));
                if (owners > 1) overlaps++; else if (owners == 1) covered++;
            }
        Check(overlaps == 0, $"no point of the map belongs to two realms ({probes:N0} probes)");
        Check(covered > 30_000, $"the political map covers the land ({covered:N0} probes fall in a country, neutral land or frontier region)");
    }

    // ---- no boxes, regular polygons or ruler-straight edges: the geometry is the land's, not a drawing
    {
        bool MgRegular(List<MapPoint> r)
        {
            var pts = r.Take(r.Count - 1).ToList(); if (pts.Count < 5 || pts.Count > 14) return false;
            double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
            var rad = pts.Select(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy))).ToList(); double m = rad.Average();
            return m > 0 && Math.Sqrt(rad.Sum(v => (v - m) * (v - m)) / rad.Count) / m < 0.06;
        }
        double MgArea(List<MapPoint> r) { double a = 0; for (int i = 0; i < r.Count - 1; i++) a += r[i].X * r[i + 1].Y - r[i + 1].X * r[i].Y; return Math.Abs(a) / 2; }
        bool MgBox(List<MapPoint> r)
        {
            double w = r.Max(p => p.X) - r.Min(p => p.X), h = r.Max(p => p.Y) - r.Min(p => p.Y);
            return r.Count <= 9 && w * h > 0 && MgArea(r) / (w * h) > 0.88;
        }
        int axis = 0;
        foreach (var (_, _, ring) in allRings)
            for (int i = 0; i < ring.Count - 1; i++)
            {
                var a = ring[i]; var b = ring[i + 1];
                bool frame = (a.X == 0 && b.X == 0) || (a.Y == 0 && b.Y == 0) || (a.X == 2200 && b.X == 2200) || (a.Y == 1150 && b.Y == 1150);
                if (!frame && (a.X == b.X || a.Y == b.Y) && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) > 25) axis++;
            }
        Check(allRings.Count(r => MgRegular(r.Ring)) == 0, "no polygon is a regular octagon or other regular shape (the old Venice, Genoa, Papal States, Scotland, Crimea, UAE, Micronesia and Easter Island were)");
        Check(allRings.Count(r => MgBox(r.Ring)) == 0, "no polygon is a rectangle (the old Tibet, Hejaz, Jurchen and frontier-region boxes were)");
        Check(axis == 0, "no border or coast runs ruler-straight along the map's axes (the old Russia cut at x = 1250 and the Egypt-Libya line at x = 485 did)");
        Check(allRings.All(r => MgArea(r.Ring) > 4), "no polygon is a sliver or a dot");
    }

    // ---- who owns what: preserved lands, islands and exclaves are still separate polygons of the right owner
    bool MgHas(string id, double x0, double y0, double x1, double y1) => shapes.First(s => s.Id == id).Polys.Any(r => { double cx = r.Average(p => p.X), cy = r.Average(p => p.Y); return cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1; });
    Check(MgHas("spain", 1440, 560, 1530, 700) && MgHas("spain", 1030, 640, 1075, 690) && MgHas("spain", 330, 355, 360, 395) && MgHas("spain", 380, 360, 440, 400), "the Iberian Union keeps its overseas possessions as separate polygons: the Philippines, Ceylon, Sardinia and southern Italy / Sicily");
    Check(MgHas("venice", 470, 400, 530, 430), "Venice holds Crete as an exclave of its own");
    Check(st.AllNations().First(n => n.Id == "morocco").Territory.Count >= 3 && st.AllNations().First(n => n.Id == "japan").Territory.Count >= 3, "island and exclave countries keep several polygons (Japan's islands, Morocco's Saharan outpost and the Canaries)");
    Check(st.AllNations().First(n => n.Id == "micronesia").Territory.Count >= 2 && st.AllNations().First(n => n.Id == "easter").Territory.Count >= 1 && st.FrontierRegions.All(r => r.Polygons.Count >= 2), "the Pacific islands and the frontier archipelagos are islands, in several pieces");
    {
        var tibet = st.NeutralRegions.First(r => r.Name == "Tibet");
        var ming = st.AllNations().First(n => n.Id == "ming");
        Check(tibet.Polygons.Count >= 1 && MgInShape(tibet.LabelX, tibet.LabelY, tibet.Polygons) && !MgInShape(tibet.LabelX, tibet.LabelY, ming.Territory), "Tibet is a neutral plateau of its own and no longer lies inside Ming (it used to be a rectangle drawn over it)");
        Check(st.NeutralRegions.All(r => MgInShape(r.LabelX, r.LabelY, r.Polygons)) && st.FrontierRegions.All(r => MgInShape(r.LabelX, r.LabelY, r.Polygons)), "every neutral and frontier region is named on its own land, not in the sea");
        var egypt = st.AllNations().First(n => n.Id == "ottoman");
        Check(MgInShape(575, 470, egypt.Territory), "Egypt, the Nile Delta and Sinai are Ottoman land (they were missing, with the Ottoman rectangle drawn in the Arabian desert instead)");
    }

    // ---- capitals: inside their own realm, on land; the moved ones, listed
    var stock = new Dictionary<string, (double X, double Y)>
    {
        ["ottoman"] = (604, 382), ["spain"] = (206, 368), ["france"] = (281, 308), ["england"] = (221, 247), ["dutch"] = (307, 256), ["austria"] = (389, 297), ["poland"] = (555, 287), ["russia"] = (814, 179),
        ["sweden"] = (418, 156), ["venice"] = (372, 317), ["persia"] = (783, 433), ["mughal"] = (1040, 515), ["ming"] = (1299, 393), ["japan"] = (1611, 407), ["kazakh"] = (909, 298), ["morocco"] = (165, 465),
        ["denmark"] = (437, 127), ["crimea"] = (593, 309), ["ethiopia"] = (646, 649), ["siam"] = (1262, 611), ["korea"] = (1528, 363), ["bukhara"] = (899, 358), ["hre"] = (356, 258), ["scotland"] = (209, 209),
        ["genoa"] = (334, 329), ["papal"] = (386, 349), ["italy"] = (350, 320), ["croatia"] = (412, 321), ["vietnam"] = (1309, 582), ["burma"] = (1219, 544), ["ahom"] = (1199, 498), ["yuan"] = (1296, 299),
        ["nepal"] = (1097, 477), ["kongo"] = (485, 778), ["jurchens"] = (1464, 360), ["cambodia"] = (1300, 617), ["laos"] = (1288, 568), ["malaysia"] = (1267, 693), ["uae"] = (796, 512), ["micronesia"] = (1750, 690), ["easter"] = (2120, 900),
    };
    var moved = st.AllNations().Where(n => stock.TryGetValue(n.Id, out var s0) && (Math.Abs(n.MapX - s0.X) > 0.01 || Math.Abs(n.MapY - s0.Y) > 0.01)).Select(n => n.Id).OrderBy(x => x).ToList();
    Check(stock.Count == 41 && moved.SequenceEqual(new[] { "denmark", "dutch", "jurchens", "uae", "venice" }), $"only five capitals moved with the map (Copenhagen onto the Danish isles; Holland, Venice, Hetu Ala and Julfar onto their own coast): {string.Join(", ", moved)}");
    Check(st.AllNations().All(n => MgInShape(n.MapX, n.MapY, n.Territory)), "every one of the 41 capitals lies inside its own country's territory (Denmark's, Croatia's, Vietnam's, Ahom's and Laos's lay in a neighbour's)");
    Check(st.AllNations().Where(n => moved.Contains(n.Id)).All(n => Math.Sqrt(Math.Pow(n.MapX - stock[n.Id].X, 2) + Math.Pow(n.MapY - stock[n.Id].Y, 2)) <= 12 || n.Id == "denmark"), "a harbour capital moved at most a dozen map units");
    Check(st.AllNations().First(n => n.Id == "denmark").CapitalName == "Copenhagen", "Denmark-Norway's capital is Copenhagen, not the country's own name");
    Check(WorldGeometry.Capitals.Count == 5 && WorldGeometry.Capitals.All(kv => st.AllNations().First(n => n.Id == kv.Key) is var n && n.MapX == kv.Value.X && n.MapY == kv.Value.Y && n.CapitalName == kv.Value.Name), "the table of moved capitals (used to update old saves) agrees with the capitals a new game starts with");

    // ---- march times after the coordinate changes: the same rule, from valid capitals
    Check(Balance.MarchSpeedPerDay == 30 && Balance.MarchFallbackDays == 20, "the movement speed and the fallback are untouched (30 map units a day)");
    {
        var all = st.AllNations().ToList(); bool every = true; int pairs = 0;
        foreach (var a in all) foreach (var b in all)
        {
            var route = MapGeography.Route(a, b); pairs++;
            double d = Math.Sqrt(Math.Pow(a.MapX - b.MapX, 2) + Math.Pow(a.MapY - b.MapY, 2));
            if (route.UsedFallback || route.Distance is null || route.Days != Math.Max(1, (int)Math.Ceiling(d / Balance.MarchSpeedPerDay))) every = false;
        }
        Check(every && MapGeography.MissingCapitals(st).Count == 0, $"all {pairs:N0} capital-to-capital marches are valid and take distance / 30 days, rounded up, never less than one");
        var den = all.First(n => n.Id == "denmark"); var swe = all.First(n => n.Id == "sweden"); var hre = all.First(n => n.Id == "hre"); var rus = all.First(n => n.Id == "russia");
        Check(MapGeography.Route(den, swe).Days == 4 && MapGeography.Route(den, hre).Days == 2, "Copenhagen is four days from Stockholm and two from the Empire (from the Arctic, where the old coordinate put it, the same marches took two and six days)");
        Check(MapGeography.Route(all.First(n => n.Id == "ottoman"), all.First(n => n.Id == "persia")).Days == Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Pow(604 - 783, 2) + Math.Pow(382 - 433, 2)) / 30.0)), "capitals that did not move march exactly as before (Constantinople to Isfahan)");
    }

    // ---- selection: on either side of a real border the tap finds the two different realms
    {
        var edges = new List<(MapPoint A, MapPoint B, string One, string Two)>();
        var byKey = new Dictionary<string, (MapPoint A, MapPoint B)>();
        foreach (var (id, _, ring) in allRings) for (int i = 0; i < ring.Count - 1; i++) byKey.TryAdd(MgEdgeKey(ring[i], ring[i + 1]), (ring[i], ring[i + 1]));
        int tested = 0, right = 0; int step = 0;
        foreach (var kv in uses.Where(u => u.Value.Count == 2 && u.Value[0] != u.Value[1] && u.Value.All(v => !v.StartsWith("f:"))))
        {
            if (step++ % 9 != 0) continue;
            var (a, b) = byKey[kv.Key]; double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy); if (len < 1.5) continue;
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, nx = -dy / len, ny = dx / len;
            string Who(double x, double y) { var hit = shapes.Where(s => s.Polys.Any(r => MgInRing(x, y, r))).Select(s => s.Id).ToList(); return hit.Count == 1 ? hit[0] : "?"; }
            string p1 = Who(mx + nx * 0.3, my + ny * 0.3), p2 = Who(mx - nx * 0.3, my - ny * 0.3); tested++;
            if (p1 != "?" && p2 != "?" && p1 != p2 && new[] { p1, p2 }.OrderBy(x => x).SequenceEqual(kv.Value.OrderBy(x => x))) right++;
        }
        Check(tested > 300 && right >= tested * 0.97, $"tapping either side of a border finds its two realms: {right} of {tested} sampled borders ({(100.0 * right / tested):0.0}%)");
    }

    // ---- annexation: the realm's polygons grow, the annexed country's list empties, and the lands touch along identical segments
    {
        var e2 = new GameEngine(new SimulationService(75), new SaveService(saveFolder));
        var s2 = e2.State; var player = s2.PlayerNation; var persia = s2.OtherNations.First(n => n.Id == "persia");
        int home = player.Territory.Count, theirs = persia.Territory.Count, total = home + theirs;
        Warfare.AnnexNation(s2, player, persia);
        Check(player.Territory.Count == total && persia.Territory.Count == 0 && player.AnnexedPolygonCount == theirs, "annexing a country hands its polygons to the winner's realm and empties its own list (ownership data intact)");
        var homeKeys = new HashSet<string>(); var annexedKeys = new HashSet<string>();
        for (int r = 0; r < player.Territory.Count; r++) { var ring = player.Territory[r]; for (int i = 0; i < ring.Count - 1; i++) (r < home ? homeKeys : annexedKeys).Add(MgEdgeKey(ring[i], ring[i + 1])); }
        Check(homeKeys.Overlaps(annexedKeys), "the annexed land meets its new master along exactly the same segments, so the map can draw it as one realm without a border (the renderer hides edges shared by two polygons of one realm)");
        Check(s2.AnnexedBy(player.Id).Single().Id == "persia" && persia.AnnexedById == player.Id, "the annexed country is recorded under its master");
    }

    // ---- saves made under the old map: the map is redrawn, ownership is not touched
    {
        var e3 = new GameEngine(new SimulationService(76), new SaveService(saveFolder));
        var s3 = e3.State; var player = s3.PlayerNation; var persia = s3.OtherNations.First(n => n.Id == "persia"); var dutch = s3.OtherNations.First(n => n.Id == "dutch"); var den = s3.OtherNations.First(n => n.Id == "denmark");
        Warfare.AnnexNation(s3, player, persia);
        // make it look like a save from the stylised map: boxes for every realm, old capitals, old neutral/frontier shapes, no map edition
        List<List<MapPoint>> MgBox(double x, double y) => new() { new() { new(x, y), new(x + 20, y), new(x + 20, y + 20), new(x, y + 20), new(x, y) } };
        foreach (var n in s3.AllNations()) n.Territory = n.IsEliminated ? new() : MgBox(n.MapX, n.MapY);
        player.Territory.Add(new() { new(0, 0), new(5, 0), new(5, 5), new(0, 0) }); player.AnnexedPolygonCount = 1;   // (Persia's old land, appended)
        s3.NeutralRegions.RemoveAll(r => r.Name is "Greenland" or "Svalbard"); s3.NeutralRegions.First(r => r.Name == "Tibet").Polygons = MgBox(1100, 420);
        s3.FrontierRegions.RemoveAt(2);   // one frontier region was already colonised
        s3.FrontierRegions[0].Polygons = MgBox(40, 220);
        dutch.MapX = 307; dutch.MapY = 256; dutch.CapitalName = "Holland";   // stock capital: gets moved
        den.MapX = 500; den.MapY = 500; den.CapitalName = "Hafnia";         // edited in the save: stays
        s3.MapVersion = 0;
        await e3.SaveAsync(); await e3.LoadAsync();
        var up = e3.State;
        var upPlayer = up.PlayerNation; var upPersia = up.OtherNations.First(n => n.Id == "persia");
        Check(up.MapVersion == WorldGeometry.Version, "loading an old save brings its map up to the current edition");
        Check(upPlayer.Territory.Count == WorldGeometry.Nation(upPlayer.Id).Count + WorldGeometry.Nation("persia").Count && upPlayer.AnnexedPolygonCount == WorldGeometry.Nation("persia").Count && upPersia.Territory.Count == 0 && upPersia.IsEliminated && upPersia.AnnexedById == upPlayer.Id,
            "an annexed country's lands come back in its master's realm (home lands plus the annexed country's), who owns what unchanged");
        Check(up.OtherNations.Where(n => !n.IsEliminated).All(n => n.Territory.Count == WorldGeometry.Nation(n.Id).Count), "every other country gets its redrawn lands");
        Check(up.NeutralRegions.Count == 13 && up.NeutralRegions.All(r => r.Polygons.SelectMany(p => p).Count() == WorldGeometry.Neutral(r.Name).SelectMany(p => p).Count() && r.LabelX == WorldGeometry.Label(r.Name).X) && up.NeutralRegions.Any(r => r.Name == "Greenland"), "neutral regions are redrawn point for point and the new ones added");
        Check(up.FrontierRegions.Count == 2 && up.FrontierRegions.All(r => r.Polygons.SelectMany(p => p).Count() == WorldGeometry.Frontier(r.Name).SelectMany(p => p).Count()), "frontier regions still open are redrawn point for point; the colonised one stays gone");
        var upDutch = up.OtherNations.First(n => n.Id == "dutch"); var upDen = up.OtherNations.First(n => n.Id == "denmark");
        Check(upDutch.MapY == 267 && upDen.MapX == 500 && upDen.CapitalName == "Hafnia", "a stock capital that the map moved is moved in the save too, one the save had already changed is left alone");
        await e3.LoadAsync();
        Check(e3.State.MapVersion == WorldGeometry.Version && e3.State.PlayerNation.Territory.Count == upPlayer.Territory.Count, "(loading the same save again gives the same map)");
    }
}

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures;
