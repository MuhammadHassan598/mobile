using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>What a colonisation mission to a region would cost a country, how long it would take, and whether it can be sent now.</summary>
/// <param name="Overland">The region touches land the country already holds, so the settlers walk: no fleet is needed and it is cheaper and quicker.</param>
/// <param name="Distance">Straight-line map units from the country's capital to the region.</param>
/// <param name="Block">Why the mission cannot be sent right now (null = it can).</param>
public sealed record ColonyQuote(
    FrontierRegion Region, ColonySize Size, double Area, bool Overland, double Distance,
    double Gold, double Wheat, double Wood, long Settlers, int Warships, int Days, DateOnly Completion, string? Block);

/// <summary>One thing a mission needs, what it needs and what the country has (for the colony popup: a tick or a cross on each).</summary>
public sealed record ColonyRequirement(string Icon, string Label, string Need, string Have, bool Met);

/// <summary>
/// Colonisation: any country, the player or an AI one, can settle an unclaimed colony region by sending a mission. The same rules serve everybody:
/// <list type="bullet">
/// <item><b>Cost</b> — gold, wheat and wood from the country's own stores, and settlers from its own people (<see cref="Quote"/>), committed exactly once when
/// the mission is sent and recorded on the mission; a fleet must be held to carry them (not for a region reached overland).</item>
/// <item><b>Claim</b> — the region is claimed by the first mission sent. Nobody else can send one to it while it is under way, and it can never be settled twice.</item>
/// <item><b>Landing</b> — on its completion date the mission founds a colony: a territory of its own (<see cref="Nation.IsColony"/>) with the settlers as its
/// people, the wheat and half the wood as its stocks, and the region's first workings. It fails (the cost is lost, the region is free again) if the country has
/// fallen or no longer holds the fleet it needed to carry the settlers.</item>
/// </list>
/// </summary>
public static class ColonizationService
{
    // ================= Reading =================

    /// <summary>The area of a region in map units squared.</summary>
    public static double AreaOf(FrontierRegion region)
    {
        double total = 0;
        foreach (var ring in region.Polygons)
        {
            double a = 0;
            for (int i = 0; i < ring.Count - 1; i++) a += ring[i].X * ring[i + 1].Y - ring[i + 1].X * ring[i].Y;
            total += Math.Abs(a) / 2;
        }
        return total;
    }

    /// <summary>The area as the player reads it, in square kilometres (to the nearest thousand).</summary>
    public static double AreaKm2(FrontierRegion region) => Math.Round(AreaOf(region) * Balance.ColonyKm2PerMapUnitSq / 1000) * 1000;

    public static ColonySize SizeOf(double area) =>
        area < Balance.ColonySmallMax ? ColonySize.Small
        : area < Balance.ColonyMediumMax ? ColonySize.Medium
        : area < Balance.ColonyLargeMax ? ColonySize.Large : ColonySize.Vast;

    public static double SizeMult(ColonySize size) => size switch
    {
        ColonySize.Small => Balance.ColonySmallMult,
        ColonySize.Medium => Balance.ColonyMediumMult,
        ColonySize.Large => Balance.ColonyLargeMult,
        _ => Balance.ColonyVastMult,
    };

    public static string SizeName(ColonySize size) => size switch
    {
        ColonySize.Small => "small", ColonySize.Medium => "medium", ColonySize.Large => "large", _ => "vast",
    };

    private static int SizeDays(ColonySize size) => size switch { ColonySize.Small => 0, ColonySize.Medium => 5, ColonySize.Large => 10, _ => 15 };

    /// <summary>Straight-line distance from a country's capital to a region.</summary>
    public static double DistanceTo(Nation owner, FrontierRegion region) =>
        Math.Sqrt(Math.Pow(owner.MapX - region.LabelX, 2) + Math.Pow(owner.MapY - region.LabelY, 2));

