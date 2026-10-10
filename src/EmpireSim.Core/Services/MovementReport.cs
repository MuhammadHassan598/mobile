using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>Something that is on the road or posted abroad right now (the "Mission location" view).</summary>
public sealed record Mission(
    MovementKind Kind,
    string FromId,
    string FromName,
    string LocationId,
    string LocationName,
    string Text,
    /// <summary>0..1 where the mission has a start and an end; null when it simply stays in place.</summary>
    double? Progress,
    /// <summary>When it arrives or comes home; null when it has no end date.</summary>
    DateOnly? Due,
    int? DaysLeft);

/// <summary>Records grouped under the state they concern.</summary>
public sealed record StateGroup<T>(string StateId, string StateName, IReadOnlyList<T> Items);

/// <summary>
/// Read side of the Movement Report: saved movement records (Events) and what is under way
/// or stationed abroad right now (Mission location), filtered by state and grouped as the page needs.
/// </summary>
public static class MovementReport
{
    public static string Icon(MovementKind kind) => kind switch
    {
        MovementKind.Troops => "🪖",
        MovementKind.March => "⚔️",
        MovementKind.Gold => "🪙",
        MovementKind.Goods => "📦",
        MovementKind.Colony => "⛵",
        MovementKind.Mission => "📜",
        MovementKind.Treaty => "🤝",
        MovementKind.War => "🚩",
        MovementKind.Assembly => "🏛️",
        MovementKind.Diplomacy => "🗣️",
        _ => "•"
    };

    public static string KindName(MovementKind kind) => kind switch
    {
        MovementKind.Troops => "Troops",
        MovementKind.March => "Armies",
        MovementKind.Gold => "Gold",
        MovementKind.Goods => "Goods",
        MovementKind.Colony => "Colonies",
        MovementKind.Mission => "Missions",
        MovementKind.Treaty => "Treaties",
        MovementKind.War => "War & peace",
        MovementKind.Assembly => "Assembly",
        MovementKind.Diplomacy => "Diplomacy",
        _ => kind.ToString()
    };

    public static string StatusName(MovementStatus status) => status switch
    {
        MovementStatus.Completed => "Done",
        MovementStatus.UnderWay => "Under way",
        MovementStatus.Failed => "Refused",
        _ => ""
    };

    // ---------------- Events (saved records) ----------------

    /// <summary>Saved movements, newest first; with a state id only those that involve that state.</summary>
    public static List<MovementRecord> Events(GameState state, string? stateId = null) =>
        state.Movements
            .Where(m => stateId is null || m.Involves(stateId))
            .Reverse()   // records are appended in time order, so reversing keeps same-day events newest-first
            .OrderByDescending(m => m.Date)
            .ToList();

    /// <summary>
    /// The state a record is "about" from the player's point of view: the other side when the player
    /// is involved, otherwise the one that acted (e.g. an ally marching on an enemy).
    /// </summary>
    public static (string Id, string Name) Counterpart(GameState state, MovementRecord m)
    {
        string player = state.PlayerNation.Id;
        if (m.FromId == player) return (m.ToId, m.ToName);
        if (m.ToId == player) return (m.FromId, m.FromName);
        return (m.FromId, m.FromName);
    }

    /// <summary>Events grouped under the state they concern; groups with the latest news first, items newest first.</summary>
    public static List<StateGroup<MovementRecord>> EventsByState(GameState state, string? stateId = null) =>
        Events(state, stateId)
            .GroupBy(m => Counterpart(state, m))
            .Select(g => new StateGroup<MovementRecord>(g.Key.Id, g.Key.Name, g.ToList()))
            .ToList();   // GroupBy keeps first-seen order, i.e. the group with the newest event first

    // ---------------- Mission location (live) ----------------

