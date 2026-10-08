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
    Check(engine.State.PlayerNation.Territory.Count == 4, "4 territory shapes");
    Check(engine.Clock.Speed == GameSpeed.Paused, "clock paused initially");

    Console.WriteLine("== 2. 200-day simulation (covers the 6-month payday on 01-07-1600) ==");
    var n0 = engine.State.PlayerNation;
    long pop0 = n0.Population; double tre0 = n0.WealthInSilver; int sol0 = n0.Soldiers;
    for (int i = 0; i < 200; i++) engine.AdvanceOneDay();
    var n = engine.State.PlayerNation;
    Console.WriteLine($"  day 0:   date=01-01-1600 pop={pop0:N0} treasury={tre0:N0} soldiers={sol0:N0}");
    Console.WriteLine($"  day 200: date={engine.State.CurrentDate:dd-MM-yyyy} pop={n.Population:N0} treasury={n.WealthInSilver:N0} food={n.Food:N0} soldiers={n.Soldiers:N0}");
    Check(engine.State.CurrentDate == new DateOnly(1600, 7, 19), "date advanced to 19-07-1600");
    Check(n.WealthInSilver >= 0 && n.Food >= 0 && n.Population > 0, "no negative stocks, nation survives");
    Check(double.IsFinite(n.WealthInSilver) && double.IsFinite(n.Food), "stocks are finite numbers");
    Check(n.Population > pop0, "population grows with food surplus");
    Check(engine.State.EventLog.Any(e => e.Contains("paid army maintenance")), "first payday was paid and logged");

    Console.WriteLine("== 3. Save / load round-trip ==");
    double savedSilver = engine.State.PlayerNation.Silver;
    double savedGold = engine.State.PlayerNation.Gold;
    await engine.SaveAsync();
    Check(engine.HasSave, "save file exists");
    engine.State.PlayerNation.Silver = 1; // mutate...
    engine.State.PlayerNation.Gold = 2;
    await engine.LoadAsync();             // ...then restore
    Check(Math.Abs(engine.State.PlayerNation.Silver - savedSilver) < 0.01
        && Math.Abs(engine.State.PlayerNation.Gold - savedGold) < 0.01, "load restores saved purses");

    Console.WriteLine("== 4. Missed payday -> grace warning -> desertion ==");
    engine.NewGame();
    var p = engine.State.PlayerNation;
    int soldiersBefore = p.Soldiers;
    bool sawWarning = false;
    // Keep the nation broke all the way past the payday (day 182) + grace period.
    for (int i = 0; i < 196; i++)
    {
        p.Silver = 0; p.Gold = 0;
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
    Check(allTerr.Count == 92, "92 territory polygons across 41 nations");
    Check(engine2.State.AllNations().Count() == 41, "41 nations on the 1600 map");
    Check(allTerr.All(t => t.Count >= 3), "every territory polygon has >= 3 points");
    Check(allTerr.All(t => t.All(pt => pt.X >= 0 && pt.X <= 2200 && pt.Y >= 0 && pt.Y <= 1150)),
        "all polygon points inside the 2200x1150 viewBox");
    Check(engine2.State.AllNations().All(n => n.MapX > 0 && n.MapY > 0), "every nation has a map anchor");
    Check(engine2.State.AllNations().All(n => !string.IsNullOrEmpty(n.CapitalName)), "every nation has a capital");
    Check(engine2.State.NeutralRegions.Count > 0, "neutral territories drawn on the map");
}