    /// <summary>
    /// Whether a region touches land the country already rules (its own realm or one of its colonies): the borders of the map are shared
    /// exactly, so touching means sharing a point.
    /// </summary>
    public static bool IsOverland(GameState state, Nation owner, FrontierRegion region)
    {
        var held = new HashSet<(double, double)>();
        foreach (var ring in owner.Territory) foreach (var p in ring) held.Add((p.X, p.Y));
        foreach (var colony in state.ColoniesOf(owner.Id)) foreach (var ring in colony.Territory) foreach (var p in ring) held.Add((p.X, p.Y));
        foreach (var ring in region.Polygons) foreach (var p in ring) if (held.Contains((p.X, p.Y))) return true;
        return false;
    }

    /// <summary>How the settlers travel, for the popup: on foot over land the country holds, by sea to a coast, or by sea and then inland (a region with no shore of its own).</summary>
    public static string Journey(bool overland, ColonyTerrain terrain) =>
        overland ? "overland, on foot" : terrain == ColonyTerrain.Highland ? "by sea to the nearest coast, then inland on foot" : "by sea";

    /// <summary>The same, as the verb of a movement line ("Colonists of X sailing to Java").</summary>
    public static string Voyage(bool overland, ColonyTerrain terrain) =>
        overland ? "marching overland" : terrain == ColonyTerrain.Highland ? "sailing, then marching inland" : "sailing";

    /// <summary>The mission a country has under way (a country sends one at a time).</summary>
    public static ColonizationMission? ActiveMission(GameState state, string ownerId) =>
        state.ColonizationMissions.FirstOrDefault(m => m.OwnerId == ownerId && m.Status == MissionStatus.InProgress);

    /// <summary>The country that owns a colonised region (null while it is not colonised).</summary>
    public static Nation? OwnerOf(GameState state, FrontierRegion region) =>
        region.Status != ColonyStatus.Colonized || region.ColonyId is null ? null
        : state.AllNations().FirstOrDefault(n => n.Id == state.Colonies.FirstOrDefault(c => c.Id == region.ColonyId)?.OwnerId);

    private static string NameOfId(GameState state, string id) => state.AllNations().FirstOrDefault(n => n.Id == id)?.Name ?? "another country";

    /// <summary>The price, time and verdict for <paramref name="owner"/> settling <paramref name="region"/> now.</summary>
    public static ColonyQuote Quote(GameState state, Nation owner, FrontierRegion region)
    {
        double area = AreaOf(region);
        var size = SizeOf(area);
        double mult = SizeMult(size);
        double distance = DistanceTo(owner, region);
        bool overland = IsOverland(state, owner, region);

        double gold = Balance.ColonyBaseGold * mult * (1 + Balance.ColonyDistanceGoldFactor * Math.Min(1.5, distance / 1000.0));
        if (overland) gold *= Balance.ColonyOverlandGoldMult;
        gold = Math.Round(gold / 10) * 10;
        double wheat = Math.Round(Balance.ColonyBaseWheat * mult);
        double wood = Math.Round(Balance.ColonyBaseWood * mult);
        long settlers = (long)Math.Round(Balance.ColonyBaseSettlers * mult);
        int warships = overland ? 0 : Balance.ColonyWarshipsRequired;
        int days = overland
            ? Math.Clamp(Balance.ColonyOverlandBaseDays + (int)Math.Ceiling(distance / 25.0) + SizeDays(size) / 2, Balance.ColonyMinOverlandDays, Balance.ColonyMaxOverlandDays)
            : Math.Clamp(Balance.ColonyBaseDays + (int)Math.Ceiling(distance / 10.0) + SizeDays(size), Balance.ColonyMinDays, Balance.ColonyMaxDays);

        return new ColonyQuote(region, size, area, overland, distance, gold, wheat, wood, settlers, warships, days,
            state.CurrentDate.AddDays(days), Block(state, owner, region, gold, wheat, wood, settlers, warships));
    }