    /// <summary>
    /// Everything on the road or stationed abroad right now: marching armies, troop loans, shipments in transit,
    /// the colony expedition, spy networks, missionaries and embassies. Soonest arrival first.
    /// </summary>
    public static List<Mission> ActiveMissions(GameState state, string? stateId = null)
    {
        var today = state.CurrentDate;
        var player = state.PlayerNation;
        var missions = new List<Mission>();

        foreach (var m in state.MarchingArmies)
            missions.Add(new Mission(MovementKind.March, m.AttackerNationId, m.AttackerNationName, m.TargetNationId, m.TargetNationName,
                $"{m.Strength:N0} soldiers marching on {m.TargetNationName}", m.Progress, today.AddDays(m.DaysLeft), m.DaysLeft));

        foreach (var l in state.TroopLoans)
        {
            string owner = NameOf(state, l.OwnerId), host = NameOf(state, l.HostId);
            int left = Math.Max(0, l.ReturnDate.DayNumber - today.DayNumber);
            missions.Add(new Mission(MovementKind.Troops, l.OwnerId, owner, l.HostId, host,
                $"{l.Soldiers:N0} soldiers of {owner} serving in {host}", 1 - Math.Clamp((double)left / Balance.TroopLoanDays, 0, 1), l.ReturnDate, left));
        }

        foreach (var c in state.TradeContracts.Where(c => c.Status == TradeStatus.InTransit))
        {
            string seller = NameOf(state, c.SellerId), buyer = NameOf(state, c.BuyerId);
            string goods = TradeCatalog.Get(c.ProductId)?.Name ?? c.ProductId;
            int total = Math.Max(1, c.DeliveryDate.DayNumber - c.CreatedDate.DayNumber);
            int left = Math.Max(0, c.DeliveryDate.DayNumber - today.DayNumber);
            missions.Add(new Mission(MovementKind.Goods, c.SellerId, seller, c.BuyerId, buyer,
                $"{c.Quantity:N0} {goods} from {seller} to {buyer}", 1 - Math.Clamp((double)left / total, 0, 1), c.DeliveryDate, left));
        }

        if (state.ActiveExpedition is { } ex)
            missions.Add(new Mission(MovementKind.Colony, player.Id, player.Name, ex.RegionId, ex.RegionName,
                $"Colony expedition sailing for {ex.RegionName}", ex.Progress, today.AddDays(ex.DaysLeft), ex.DaysLeft));

        foreach (var net in state.SpyNetworks)
            missions.Add(new Mission(MovementKind.Mission, player.Id, player.Name, net.TargetNationId, net.TargetNationName,
                $"Spy network in {net.TargetNationName} (strength {net.Strength})", null, null, null));

        foreach (var (targetId, influence) in state.MissionaryInfluence.Where(kv => kv.Value > 0))
            missions.Add(new Mission(MovementKind.Mission, player.Id, player.Name, targetId, NameOf(state, targetId),
                $"Missionaries in {NameOf(state, targetId)} (influence {influence:N0}/{Balance.MissionaryConversionThreshold:N0})", null, null, null));

        foreach (var e in state.Treaties.Where(t => t.Type == TreatyType.Embassy && t.NationAId == player.Id && t.IsActiveOn(today)))
            missions.Add(new Mission(MovementKind.Mission, player.Id, player.Name, e.NationBId, NameOf(state, e.NationBId),
                $"Embassy in {NameOf(state, e.NationBId)} since {e.SignedDate:dd-MM-yyyy}", null, null, null));

        return missions
            .Where(m => stateId is null || m.FromId == stateId || m.LocationId == stateId)
            .OrderBy(m => m.Due ?? DateOnly.MaxValue)
            .ThenBy(m => m.LocationName)
            .ToList();
    }

    /// <summary>Missions grouped by the state they are in (where they are now).</summary>
    public static List<StateGroup<Mission>> MissionsByState(GameState state, string? stateId = null) =>
        ActiveMissions(state, stateId)
            .GroupBy(m => (m.LocationId, m.LocationName))
            .Select(g => new StateGroup<Mission>(g.Key.LocationId, g.Key.LocationName, g.ToList()))
            .ToList();

    private static string NameOf(GameState state, string nationId) =>
        state.AllNations().FirstOrDefault(n => n.Id == nationId)?.Name ?? "unknown";
}