Console.WriteLine("== 6. Economy: construction, chains, trade ==");
using (var engine3 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n3 = engine3.State.PlayerNation;
    int farmsBefore = n3.Farms;
    double goldBefore = n3.WealthInSilver;

    // 6a. Build a farm: cost deducted upfront, completes after 5 days.
    string? err = engine3.StartConstruction(BuildingType.Farm);
    Check(err is null, "farm construction accepted");
    Check(Math.Abs(n3.WealthInSilver - (goldBefore - 100)) < 0.01, "farm cost deducted upfront");
    Check(n3.ConstructionQueue.Count == 1, "project queued");
    for (int i = 0; i < 5; i++) engine3.AdvanceOneDay();
    Check(n3.Farms == farmsBefore + 1, "farm completed after 5 days");
    Check(n3.ConstructionQueue.Count == 0, "queue empty after completion");
    Check(engine3.State.EventLog.Any(e => e.Contains("Farm completed")), "completion logged");

    // 6b. Unaffordable build is rejected.
    n3.Silver = 0; n3.Gold = 0;
    string? err2 = engine3.StartConstruction(BuildingType.Mine);
    Check(err2 is not null, "broke build rejected with error");
    Check(n3.ConstructionQueue.Count == 0, "nothing queued when broke");

    // 6c. Workshop chain: wood + iron -> goods.
    n3.Silver = 5000; n3.Gold = 0; n3.Wood = 50; n3.Iron = 30;
    string? err3 = engine3.StartConstruction(BuildingType.Workshop);
    Check(err3 is null, "workshop construction accepted (had wood+iron)");
    for (int i = 0; i < 12; i++) engine3.AdvanceOneDay();
    Check(n3.Workshops == 1, "workshop completed after 12 days");
    // Isolate the chain: remove mines/sawmills so stocks move only via the workshop.
    n3.Mines = 0; n3.Sawmills = 0;
    n3.Wood = 50; n3.Iron = 30;
    double woodBefore = n3.Wood, ironBefore = n3.Iron, goodsBefore = n3.Goods;
    for (int i = 0; i < 3; i++) engine3.AdvanceOneDay();
    Check(n3.Goods > goodsBefore, "workshop produced goods");
    Check(Math.Abs(n3.Wood - (woodBefore - 6)) < 0.01 && Math.Abs(n3.Iron - (ironBefore - 3)) < 0.01,
        "workshop consumed 2 wood + 1 iron per day");

    // 6d. Selling goods converts to gold.
    n3.Goods = 10;
    double tBefore = n3.WealthInSilver;
    engine3.SellGoods();
    Check(n3.Goods == 0, "goods stockpile emptied by sale");
    Check(Math.Abs(n3.WealthInSilver - (tBefore + 150)) < 0.01, "sold 10 goods for 150 Silver");

    // 6e. 60-day economy run: stocks stay sane.
    for (int i = 0; i < 60; i++) engine3.AdvanceOneDay();
    Check(n3.Wood >= 0 && n3.Iron >= 0 && n3.Goods >= 0 && n3.WealthInSilver >= 0, "no negative stocks after 60 days");
}