    private static string? Block(GameState state, Nation owner, FrontierRegion region, double gold, double wheat, double wood, long settlers, int warships)
    {
        if (owner.IsEliminated || owner.IsColony) return $"{owner.Name} no longer rules anything.";
        if (region.Status == ColonyStatus.Colonized)
            return $"{region.Name} is already a colony of {OwnerOf(state, region)?.Name ?? "another country"}.";
        if (region.Status == ColonyStatus.MissionInProgress)
        {
            var claim = state.ColonizationMissions.FirstOrDefault(m => m.Id == region.MissionId);
            return claim is null ? $"{region.Name} has been claimed."
                : claim.OwnerId == owner.Id ? $"Your colonists are already on their way to {region.Name} (landing {claim.CompletionDate:dd-MM-yyyy})."
                : $"{region.Name} has been claimed by {claim.OwnerName}: their mission lands {claim.CompletionDate:dd-MM-yyyy}.";
        }
        if (ActiveMission(state, owner.Id) is { } busy)
            return $"{owner.Name} already has a colonisation mission under way, to {busy.RegionName} (landing {busy.CompletionDate:dd-MM-yyyy}). One at a time.";
        if (warships > 0 && owner.Warships < warships)
            return $"Needs {warships} warships to carry the colonists ({owner.Name} has {owner.Warships}).";
        if (owner.Population < settlers * Balance.ColonyHomeMultiple)
            return $"Too few people at home to spare {settlers:N0} settlers (a country needs {settlers * Balance.ColonyHomeMultiple:N0}).";
        if (!owner.CanPay(gold)) return $"Needs {Currency.Cost(gold)} (the treasury holds {Currency.Cost(owner.Gold)}).";
        if (owner.GetProduct("Wheat") + 1e-9 < wheat) return $"Needs {wheat:N0} wheat for the voyage ({owner.Name} holds {owner.GetProduct("Wheat"):N0}).";
        if (owner.Wood + 1e-9 < wood) return $"Needs {wood:N0} wood for the ships and the first huts ({owner.Name} holds {owner.Wood:N0}).";
        return null;
    }

    /// <summary>Everything a mission to this region needs, each with what the country has, in the order the popup lists them.</summary>
    public static List<ColonyRequirement> Requirements(GameState state, Nation owner, ColonyQuote q)
    {
        var list = new List<ColonyRequirement>
        {
            new("🪙", "Gold", Currency.Cost(q.Gold), Currency.Cost(owner.Gold), owner.CanPay(q.Gold)),
            new("🌾", "Wheat", q.Wheat.ToString("N0"), owner.GetProduct("Wheat").ToString("N0"), owner.GetProduct("Wheat") + 1e-9 >= q.Wheat),
            new("🪵", "Wood", q.Wood.ToString("N0"), owner.Wood.ToString("N0"), owner.Wood + 1e-9 >= q.Wood),
            new("👥", "Settlers", q.Settlers.ToString("N0"), $"{owner.Population:N0} at home (needs {q.Settlers * Balance.ColonyHomeMultiple:N0})", owner.Population >= q.Settlers * Balance.ColonyHomeMultiple),
        };
        if (q.Warships > 0)
            list.Add(new("⚓", "Warships", q.Warships.ToString("N0"), owner.Warships.ToString("N0"), owner.Warships >= q.Warships));
        else
            list.Add(new("🥾", "Fleet", "none: the settlers walk", "—", true));
        var busy = ActiveMission(state, owner.Id);
        list.Add(new("⛵", "No other mission under way", "one at a time", busy is null ? "free" : $"busy until {busy.CompletionDate:dd-MM-yyyy}", busy is null));
        return list;
    }

    /// <summary>One line on where a region stands, for the map popup and the lists.</summary>
    public static string StatusText(GameState state, FrontierRegion region)
    {
        switch (region.Status)
        {
            case ColonyStatus.Colonized:
                var colony = state.Colonies.FirstOrDefault(c => c.Id == region.ColonyId);
                return colony is null ? "Colonised" : $"Colonised by {NameOfId(state, colony.OwnerId)} on {colony.FoundedDate:dd-MM-yyyy}";
            case ColonyStatus.MissionInProgress:
                var m = state.ColonizationMissions.FirstOrDefault(x => x.Id == region.MissionId);
                return m is null ? "Claimed" : $"Mission of {m.OwnerName} in progress: lands {m.CompletionDate:dd-MM-yyyy} ({m.DaysLeft(state.CurrentDate)} days left)";
            default:
                return "Unclaimed";
        }
    }

    // ================= Sending =================

