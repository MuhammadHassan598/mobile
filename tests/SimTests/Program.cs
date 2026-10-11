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
    Check(allTerr.Count >= 80, "80+ territory polygons across 41 nations");
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

    // 8c. Sue for peace.
    player5.Gold = 100_000;
    Check(engine5.SueForPeace(persia.Id) is null, "peace sued");
    Check(!persia.AtWarWithPlayer && persia.RelationToPlayer == -20, "peace ends war, relations -20");

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
    Check(march1.TotalDays >= Balance.MarchBaseDays, "march takes travel days based on distance");
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
    Check(player8.Population > popBefore, "winner absorbs the population");
    Check(player8.Farms > farmsBefore, "winner absorbs the farms");

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
    engine10c.SueForPeace(persiaC.Id);
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
        invaded = engine9.State.ActiveWarnings.Any(w => w.Contains("invading"));
    }
    Check(invaded, "a stronger AI invades the player by itself");
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
using (var engine12 = new GameEngine(new SimulationService(seed: 99), new SaveService(saveFolder)))
{
    var n12 = engine12.State.PlayerNation;
    for (int i = 0; i < 1825; i++) engine12.AdvanceOneDay();
    Check(!n12.IsEliminated, "a passive player keeps their country for 5 years");
    Check(n12.Population > 0, "population survives 5 years");
    Check(double.IsFinite(n12.Gold) && n12.Gold >= 0, "wealth stays sane for 5 years");
    Check(n12.Soldiers > 0, "the army survives 5 years");
    Check(!engine12.State.Defeated, "no accidental defeat in 5 quiet years");
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
    Check(win.GetGood("Wheat") == 50 && win.GetGood("Bread") == 7, "annexation moves the loser's food items");
    Check(win.GetProductionBuilding("farm") == 8 && win.GetProductionBuilding("bakery") == 2, "annexation moves the loser's mills (added to existing)");
    Check(Math.Abs(win.Wood - (woodBefore + loseWood)) < 0.001, "loser's wood counted once");
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
GameEngine NewDipEngine(int seed = 1) => new GameEngine(new SimulationService(seed), new SaveService(saveFolder));
Nation Ai(GameEngine eng, string id) => eng.State.OtherNations.First(x => x.Id == id);
void SetRating(Nation n, int rating) => n.RelationToPlayer = DiplomacyService.FromDisplayRating(rating);
int Rate(Nation n) => DiplomacyService.ToDisplayRating(n.RelationToPlayer);
int CountOf(UnitType type, Nation n) => n.Units.Where(u => u.Type == type).Sum(u => u.Count);
bool HasLog(GameEngine eng, string text) => eng.State.EventLog.Any(x => x.Contains(text));

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
    foreach (var m in eng.State.MarchingArmies) m.DaysLeft = 1;
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
    s.LogMovement(MovementKind.March, MovementStatus.Completed, spain, france, "c");   // neither side is the player

    Check(string.Join("", MovementReport.Events(s).Select(x => x.Text)) == "cba", "events are newest first, even within one day");
    Check(string.Join("", MovementReport.Events(s, persia.Id).Select(x => x.Text)) == "ba", "filtering by a state keeps only movements that involve it");
    Check(string.Join("", MovementReport.Events(s, spain.Id).Select(x => x.Text)) == "c" && MovementReport.Events(s, france.Id).Count == 1,
        "both sides of a movement between two other states see it");
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
    var allyMarch = TreatyService.LaunchMarch(s, spain, persia, 800)!;
    eng.BuyProduct(france.Id, "wheat", 1000);
    p.Warships = 5; p.Gold = 100_000; p.GoodsInventory["Wheat"] = 100_000;
    eng.FoundColony(s.FrontierRegions[0].Id);

    var all = MovementReport.ActiveMissions(s);
    Check(all.Count == 7, "embassy, spy network, missionaries, loan, march, shipment and expedition are all listed");
    var embassy = all.Single(x => x.Text.StartsWith("Embassy"));
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
    Check(byState.Single(x => x.StateId == persia.Id).Items.Count == 3 && byState.Sum(x => x.Items.Count) == 7, "missions group by where they are");

    // They leave the list once they are over.
    for (int i = 0; i < 8; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(!MovementReport.ActiveMissions(s).Any(x => x.Kind == MovementKind.Goods), "a delivered shipment leaves the mission list");
    for (int i = 8; i < Balance.TroopLoanDays; i++) { p.Gold = 1_000_000; eng.AdvanceOneDay(); }
    Check(s.MarchingArmies.Count == 0 && !MovementReport.ActiveMissions(s).Any(x => x.Kind == MovementKind.March), "a finished (here: recalled) march leaves the mission list");
    Check(!MovementReport.ActiveMissions(s).Any(x => x.Kind is MovementKind.Troops or MovementKind.Colony), "the loan and the expedition leave the list when they end");
    Check(MovementReport.ActiveMissions(s).Count(x => x.Text.StartsWith("Embassy") || x.Text.StartsWith("Spy") || x.Text.StartsWith("Missionaries")) == 3,
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

    s.Movements.Clear();
    var p = s.PlayerNation; var persia = Ai(eng, "persia");
    eng.GiveGift(persia.Id, 500);
    eng.EstablishEmbassy(persia.Id);
    var saved = s.Movements.Select(x => (x.Kind, x.Status, x.Date, x.FromId, x.ToId, x.Text)).ToList();
    await eng.SaveAsync();
    eng.State.Movements.Clear();
    await eng.LoadAsync();
    var loaded = eng.State.Movements.Where(x => !x.Text.Contains("Game saved") && !x.Text.Contains("Save loaded")).Select(x => (x.Kind, x.Status, x.Date, x.FromId, x.ToId, x.Text)).ToList();
    Check(saved.Count == 2 && loaded.SequenceEqual(saved), "movement records survive save / load unchanged");

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
    Check(eng.SubmitProposal("annexation_ban", persia.Id, 60, 30) is null, "proposal submitted");
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
    eng.SubmitProposal("production_ban", persia.Id, 60, 30);
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
    // A sovereignty guarantee is the same shield, and also binds the guarantor.
    var s = eng.State; var p = s.PlayerNation; var persia = Ai(eng, "persia"); var spain = Ai(eng, "spain");
    TreatyService.Add(s, TreatyType.SovereigntyGuarantee, p, persia, Balance.SovereigntyDays);
    var block = TreatyService.AnnexationBlock(s, persia.Id);
    Check(block is not null && block.Contains("guaranteed"), "a guaranteed country cannot be annexed");
    s.PendingVictories.Add(new VictoryDecision { LoserId = persia.Id, LoserName = persia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Annex).Message.Contains("guaranteed") && !persia.IsEliminated, "so a victory cannot be turned into an annexation of it");
    s.PendingVictories.Clear();
    var war = eng.DeclareWar(persia.Id);
    Check(war is not null && war.Contains("guaranteed") && !persia.AtWarWithPlayer, "the guarantor cannot declare war on the country it guaranteed");

    // Shield against a third party: an ally's army wins the battle but cannot annex the guaranteed country.
    persia.AtWarWithPlayer = true;
    persia.Units = UnitCatalog.SeedArmy(1200);
    p.Units = UnitCatalog.SeedArmy(2000);   // keeps the beaten AI from suing for peace, which would recall the march
    persia.MapX = spain.MapX + 40; persia.MapY = spain.MapY; persia.Population = spain.Population;
    AiWorldService.SetRelation(s, spain, persia, 30);   // (the ally covets its lands, so it would annex if it could)
    Check(AiWorldService.Motives(s, spain, persia).Aim == WarAim.Conquest, "(the ally's motive toward the guaranteed country is land)");
    var march = TreatyService.LaunchMarch(s, spain, persia, 2700)!;
    for (int i = 0; i < march.TotalDays; i++) { p.Gold = 1_000_000_000; eng.AdvanceOneDay(); }
    Check(persia.Soldiers < 1200, "the ally's army did fight the battle");
    Check(!persia.IsEliminated, "a third party cannot annex a guaranteed country either");
    Check(HasLog(eng, "cannot annex it"), "and the refusal is logged");
    Check(!s.Movements.Any(m => m.Kind == MovementKind.Gold && m.FromId == persia.Id && m.ToId == spain.Id),
        "nothing is plundered instead: a forbidden annexation does not turn into spoils");
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
    Check(Math.Abs(p.Gold - (gold + easterGold)) < 0.01 && p.Population > popBefore, "annex: its treasury and people become ours");
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
    p.Gold = 1_000_000;
    Check(eng.SueForPeace(nepal.Id) is null && eng.PendingVictory(nepal.Id) is null, "making peace closes the decision");

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
    s.PendingVictories.Add(new VictoryDecision { LoserId = persia.Id, LoserName = persia.Name, WonDate = s.CurrentDate, ExpiresDate = s.CurrentDate.AddDays(30) });
    Check(eng.ResolveVictory(persia.Id, VictoryChoice.Annex).Message.Contains("guaranteed"), "and it cannot be annexed");
    s.PendingVictories.Clear();
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
    // A conquest the Assembly or a guarantee forbids is not converted into plunder: the battle is won, the war goes on.
    var s = eng.State; var spain = Ai(eng, "spain"); var easter = Ai(eng, "easter");
    s.Wars.Add(new WarRecord { AggressorId = spain.Id, DefenderId = easter.Id, StartDate = s.CurrentDate, Aim = WarAim.Conquest });
    var guarantee = TreatyService.Add(s, TreatyType.SovereigntyGuarantee, s.PlayerNation, easter, 730);
    double w0 = spain.Gold, l0 = easter.Gold;
    VictoryService.ResolveForAi(s, spain, easter);
    Check(!easter.IsEliminated && HasLog(eng, "cannot annex it"), "a protected country is not annexed, and the refusal is logged");
    Check(spain.Gold == w0 && easter.Gold == l0 && AiWorldService.AtWar(s, spain.Id, easter.Id), "nothing is taken instead, and the war goes on");
    s.Treaties.Remove(guarantee);
    VictoryService.ResolveForAi(s, spain, easter);
    Check(easter.IsEliminated, "once the protection ends, the next victory annexes it");
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
    var seen = MovementReport.Events(s, persia.Id);
    Check(seen.Any(m => m.Text.Contains("declared war")) && MovementReport.EventsByState(s).Any(g => g.StateId == france.Id), "AI-to-AI events show in the Movement Report, under the country that acted");
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

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures;
