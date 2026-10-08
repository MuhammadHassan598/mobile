using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The single entry point the UI talks to. Owns the game state, the clock
/// and the simulation; raises <see cref="StateChanged"/> after every tick
/// so Blazor components can re-render (via InvokeAsync on the UI thread).
/// Registered as a singleton in MauiProgram.
/// </summary>
public sealed class GameEngine : IDisposable
{
    private readonly SimulationService _sim;
    private readonly SaveService _save;
    private bool _disposed;

    public GameClock Clock { get; } = new();
    public GameState State { get; private set; }

    /// <summary>Raised after every simulated day and after load/new-game.</summary>
    public event Action? StateChanged;

    public GameEngine(SimulationService sim, SaveService save)
    {
        _sim = sim;
        _save = save;
        State = GameState.NewGame();
        CampaignChosen = false;
        Clock.DayElapsed += OnDayElapsed;
    }

    /// <summary>False until the player picks a nation on the start screen.</summary>
    public bool CampaignChosen { get; private set; }

    /// <summary>Starts a campaign as the chosen nation.</summary>
    public void StartCampaign(string nationId)
    {
        Clock.SetSpeed(GameSpeed.Paused);
        State = GameState.NewGame(nationId);
        CampaignChosen = true;
        StateChanged?.Invoke();
    }

    /// <summary>Returns to the nation-select screen.</summary>
    public void AbandonToMenu()
    {
        Clock.SetSpeed(GameSpeed.Paused);
        CampaignChosen = false;
        StateChanged?.Invoke();
    }

    public void NewGame()
    {
        StartCampaign("ottoman");
    }

    public async Task SaveAsync()
    {
        await _save.SaveAsync(State);
        State.Log("Game saved.");
        StateChanged?.Invoke();
    }

    public async Task LoadAsync()
    {
        var loaded = await _save.LoadAsync();
        if (loaded is not null)
        {
            Clock.SetSpeed(GameSpeed.Paused);
            State = loaded;
            CampaignChosen = true;
            State.Log("Save loaded.");
            StateChanged?.Invoke();
        }
    }

    public bool HasSave => _save.HasSave;

    /// <summary>Estimated daily tax income, shown on the nation-select screen.</summary>
    public static long EstimateDailyIncome(Nation n) =>
        (long)(n.Population * Balance.TaxPerPersonPerDay + Balance.CrownDomainIncomePerDay);

    /// <summary>Whether the player can afford a building right now.</summary>
    public bool CanAfford(BuildingSpec spec)
    {
        var n = State.PlayerNation;
        return n.WealthInSilver >= spec.GoldCost
            && n.Wood >= spec.WoodCost
            && n.Iron >= spec.IronCost;
    }