    /// <summary>
    /// Sends a mission. Everything is checked again here (the region still unclaimed, the country able to pay), the cost is taken from the country exactly
    /// once and recorded on the mission, the region is claimed, and the mission is logged. Returns the mission, or the reason it cannot be sent.
    /// </summary>
    public static (ColonizationMission? Mission, string? Error) Start(GameState state, Nation owner, string regionId)
    {
        var region = state.FrontierRegions.FirstOrDefault(r => r.Id == regionId);
        if (region is null) return (null, "Region not found.");
        var quote = Quote(state, owner, region);
        if (quote.Block is { } why) return (null, why);

        owner.PayGold(quote.Gold);
        owner.AddProduct("Wheat", -quote.Wheat);
        owner.Wood -= quote.Wood;
        owner.Population -= quote.Settlers;

        var mission = new ColonizationMission
        {
            RegionId = region.Id, RegionName = region.Name, OwnerId = owner.Id, OwnerName = owner.Name,
            StartDate = state.CurrentDate, CompletionDate = quote.Completion, TotalDays = quote.Days,
            Gold = quote.Gold, Wheat = quote.Wheat, Wood = quote.Wood, Settlers = quote.Settlers,
            WarshipsRequired = quote.Warships, Overland = quote.Overland,
        };
        state.ColonizationMissions.Add(mission);
        region.Status = ColonyStatus.MissionInProgress;
        region.MissionId = mission.Id;

        string how = quote.Overland ? "marches overland to" : region.Terrain == ColonyTerrain.Highland ? "sails, then marches inland to" : "sails for";
        state.LogMovement(MovementKind.Colony, MovementStatus.UnderWay, owner.Id, owner.Name, region.Id, region.Name,
            $"{owner.Name}'s colonists {how} {region.Name} ({quote.Days} days, landing {quote.Completion:dd-MM-yyyy}).", inbox: InboxTopic.Colony);
        if (!owner.IsPlayer)
            InboxService.Notify(state, InboxTopic.Colony, $"{owner.Name} has claimed {region.Name}: its colonists land {quote.Completion:dd-MM-yyyy}.", owner.Id,
                title: $"Colony claimed — {region.Name}");
        return (mission, null);
    }

    // ================= The daily tick =================

    /// <summary>Missions move on a day: those whose completion date has come land (and found a colony) or fail.</summary>
    public static void Advance(GameState state)
    {
        foreach (var mission in state.ColonizationMissions.Where(m => m.Status == MissionStatus.InProgress).ToList())
        {
            var owner = state.AllNations().FirstOrDefault(n => n.Id == mission.OwnerId);
            var region = state.FrontierRegions.FirstOrDefault(r => r.Id == mission.RegionId);

            if (owner is null || owner.IsEliminated || owner.IsColony) { Fail(state, mission, region, owner, $"{mission.OwnerName} fell before its colonists landed: the mission was lost."); continue; }
            if (region is null || region.Status != ColonyStatus.MissionInProgress || region.MissionId != mission.Id)
            { Fail(state, mission, null, owner, $"The claim to {mission.RegionName} no longer stands: the mission was lost."); continue; }
            if (state.CurrentDate < mission.CompletionDate) continue;

            if (mission.WarshipsRequired > 0 && owner.Warships < mission.WarshipsRequired)
            {
                Fail(state, mission, region, owner,
                    $"The fleet that was to carry the colonists to {region.Name} is gone ({owner.Warships} of the {mission.WarshipsRequired} warships needed): the mission was lost.");
                continue;
            }
            Complete(state, mission, region, owner);
        }
    }

    private static void Fail(GameState state, ColonizationMission mission, FrontierRegion? region, Nation? owner, string outcome)
    {
        mission.Status = MissionStatus.Failed;
        mission.EndDate = state.CurrentDate;
        mission.Outcome = outcome;
        if (region is not null && region.MissionId == mission.Id) { region.Status = ColonyStatus.Unclaimed; region.MissionId = null; }
        state.LogMovement(MovementKind.Colony, MovementStatus.Failed, mission.OwnerId, mission.OwnerName, mission.RegionId, mission.RegionName, outcome, inbox: InboxTopic.Colony);
        if (owner is not null && !owner.IsPlayer)
            InboxService.Notify(state, InboxTopic.Colony, outcome, mission.OwnerId, title: $"Colony mission failed — {mission.RegionName}");
    }