Console.WriteLine("== 7. Military: recruitment, commanders, maintenance ==");
using (var engine4 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n4 = engine4.State.PlayerNation;

    // 7a. Recruit musketeers: gold deducted, stack grows.
    int muskBefore = n4.Units.First(u => u.Type == UnitType.Musketeer).Count;
    double goldBefore = n4.WealthInSilver;
    string? rerr = engine4.Recruit(UnitType.Musketeer, 100);
    Check(rerr is null, "recruit 100 musketeers accepted");
    Check(n4.Units.First(u => u.Type == UnitType.Musketeer).Count == muskBefore + 100, "musketeer stack grew by 100");
    Check(Math.Abs(n4.WealthInSilver - (goldBefore - 2000)) < 0.01, "recruit cost 2000 Silver deducted");

    // 7b. Cannon costs iron too; broke recruit rejected.
    n4.Iron = 100;
    string? rerr2 = engine4.Recruit(UnitType.Cannon, 10);
    Check(rerr2 is null && Math.Abs(n4.Iron - 50) < 0.01, "10 cannons consumed 50 iron");
    n4.Silver = 0; n4.Gold = 0;
    string? rerr3 = engine4.Recruit(UnitType.Musketeer, 100);
    Check(rerr3 is not null, "broke recruit rejected");

    // 7c. Upkeep accrues per unit type (fresh engine, 10 days, no commanders).
    using (var eng = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
    {
        for (int i = 0; i < 10; i++) eng.AdvanceOneDay();
        // 4400*0.03 + 2400*0.025 + 1040*0.06 + 160*0.15 (land) + 25*0.5 (naval), per day x10
        double expected = (132 + 60 + 62.4 + 24 + 12.5) * 10;
        Check(Math.Abs(eng.State.PlayerNation.UpkeepAccrued - expected) < 5, $"upkeep accrued ~{expected:N0} over 10 days");
    }

    // 7d. PayMaintenance resets the cycle.
    for (int i = 0; i < 30; i++) engine4.AdvanceOneDay();
    n4.Silver = 100_000; n4.Gold = 0;
    double accrued = n4.UpkeepAccrued;
    string? perr = engine4.PayMaintenance();
    Check(perr is null, "early maintenance payment accepted");
    Check(n4.UpkeepAccrued == 0, "accrual reset after payment");
    Check(n4.NextPayday == engine4.State.CurrentDate.AddDays(180), "payday pushed 180 days out");
    Check(Math.Abs(n4.WealthInSilver - (100_000 - accrued)) < 0.01, "treasury reduced by accrued amount");

    // 7e. Hire land commander: cheaper upkeep afterwards.
    n4.Silver = 100_000; n4.Gold = 0;
    string? herr = engine4.HireCommander(CommanderRole.LandCommander);
    Check(herr is null && n4.HasCommander(CommanderRole.LandCommander), "land commander hired");
    Check(Math.Abs(n4.WealthInSilver - (100_000 - 1200)) < 0.01, "hire cost 1200 Silver deducted");
    n4.UpkeepAccrued = 0;
    for (int i = 0; i < 10; i++) engine4.AdvanceOneDay();
    double withCommander = n4.UpkeepAccrued; // (278.4*0.85 + 12.5) * 10 + wages 2*10
    Check(withCommander < 2909, $"commander reduces upkeep ({withCommander:N0} < 2909)");
    engine4.DismissCommander(CommanderRole.LandCommander);
    Check(!n4.HasCommander(CommanderRole.LandCommander), "commander dismissed");

    // 7f. Warships cost wood + iron.
    n4.Silver = 100_000; n4.Gold = 0; n4.Wood = 500; n4.Iron = 200;
    int shipsBefore = n4.Warships;
    string? werr = engine4.RecruitWarships(10);
    Check(werr is null && n4.Warships == shipsBefore + 10, "10 warships launched");
    Check(Math.Abs(n4.Wood - 300) < 0.01 && Math.Abs(n4.Iron - 100) < 0.01, "warships consumed 200 wood + 100 iron");

    // 7g. Desertion drains stacks consistently.
    engine4.NewGame();
    var p4 = engine4.State.PlayerNation;
    int totalBefore = p4.Soldiers;
    for (int i = 0; i < 196; i++) { p4.Silver = 0; p4.Gold = 0; engine4.AdvanceOneDay(); }
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
    double g0 = player5.WealthInSilver;
    Check(engine5.SendGift(persia.Id) is null, "gift accepted");
    Check(Math.Abs(persia.RelationToPlayer - 10) < 0.01, "gift +10 relations");
    Check(Math.Abs(player5.WealthInSilver - (g0 - 500)) < 0.01, "gift cost 500 Silver");

    // 8b. Trade pact pays daily income; war breaks it.
    Check(engine5.SignTradePact(persia.Id) is null, "trade pact signed");
    double t0 = player5.WealthInSilver;
    engine5.AdvanceOneDay();
    Check(player5.WealthInSilver > t0 + 40, "pact pays daily income");
    Check(engine5.DeclareWar(persia.Id) is null, "war declared");
    Check(persia.AtWarWithPlayer && !persia.HasTradePactWithPlayer, "war breaks the pact");
    Check(persia.RelationToPlayer == -100, "war sets relations to -100");

    // 8c. Sue for peace.
    player5.Silver = 100_000; player5.Gold = 0;
    Check(engine5.SueForPeace(persia.Id) is null, "peace sued");
    Check(!persia.AtWarWithPlayer && persia.RelationToPlayer == -20, "peace ends war, relations -20");

    // 8d. Tribute: paid when strong, refused when weak.
    engine5.Recruit(UnitType.Musketeer, 1000); // ~7000 soldiers vs Persia's 4500
    double pt0 = player5.WealthInSilver, et0 = persia.WealthInSilver;
    Check(engine5.DemandTribute(persia.Id) is null, "tribute demanded");
    Check(player5.WealthInSilver > pt0 && persia.WealthInSilver < et0, "tribute transferred to player");
    double mrel = mughal.RelationToPlayer;
    engine5.DemandTribute(mughal.Id); // 7000 vs 7000 -> refused
    Check(mughal.RelationToPlayer < mrel, "refused demand hurts relations");

    // 8e. Espionage: establish, grow, steal.
    Check(engine5.EstablishNetwork(persia.Id) is null, "spy network established");
    var net = engine5.GetNetwork(persia.Id)!;
    Check(net.Strength == 20, "network starts at strength 20");
    for (int i = 0; i < 15; i++) engine5.AdvanceOneDay();
    Check(net.Strength == 35, "network grows +1/day");
    double pg0 = player5.WealthInSilver, eg1 = persia.WealthInSilver;
    Check(engine5.SpySteal(persia.Id) is null, "steal executed");
    double stolen = player5.WealthInSilver - pg0;
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
        engine6.State.PlayerNation.Silver = 1_000_000; // stay solvent, isolate the war logic
        engine6.AdvanceOneDay();
        warDeclared = engine6.State.OtherNations.Any(n => n.AtWarWithPlayer);
    }
    Check(warDeclared, "furious AI declares war by itself");
    Check(engine6.State.ActiveWarnings.Any(w => w.Contains("DECLARED WAR")), "war declaration warns the player");
}