    /// <summary>
    /// Starts construction of a building in one of the player's provinces.
    /// Costs are paid upfront. Returns an error message, or null on success.
    /// </summary>
    public string? StartConstruction(string provinceId, BuildingType type)
    {
        var nation = State.PlayerNation;
        var province = nation.Provinces.FirstOrDefault(p => p.Id == provinceId);
        if (province is null) return "Province not found.";

        var spec = BuildingCatalog.Get(type);

        if (nation.ConstructionQueue.Count(q => q.ProvinceId == provinceId) >= Balance.MaxQueuePerProvince)
            return $"Build queue is full in {province.Name} (max {Balance.MaxQueuePerProvince}).";

        if (!CanAfford(spec)) return "Not enough resources.";

        nation.PaySilver(spec.GoldCost);
        nation.Wood -= spec.WoodCost;
        nation.Iron -= spec.IronCost;
        nation.ConstructionQueue.Add(new ConstructionProject
        {
            ProvinceId = province.Id,
            ProvinceName = province.Name,
            Building = type,
            DaysLeft = spec.BuildDays,
            TotalDays = spec.BuildDays,
        });
        State.Log($"Started building {spec.Name} in {province.Name} ({spec.BuildDays} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Sells all stockpiled goods for gold (the player's meaningful trade action).</summary>
    public void SellGoods()
    {
        var n = State.PlayerNation;
        if (n.Goods <= 0) return;
        double gold = n.Goods * Balance.GoodsSellPrice;
        State.Log($"Sold {n.Goods:N0} goods for {Currency.Cost(gold)}.");
        n.Silver += gold;
        n.Goods = 0;
        StateChanged?.Invoke();
    }

    /// <summary>Converts Silver into Gold (100:1). Returns an error, or null.</summary>
    public string? ExchangeSilverForGold(double silverAmount)
    {
        var n = State.PlayerNation;
        double convertible = Math.Floor(Math.Min(n.Silver, silverAmount) / Currency.SilverPerGold)
            * Currency.SilverPerGold;
        if (convertible < Currency.SilverPerGold)
            return $"Need at least {Currency.SilverPerGold:N0} Silver.";
        n.Silver -= convertible;
        n.Gold += convertible / Currency.SilverPerGold;
        State.Log($"Exchanged {Currency.Cost(convertible)} into Gold.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Converts Gold back into Silver. Returns an error, or null.</summary>
    public string? ExchangeGoldForSilver(double goldAmount)
    {
        var n = State.PlayerNation;
        if (goldAmount <= 0 || n.Gold < goldAmount) return "Not enough Gold.";
        n.Gold -= goldAmount;
        n.Silver += goldAmount * Currency.SilverPerGold;
        State.Log($"Exchanged {goldAmount:N0} Gold into Silver.");
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Warfare ----------------

    /// <summary>
    /// Launches an invasion: the army marches to the target province and the
    /// battle resolves on arrival. Returns (ok, message): an error, or a
    /// march confirmation.
    /// </summary>
    public (bool ok, string message) LaunchInvasion(string provinceId, int commitCount)
    {
        var player = State.PlayerNation;
        Nation? owner = null;
        Province? province = null;
        foreach (var n in State.OtherNations)
        {
            province = n.Provinces.FirstOrDefault(p => p.Id == provinceId);
            if (province is not null) { owner = n; break; }
        }
        if (province is null || owner is null) return (false, "Province not found.");
        if (owner.IsEliminated) return (false, "That nation no longer exists.");
        if (!owner.AtWarWithPlayer) return (false, "You must declare war first.");
        if (player.Soldiers < Balance.MinInvasionForce)
            return (false, $"Need at least {Balance.MinInvasionForce} soldiers to invade.");

        int commit = Math.Min(commitCount, player.Soldiers);
        var force = ArmyHelper.ExtractSoldiers(player, commit);
        var from = player.Provinces.First();
        int days = Warfare.TravelDays(from, province);
        State.MarchingArmies.Add(new MarchingArmy
        {
            AttackerNationId = player.Id,
            AttackerNationName = player.Name,
            TargetProvinceId = province.Id,
            TargetProvinceName = province.Name,
            TargetNationId = owner.Id,
            TargetNationName = owner.Name,
            Force = force,
            DaysLeft = days,
            TotalDays = days,
        });

        State.Log($"⚔ {commit:N0} soldiers march on {province.Name} ({owner.Name}) — arrival in {days} days.");
        StateChanged?.Invoke();
        return (true, $"Your army marches on {province.Name} — arrival in {days} days.");
    }

    // ---------------- Laws & religion ----------------

    public string? ToggleEdict(EdictType edict)
    {
        var n = State.PlayerNation;
        if (n.HasEdict(edict))
        {
            n.ActiveEdicts.Remove(edict);
            State.Log($"Repealed {EdictCatalog.Get(edict).Name}.");
        }
        else
        {
            if (!n.CanPay(Balance.EdictEnactCost)) return "Not enough Silver.";
            n.PaySilver(Balance.EdictEnactCost);
            n.ActiveEdicts.Add(edict);
            State.Log($"Enacted {EdictCatalog.Get(edict).Name}.");
        }
        StateChanged?.Invoke();
        return null;
    }

    public string? SetStance(ReligiousStance stance)
    {
        var n = State.PlayerNation;
        if (n.Stance == stance) return null;
        if (!n.CanPay(Balance.StanceChangeCost)) return "Not enough Silver.";
        n.PaySilver(Balance.StanceChangeCost);
        n.Stance = stance;
        State.Log($"The court adopts a {stance.ToString().ToLower()} religious stance.");
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Colonisation ----------------

    /// <summary>Sends a colony expedition to an uncharted region.</summary>
    public string? FoundColony(string regionId)
    {
        var region = State.FrontierRegions.FirstOrDefault(r => r.Id == regionId);
        if (region is null) return "Region not found.";
        if (State.ActiveExpedition is not null) return "An expedition is already at sea.";
        var n = State.PlayerNation;
        if (n.Warships < Balance.ColonyWarshipsRequired)
            return $"Need {Balance.ColonyWarshipsRequired} warships to carry the colonists.";
        if (!n.CanPay(Balance.ColonyCostSilver)) return "Not enough Silver.";
        if (n.Food < Balance.ColonyCostFood) return "Not enough food for the voyage.";
        if (n.Population < Balance.ColonyColonists) return "Not enough people to spare.";

        n.PaySilver(Balance.ColonyCostSilver);
        n.Food -= Balance.ColonyCostFood;
        n.Population -= Balance.ColonyColonists;
        State.ActiveExpedition = new ColonyExpedition
        {
            RegionId = region.Id,
            RegionName = region.Name,
            DaysLeft = Balance.ColonyDays,
            TotalDays = Balance.ColonyDays,
        };
        State.Log($"A colony expedition sails for {region.Name} ({Balance.ColonyDays} days).");
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Military ----------------

    /// <summary>Recruits land units instantly. Returns an error message, or null on success.</summary>
    public string? Recruit(UnitType type, int count)
    {
        if (count <= 0) return "Invalid count.";
        var spec = UnitCatalog.Get(type);
        var n = State.PlayerNation;

        double mult = n.HasCommander(CommanderRole.LandCommander) ? Balance.LandCommanderRecruitMult : 1.0;
        double gold = spec.GoldCost * mult * count;
        double wood = spec.WoodCost * count;
        double iron = spec.IronCost * count;
        if (!n.CanPay(gold) || n.Wood < wood || n.Iron < iron)
            return "Not enough resources.";

        n.PaySilver(gold);
        n.Wood -= wood;
        n.Iron -= iron;
        var stack = n.Units.FirstOrDefault(u => u.Type == type);
        if (stack is null) n.Units.Add(new UnitStack { Type = type, Count = count });
        else stack.Count += count;

        State.Log($"Recruited {count:N0} {spec.Name} for {Currency.Cost(gold)}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Builds warships instantly. Returns an error message, or null on success.</summary>
    public string? RecruitWarships(int count)
    {
        if (count <= 0) return "Invalid count.";
        var n = State.PlayerNation;

        double mult = n.HasCommander(CommanderRole.FleetCommander) ? Balance.FleetCommanderRecruitMult : 1.0;
        double gold = WarshipSpec.GoldCost * mult * count;
        double wood = WarshipSpec.WoodCost * count;
        double iron = WarshipSpec.IronCost * count;
        if (!n.CanPay(gold) || n.Wood < wood || n.Iron < iron)
            return "Not enough resources.";

        n.PaySilver(gold);
        n.Wood -= wood;
        n.Iron -= iron;
        n.Warships += count;

        State.Log($"Launched {count:N0} warships for {Currency.Cost(gold)}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Hires a commander for a vacant role. Returns an error message, or null on success.</summary>
    public string? HireCommander(CommanderRole role)
    {
        var n = State.PlayerNation;
        if (n.HasCommander(role)) return "Role already filled.";
        var spec = CommanderCatalog.GetRole(role);
        if (!n.CanPay(spec.HireCost)) return "Not enough Silver.";

        n.PaySilver(spec.HireCost);
        n.Commanders.Add(new Commander
        {
            Name = CommanderCatalog.NextCandidateName(role, n.Commanders),
            Role = role,
            DailyWage = spec.DailyWage,
        });
        State.Log($"Hired {n.Commanders.Last().Name} as {spec.Title}.");
        StateChanged?.Invoke();
        return null;
    }

    public void DismissCommander(CommanderRole role)
    {
        var n = State.PlayerNation;
        var cmd = n.Commanders.FirstOrDefault(c => c.Role == role);
        if (cmd is null) return;
        n.Commanders.Remove(cmd);
        State.Log($"Dismissed {cmd.Name}.");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Pays accrued army maintenance immediately and restarts the 180-day
    /// cycle. Returns an error message, or null on success.
    /// </summary>
    public string? PayMaintenance()
    {
        var n = State.PlayerNation;
        if (n.UpkeepAccrued <= 0) return "Nothing due.";
        if (!n.CanPay(n.UpkeepAccrued)) return "Not enough Silver in the treasury.";

        n.PaySilver(n.UpkeepAccrued);
        State.Log($"Paid army maintenance early: {Currency.Format(n.UpkeepAccrued)}.");
        n.UpkeepAccrued = 0;
        n.GraceDaysLeft = 0;
        n.NextPayday = State.CurrentDate.AddDays(Balance.PaydayIntervalDays);
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Days until the next maintenance payday (negative = overdue).</summary>
    public int DaysToPayday =>
        State.PlayerNation.NextPayday.DayNumber - State.CurrentDate.DayNumber;

    // ---------------- Diplomacy ----------------

    private Nation? FindNation(string nationId) =>
        State.OtherNations.FirstOrDefault(n => n.Id == nationId);

    /// <summary>Sends a gift: +relations for gold. Returns an error, or null on success.</summary>
    public string? SendGift(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "You are at war — they refuse your gift.";
        if (!State.PlayerNation.CanPay(Balance.GiftCost)) return "Not enough Silver.";

        State.PlayerNation.PaySilver(Balance.GiftCost);
        n.RelationToPlayer = Math.Min(100, n.RelationToPlayer + Balance.GiftRelationGain);
        State.Log($"Sent a gift to {n.Name} (+{Balance.GiftRelationGain} relations).");
        StateChanged?.Invoke();
        return null;
    }

    public string? DeclareWar(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "Already at war.";

        n.AtWarWithPlayer = true;
        n.HasTradePactWithPlayer = false;
        n.RelationToPlayer = -100;
        State.Log($"You declared war on {n.Name}!");
        StateChanged?.Invoke();
        return null;
    }

    public string? SueForPeace(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (!n.AtWarWithPlayer) return "You are not at war.";
        if (!State.PlayerNation.CanPay(Balance.PeaceTributeCost)) return "Not enough Silver for tribute.";

        State.PlayerNation.PaySilver(Balance.PeaceTributeCost);
        n.AtWarWithPlayer = false;
        n.RelationToPlayer = -20;
        State.Log($"You sued for peace with {n.Name} (tribute {Balance.PeaceTributeCost:N0} gold).");
        StateChanged?.Invoke();
        return null;
    }

    public string? SignTradePact(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "Cannot trade while at war.";
        if (n.HasTradePactWithPlayer) return "Pact already signed.";
        if (n.RelationToPlayer < 0) return "Relations too poor — send gifts first.";
        if (!State.PlayerNation.CanPay(Balance.TradePactFee)) return "Not enough Silver.";

        State.PlayerNation.PaySilver(Balance.TradePactFee);
        n.HasTradePactWithPlayer = true;
        State.Log($"Signed a trade pact with {n.Name}.");
        StateChanged?.Invoke();
        return null;
    }

    public void CancelTradePact(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null || !n.HasTradePactWithPlayer) return;
        n.HasTradePactWithPlayer = false;
        State.Log($"Cancelled the trade pact with {n.Name}.");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Demands tribute. Paid if your army is 1.5x theirs; otherwise relations
    /// suffer and they may declare war. Returns an error, or null on success.
    /// </summary>
    public string? DemandTribute(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "You are already at war.";

        var player = State.PlayerNation;
        if (player.Soldiers > n.Soldiers * Balance.TributeArmyRatio)
        {
            double tribute = Math.Min(n.WealthInSilver,
                Math.Max(100, n.WealthInSilver * Balance.TributeFraction));
            n.PaySilver(tribute);
            player.Silver += tribute;
            n.RelationToPlayer = Math.Max(-100, n.RelationToPlayer - 20);
            State.Log($"{n.Name} paid tribute: {Currency.Cost(tribute)}.");
        }
        else
        {
            n.RelationToPlayer = Math.Max(-100, n.RelationToPlayer - 30);
            State.Log($"{n.Name} refused your tribute demand.");
            if (_sim.RollChance(Balance.TributeRefusalWarChance))
            {
                n.AtWarWithPlayer = true;
                n.HasTradePactWithPlayer = false;
                n.RelationToPlayer = -100;
                State.Log($"{n.Name} declared war over your insult!");
                State.ActiveWarnings.Add($"⚠ {n.Name} has DECLARED WAR on you!");
            }
        }
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Espionage ----------------

    public SpyNetwork? GetNetwork(string nationId) =>
        State.SpyNetworks.FirstOrDefault(s => s.TargetNationId == nationId);

    public string? EstablishNetwork(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        var net = GetNetwork(nationId);
        if (net is not null && net.Strength >= Balance.MaxNetworkStrength)
            return "Network already at full strength.";
        if (!State.PlayerNation.CanPay(Balance.EstablishNetworkCost)) return "Not enough Silver.";

        State.PlayerNation.PaySilver(Balance.EstablishNetworkCost);
        if (net is null)
            State.SpyNetworks.Add(new SpyNetwork
            {
                TargetNationId = n.Id,
                TargetNationName = n.Name,
                Strength = Balance.EstablishNetworkStrength,
            });
        else
            net.Strength = Math.Min(Balance.MaxNetworkStrength,
                net.Strength + Balance.EstablishNetworkStrength);
        State.Log($"Spy network operating in {n.Name}.");
        StateChanged?.Invoke();
        return null;
    }

    private string? SpendNetwork(string nationId, int minStrength, int cost,
        out SpyNetwork? net, out Nation? target)
    {
        net = null;
        target = FindNation(nationId);
        if (target is null) return "Nation not found.";
        net = GetNetwork(nationId);
        if (net is null || net.Strength < minStrength)
            return $"Need a spy network of strength {minStrength}.";
        net.Strength -= cost;
        return null;
    }

    private void Discover(SpyNetwork net, Nation target, string deed)
    {
        net.Strength /= 2;
        target.RelationToPlayer = Math.Max(-100,
            target.RelationToPlayer - Balance.DiscoveryRelationHit);
        State.Log($"Our spy was caught ({deed}) in {target.Name}!");
        State.ActiveWarnings.Add($"🕵 Our spy was caught in {target.Name}!");
    }

    /// <summary>Steals 5–15% of the target's treasury. 25% discovery risk.</summary>
    public string? SpySteal(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.StealMinStrength, Balance.StealStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        double frac = Balance.StealFractionMin
            + _sim.NextDouble() * (Balance.StealFractionMax - Balance.StealFractionMin);
        double amount = target!.WealthInSilver * frac;
        target.PaySilver(amount);
        State.PlayerNation.Silver += amount;
        State.Log($"Spies stole {Currency.Cost(amount)} from {target.Name}.");
        if (_sim.RollChance(Balance.StealDiscoveryChance))
            Discover(net!, target, "theft");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Destroys one random building in the target nation. 30% discovery risk.</summary>
    public string? SpySabotage(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.SabotageMinStrength, Balance.SabotageStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        var provinces = target!.Provinces
            .Where(p => p.Farms + p.Mines + p.Sawmills + p.Workshops > 0).ToList();
        if (provinces.Count == 0)
        {
            net!.Strength += Balance.SabotageStrengthCost; // refund
            return "No buildings to sabotage.";
        }
        var prov = provinces[_sim.NextInt(provinces.Count)];

        var present = new List<(BuildingType type, string name)>();
        if (prov.Farms > 0) present.Add((BuildingType.Farm, "Farm"));
        if (prov.Mines > 0) present.Add((BuildingType.Mine, "Mine"));
        if (prov.Sawmills > 0) present.Add((BuildingType.Sawmill, "Sawmill"));
        if (prov.Workshops > 0) present.Add((BuildingType.Workshop, "Workshop"));
        var pick = present[_sim.NextInt(present.Count)];
        switch (pick.type)
        {
            case BuildingType.Farm: prov.Farms--; break;
            case BuildingType.Mine: prov.Mines--; break;
            case BuildingType.Sawmill: prov.Sawmills--; break;
            case BuildingType.Workshop: prov.Workshops--; break;
        }
        State.Log($"Spies sabotaged a {pick.name} in {prov.Name} ({target.Name}).");
        if (_sim.RollChance(Balance.SabotageDiscoveryChance))
            Discover(net!, target, "sabotage");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Incites 5% of the target's soldiers to desert. 35% discovery risk.</summary>
    public string? SpyInciteRevolt(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.InciteMinStrength, Balance.InciteStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        int deserters = (int)(target!.Soldiers * Balance.InciteDesertionFraction);
        ArmyHelper.RemoveSoldiers(target, deserters);
        State.Log($"Spies incited revolt in {target.Name}: {deserters:N0} soldiers deserted.");
        if (_sim.RollChance(Balance.InciteDiscoveryChance))
            Discover(net!, target, "sedition");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Advances exactly one day. Used by the clock and by tests.</summary>
    public void AdvanceOneDay() => OnDayElapsed();

    private void OnDayElapsed()
    {
        State.CurrentDate = State.CurrentDate.AddDays(1);
        _sim.AdvanceDay(State);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clock.DayElapsed -= OnDayElapsed;
        Clock.Dispose();
    }
}
