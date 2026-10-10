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
    // The Ottomans start ~30% short of every item, so deaths exceed births: a slow decline, not a collapse.
    Check(n.Population < pop0 && n.Population > pop0 * 0.85, $"population shrinks slowly under the starting shortage ({pop0:N0} -> {n.Population:N0})");
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
    foreach (var s in ConsumptionCatalog.All) n10.AddProduct(s.Item, 1e12);   // no shortage deaths: isolate growth
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
    Check(Math.Abs(ConsumptionService.Deaths(repA, 1_000_000) - 72) < 0.001, "50% food shortage at 1M people = 72 deaths/day");
    Check(Math.Abs(ConsumptionService.RatingDrop(repA) - 0.00005) < 1e-12, "50% food shortage = 0.00005 rating drop");

    // (b) every item 2% short: food 12 x 2 x 0.0001 + minerals 5 x 2 x 0.00003
    var rep2 = Report(2, 2);
    // 12 food x 2 x 0.000001 = 0.000024; 5 minerals x 2 x 0.0000003 = 0.000003 (user's 6-mineral example: 0.0000036 -> 0.0000276)
    Check(Math.Abs(ConsumptionService.RatingDrop(rep2) - (0.000024 + 0.000003)) < 1e-12, "all items 2% short = 0.000027 rating drop");
    Check(Math.Abs((12 * 2 * Balance.ShortageRatingDropFoodPerPct + 6 * 2 * Balance.ShortageRatingDropMineralPerPct) - 0.0000276) < 1e-12,
          "rates reproduce the 18-item example: 0.0000276/day");
    // 12 food x 2 x 1.44 + 5 minerals x 2 x 0.72 = 41.76 deaths per 1M people
    Check(Math.Abs(ConsumptionService.Deaths(rep2, 1_000_000) - 41.76) < 1e-6, "all items 2% short at 1M = 41.76 deaths/day");
    Check(Math.Abs(ConsumptionService.Deaths(rep2, 2_000_000) - 2 * ConsumptionService.Deaths(rep2, 1_000_000)) < 1e-9, "deaths double when population doubles");

    // (c) fractional deaths accumulate: one mineral 1% short at 600k people = 0.432/day
    var oneMineral = new ShortageReport();
    foreach (var s in ConsumptionCatalog.All) oneMineral.UnmetPct[s.Item] = s.Item == "Wood" ? 1 : 0;
    n18.Population = 600_000; n18.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n18, oneMineral);
    ConsumptionService.ApplyShortageEffects(n18, oneMineral);
    Check(n18.Population == 600_000, "0.432 death/day: nobody dies on days 1-2");
    ConsumptionService.ApplyShortageEffects(n18, oneMineral);
    Check(n18.Population == 599_999, "0.432 death/day: one person dies on day 3");

    // Balance point: births (Ottomans, Islam) = deaths at ~9.6% average shortage; above it the nation
    // shrinks, below it it grows, so a big shortage keeps a clearly negative net until it is closed.
    long popEq = 29_513_948;
    var islam = new Nation { Population = popEq, Religion = "Islam" };
    long birthsEq = PopulationService.DailyBirths(islam);
    double net29 = birthsEq - ConsumptionService.Deaths(Report(29, 29), popEq);
    double net5 = birthsEq - ConsumptionService.Deaths(Report(5, 5), popEq);
    double net10 = birthsEq - ConsumptionService.Deaths(Report(10, 10), popEq);
    Check(net29 < -10_000, $"29% short (screenshot case): net {net29:N0}/day stays strongly negative");
    Check(net10 < 0 && net10 > -1_500, $"10% short: net {net10:N0}/day, nearly balanced");
    Check(net5 > 0, $"5% short: net {net5:N0}/day, the population grows again");

    // (d) no shortage = no effect
    n18.RulerRating = 50; n18.Population = 1_000_000; n18.ShortageDeathCarry = 0;
    ConsumptionService.ApplyShortageEffects(n18, Report(0, 0));
    Check(n18.RulerRating == 50 && n18.Population == 1_000_000, "no shortage: no deaths, no rating drop");

    // (e) rating clamps at 0, population never negative
    n18.RulerRating = 0.001; n18.Population = 10; n18.ShortageDeathCarry = 50; // more deaths owed than people alive
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

    // Every nation, every item: output is the closest mill count to need x (1 - tier shortage).
    bool closest = true; string firstBad = "";
    foreach (var nat in all20)
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

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures;