Console.WriteLine("== 9. Period currency: Silver and Gold ==");
using (var engine7 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n7 = engine7.State.PlayerNation;
    Check(Math.Abs(n7.WealthInSilver - 6200) < 0.01, "starting wealth is 6200 silver-equivalent");

    Check(engine7.ExchangeSilverForGold(500) is null, "exchange to gold accepted");
    Check(Math.Abs(n7.Silver - 4500) < 0.01 && Math.Abs(n7.Gold - 17) < 0.01,
        "500 Silver became 5 Gold");

    Check(engine7.ExchangeGoldForSilver(5) is null, "exchange to silver accepted");
    Check(Math.Abs(n7.Silver - 5000) < 0.01 && Math.Abs(n7.Gold - 12) < 0.01,
        "5 Gold became 500 Silver");

    // Auto-convert: payment larger than the silver purse dips into gold.
    n7.Silver = 50;
    Check(engine7.Recruit(UnitType.Musketeer, 10) is null, "recruit succeeds via gold conversion");
    Check(Math.Abs(n7.Silver - 50) < 0.01 && Math.Abs(n7.Gold - 10) < 0.01,
        "gold auto-converted to cover the 200 Silver payment");

    // Display forms.
    Check(Currency.Format(5230) == "52 Gold · 30 Silver", "wealth formats as Gold + Silver");
    Check(Currency.Format(99) == "99 Silver", "small amounts stay in Silver");
    Check(Currency.Cost(1200) == "12 Gold", "round gold costs show as Gold");
    Check(Currency.Cost(550) == "550 Silver", "silver costs show as Silver");
}

Console.WriteLine("== 10. Warfare: marches, battles, whole-country annexation ==");
using (var engine8 = new GameEngine(new SimulationService(seed: 11), new SaveService(saveFolder)))
{
    var player8 = engine8.State.PlayerNation;
    var kazakh = engine8.State.OtherNations.First(n => n.Name == "Kazakh Khanate");

    var (ok0, _) = engine8.LaunchInvasion(kazakh.Id, 1000);
    Check(!ok0, "invasion refused without a declaration of war");

    engine8.DeclareWar(kazakh.Id);
    player8.Silver = 1_000_000;
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
        engine9.State.PlayerNation.Silver = 1_000_000;
        engine9.AdvanceOneDay();
        invaded = engine9.State.ActiveWarnings.Any(w => w.Contains("invading"));
    }
    Check(invaded, "a stronger AI invades the player by itself");
}

Console.WriteLine("== 11. Laws & religion ==");
using (var engine10 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var n10 = engine10.State.PlayerNation;
    n10.Silver = 100_000;

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
    n11.Silver = 100_000;
    n11.Food = 100_000;
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

Console.WriteLine("== 13. Balance pass: 5-year autoplay + victory/defeat ==");
using (var engine12 = new GameEngine(new SimulationService(seed: 99), new SaveService(saveFolder)))
{
    var n12 = engine12.State.PlayerNation;
    for (int i = 0; i < 1825; i++) engine12.AdvanceOneDay();
    Check(!n12.IsEliminated, "a passive player keeps their country for 5 years");
    Check(n12.Population > 0, "population survives 5 years");
    Check(double.IsFinite(n12.WealthInSilver) && n12.WealthInSilver >= 0, "wealth stays sane for 5 years");
    Check(n12.Soldiers > 0, "the army survives 5 years");
    Check(!engine12.State.Defeated, "no accidental defeat in 5 quiet years");
}

using (var engine13 = new GameEngine(new SimulationService(), new SaveService(saveFolder)))
{
    var p13 = engine13.State.PlayerNation;
    foreach (var other in engine13.State.OtherNations.Where(n => !n.IsEliminated).Take(8).ToList())
        Warfare.AnnexNation(engine13.State, p13, other);
    Check(engine13.State.NationsAnnexedByPlayer == 8, "test setup: player annexed 8 nations");
    engine13.AdvanceOneDay();
    Check(engine13.State.VictoryAchieved, "hegemony triggers victory");

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

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures;