    private static void Complete(GameState state, ColonizationMission mission, FrontierRegion region, Nation owner)
    {
        var colony = Found(state, mission, region, owner);
        mission.Status = MissionStatus.Completed;
        mission.EndDate = state.CurrentDate;
        mission.ColonyId = colony.Id;
        mission.Outcome = $"{owner.Name} founded the colony of {region.Name}.";
        region.Status = ColonyStatus.Colonized;
        region.ColonyId = colony.Id;
        region.MissionId = null;
        owner.ColoniesFounded++;

        string text = $"{owner.Name}'s colonists landed in {region.Name}: a colony was founded with {mission.Settlers:N0} settlers.";
        state.LogMovement(MovementKind.Colony, MovementStatus.Completed, owner.Id, owner.Name, region.Id, region.Name, text, inbox: InboxTopic.Colony);
        if (!owner.IsPlayer)
            InboxService.Notify(state, InboxTopic.Colony, text, owner.Id, title: $"Colony founded — {region.Name}");
    }

    /// <summary>
    /// The colony's own record: its land (the region's), its settlers (taken from the home country when the mission was sent, so nobody is counted twice), the wheat
    /// and half the wood the mission carried as its stocks, a week of everything its people consume, mills enough to feed them, and the region's first workings.
    /// </summary>
    private static Colony Found(GameState state, ColonizationMission mission, FrontierRegion region, Nation owner)
    {
        var site = ColonyCatalog.Get(region.Name);
        var record = new Nation
        {
            Id = "colony-" + region.Id, Name = region.Name, IsColony = true, IsEliminated = true,
            AnnexedById = owner.Id, AnnexedDate = state.CurrentDate, ColonyRegionId = region.Id,
            Emblem = site?.Icon ?? "⛵", Religion = owner.Religion, ColorHex = owner.ColorHex,
            CapitalName = site?.Settlement ?? region.Name, MapX = region.LabelX, MapY = region.LabelY,
            Population = mission.Settlers, CrownDomains = 0,
            Territory = region.Polygons.Select(ring => ring.ToList()).ToList(),
        };
        if (site is not null)
            foreach (var (building, count) in site.Endowment)
                record.ProductionBuildings[building] = record.GetProductionBuilding(building) + count;
        record.EnsureTaxationInitialized();
        ConsumptionService.SeedStock(record, Balance.StartingStockDays);
        ConsumptionService.EnsureSupply(record, Balance.ColonySupplyCoverage);
        record.AddProduct("Wheat", mission.Wheat * Balance.ColonyFoodStockShare);
        record.Wood += mission.Wood * Balance.ColonyWoodStockShare;
        state.OtherNations.Add(record);

        var colony = new Colony
        {
            Id = record.Id, Name = region.Name, OwnerId = owner.Id, FoundedById = owner.Id, FoundedDate = state.CurrentDate, RegionId = region.Id,
        };
        state.Colonies.Add(colony);
        return colony;
    }

    // ================= The AI =================

    private static double EndowmentValue(ColonySite? site)
    {
        if (site is null) return 1;
        double v = 0;
        foreach (var (building, count) in site.Endowment)
            v += count * building switch
            {
                "goldmine" => 3.0, "ironmine" => 1.5, "copperminer" or "leadminer" => 1.2, "farm" => 1.0, "furfarm" => 1.0,
                "sawmill" => 0.8, _ => 0.6,
            };
        return 1 + v / 4;
    }

    /// <summary>How much a country cares for colonies at all, in 0..1: a stable trait (some rulers look outward, some do not).</summary>
    public static double Appetite(Nation n)
    {
        int h = 7;
        foreach (char c in "appetite|" + n.Id) h = unchecked(h * 31 + c);
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>A stable preference of a country for a region, in -0.35..0.35: no two rulers want the same land for the same reasons.</summary>
    private static double Taste(string nationId, string regionId)
    {
        int h = 17;
        foreach (char c in nationId + "|" + regionId) h = unchecked(h * 31 + c);
        return ((h & 0xFFFF) / 65535.0 - 0.5) * 0.7;
    }

    /// <summary>
    /// How much an AI country wants a region: what the land offers per gold it would cost, minus the distance, plus a liking for land it can reach overland
    /// or is closest to (its own sphere), minus land that a rival lies much closer to (it would be contested), plus the country's own taste.
    /// </summary>
    public static double Desire(GameState state, Nation n, ColonyQuote quote)
    {
        var region = quote.Region;
        var site = ColonyCatalog.Get(region.Name);
        double sizeFactor = quote.Size switch { ColonySize.Small => 0.8, ColonySize.Medium => 1.0, ColonySize.Large => 1.2, _ => 1.4 };
        double value = EndowmentValue(site) * sizeFactor / Math.Max(0.4, quote.Gold / Balance.ColonyBaseGold);
        double score = value - quote.Distance / 1500.0 + (quote.Overland ? 0.5 : 0);

        double nearestRival = double.MaxValue;
        foreach (var other in state.AllNations())
        {
            if (other.IsEliminated || other.Id == n.Id) continue;
            nearestRival = Math.Min(nearestRival, DistanceTo(other, region));
        }
        if (!n.IsPlayer && nearestRival < double.MaxValue)
        {
            if (nearestRival < quote.Distance * 0.7) score -= 0.4;   // a rival is far closer: it would be contested
            else if (quote.Distance <= nearestRival) score += 0.3;   // this country is the nearest power: its own sphere
        }
        return score + Taste(n.Id, region.Id);
    }

    /// <summary>
    /// Once in a while an AI country weighs sending a colonisation mission, by the very rules and costs of the player's: it needs the fleet, the settlers and
    /// the gold, wheat and wood from its own stores (and keeps a reserve), it is not at war, it holds colonies only in proportion to its people, it rests
    /// between missions, and only a handful of AI missions are under way in the world at once. It picks the region it wants most, if it wants one enough.
    /// </summary>
    public static void AiThink(GameState state, Nation n, Random rng)
    {
        if (n.IsPlayer || n.IsEliminated || n.IsColony) return;
        if (state.CurrentDate < state.AiWorldStartDate.AddDays(Balance.AiColonyStartDays)) return;
        if (ActiveMission(state, n.Id) is not null) return;
        if (n.Population < Balance.AiColonyMinPopulation || Appetite(n) < Balance.AiColonyAppetiteMin) return;
        if (AiWorldService.AtWarAny(state, n)) return;

        int held = state.ColoniesOf(n.Id).Count;
        int cap = (int)Math.Clamp(1 + n.Population / (Balance.AiColonyPopulationPerColony * 2), 1, 4);
        if (held >= cap) return;
        var last = state.ColonizationMissions.Where(m => m.OwnerId == n.Id && m.EndDate is not null).Select(m => m.EndDate!.Value).DefaultIfEmpty(DateOnly.MinValue).Max();
        if (last != DateOnly.MinValue && state.CurrentDate.DayNumber - last.DayNumber < Balance.AiColonyCooldownDays) return;
        if (state.ColonizationMissions.Count(m => m.Status == MissionStatus.InProgress && m.OwnerId != state.PlayerNation.Id) >= Balance.AiColonyConcurrent) return;
        if (rng.NextDouble() >= Balance.AiColonyChancePerThink) return;

        ColonyQuote? best = null; double bestScore = double.MinValue;
        foreach (var region in state.FrontierRegions.Where(r => r.Status == ColonyStatus.Unclaimed))
        {
            var quote = Quote(state, n, region);
            if (quote.Block is not null) continue;
            if (!quote.Overland && quote.Distance > Balance.AiColonyRange) continue;   // beyond its reach
            if (n.Gold < quote.Gold * Balance.AiColonyReserve || n.GetProduct("Wheat") < quote.Wheat * 1.2 || n.Wood < quote.Wood * 1.2) continue;
            double score = Desire(state, n, quote);
            if (score > bestScore) { bestScore = score; best = quote; }
        }
        if (best is null || bestScore < Balance.AiColonyMinDesire) return;
        Start(state, n, best.Region.Id);
    }
}
