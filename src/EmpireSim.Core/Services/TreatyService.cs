using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// Treaty bookkeeping and enforcement: lookups, signing/ending, the places treaties
/// forbid attacks, how warmly an AI country regards the player, allied intervention
/// and troop loans. Pure functions over <see cref="GameState"/> so both the engine
/// (player actions) and the simulation (AI behaviour) use the same rules.
/// </summary>
public static class TreatyService
{
    // ---------------- Lookups ----------------

    /// <summary>The active treaty of a type between two nations (an embassy is directional: a sent it to b).</summary>
    public static Treaty? Find(GameState state, TreatyType type, string a, string b)
    {
        foreach (var t in state.Treaties)
        {
            if (t.Type != type || !t.IsActiveOn(state.CurrentDate)) continue;
            bool match = type == TreatyType.Embassy
                ? t.NationAId == a && t.NationBId == b
                : t.Links(a, b);
            if (match) return t;
        }
        return null;
    }

    public static bool Has(GameState state, TreatyType type, string a, string b) =>
        Find(state, type, a, b) is not null;

    /// <summary>Whether <paramref name="fromId"/> keeps an embassy in <paramref name="inId"/>.</summary>
    public static bool HasEmbassy(GameState state, string fromId, string inId) =>
        Has(state, TreatyType.Embassy, fromId, inId);

    /// <summary>Every active treaty between two nations, in either direction.</summary>
    public static IEnumerable<Treaty> Between(GameState state, string a, string b) =>
        state.Treaties.Where(t => t.Links(a, b) && t.IsActiveOn(state.CurrentDate));

    public static string NameOf(GameState state, string nationId) =>
        state.AllNations().FirstOrDefault(n => n.Id == nationId)?.Name ?? "that nation";

    public static string TypeName(TreatyType type) => type switch
    {
        TreatyType.Embassy => "embassy",
        TreatyType.NonAggression => "non-aggression pact",
        TreatyType.DefensiveAlliance => "defensive alliance",
        TreatyType.TradeAgreement => "trade agreement",
        _ => "treaty"
    };

    /// <summary>"an embassy", "a defensive alliance": the treaty name with the right article.</summary>
    public static string WithArticle(TreatyType type)
    {
        string name = TypeName(type);
        return "aeiou".Contains(name[0]) ? $"an {name}" : $"a {name}";
    }

    // ---------------- Enforcement ----------------

    /// <summary>
    /// A message when a treaty forbids the attacker from hurting the defender (war, invasion,
    /// hostile spy work), otherwise null. Applies to the player and to AI nations alike.
    /// </summary>
    public static string? ForbidsAttack(GameState state, string attackerId, string defenderId)
    {
        var nap = Find(state, TreatyType.NonAggression, attackerId, defenderId);
        if (nap is not null)
            return $"A non-aggression pact with {NameOf(state, defenderId)} runs until " +
                   $"{nap.ExpiresDate:dd-MM-yyyy}. Cancel it first (costs relations).";
        if (Has(state, TreatyType.DefensiveAlliance, attackerId, defenderId))
            return $"You are allied with {NameOf(state, defenderId)}. Cancel the alliance first (costs relations).";
        return null;
    }

    // ---------------- AI attitude ----------------

    /// <summary>
    /// How warmly an AI country regards the player when deciding on a proposal:
    /// its rating (0-100) plus goodwill from an embassy, pacts and a shared faith.
    /// Deterministic — the same situation always gets the same answer.
    /// </summary>
    public static int Score(GameState state, Nation target)
    {
        var player = state.PlayerNation;
        int score = DiplomacyService.ToDisplayRating(target.RelationToPlayer);
        if (HasEmbassy(state, player.Id, target.Id)) score += 5;
        if (Has(state, TreatyType.DefensiveAlliance, player.Id, target.Id)) score += 10;
        if (Has(state, TreatyType.NonAggression, player.Id, target.Id)) score += 3;
        if (Has(state, TreatyType.TradeAgreement, player.Id, target.Id)) score += 3;
        if (!string.IsNullOrEmpty(player.Religion)
            && player.Religion.Equals(target.Religion, StringComparison.OrdinalIgnoreCase))
            score += 5;
        return score;
    }

    // ---------------- Signing and ending ----------------

    /// <summary>Signs a treaty (callers have already validated and charged).</summary>
    public static Treaty Add(GameState state, TreatyType type, Nation a, Nation b, int? days = null)
    {
        var treaty = new Treaty
        {
            Type = type,
            NationAId = a.Id,
            NationBId = b.Id,
            SignedDate = state.CurrentDate,
            ExpiresDate = days is null ? null : state.CurrentDate.AddDays(days.Value),
        };
        state.Treaties.Add(treaty);
        if (type == TreatyType.TradeAgreement) SetTradeFlag(a, b, true);
        return treaty;
    }

    /// <summary>Ends a treaty and keeps the legacy trade-pact flag in step.</summary>
    public static void Remove(GameState state, Treaty treaty)
    {
        state.Treaties.Remove(treaty);
        if (treaty.Type == TreatyType.TradeAgreement)
        {
            var a = state.AllNations().FirstOrDefault(n => n.Id == treaty.NationAId);
            var b = state.AllNations().FirstOrDefault(n => n.Id == treaty.NationBId);
            if (a is not null && b is not null) SetTradeFlag(a, b, false);
        }
    }

    /// <summary>The pact flag lives on the AI nation (its pact with the player).</summary>
    private static void SetTradeFlag(Nation a, Nation b, bool value)
    {
        if (a.IsPlayer) b.HasTradePactWithPlayer = value;
        else if (b.IsPlayer) a.HasTradePactWithPlayer = value;
    }

    /// <summary>War between two nations ends every treaty between them and recalls their loaned soldiers.</summary>
    public static void OnWar(GameState state, Nation a, Nation b)
    {
        foreach (var t in state.Treaties.Where(t => t.Links(a.Id, b.Id)).ToList())
            Remove(state, t);
        foreach (var loan in state.TroopLoans
                     .Where(l => (l.OwnerId == a.Id && l.HostId == b.Id) || (l.OwnerId == b.Id && l.HostId == a.Id))
                     .ToList())
            ReturnLoan(state, loan, "war broke out");
    }

    /// <summary>Called as a nation is annexed: its treaties end and soldiers it was hosting go home.</summary>
    public static void OnEliminated(GameState state, Nation loser)
    {
        foreach (var loan in state.TroopLoans.Where(l => l.HostId == loser.Id).ToList())
            ReturnLoan(state, loan, $"{loser.Name} fell");
        // Soldiers the loser had lent out stay with their hosts: the loan record just ends.
        state.TroopLoans.RemoveAll(l => l.OwnerId == loser.Id);
        foreach (var t in state.Treaties.Where(t => t.Involves(loser.Id)).ToList())
            Remove(state, t);
        state.MissionaryInfluence.Remove(loser.Id);
    }

    // ---------------- Troop loans ----------------

    /// <summary>
    /// Moves soldiers from the owner's army into the host's and records the loan.
    /// The soldiers exist exactly once: they leave the owner's stacks as they join the host's.
    /// </summary>
    public static TroopLoan? Lend(GameState state, Nation owner, Nation host, int count, int days)
    {
        var force = ArmyHelper.ExtractSoldiers(owner, count);
        if (force.Count == 0) return null;
        ArmyHelper.MergeStacks(host, force);
        var loan = new TroopLoan
        {
            OwnerId = owner.Id,
            HostId = host.Id,
            Force = force.Select(f => new UnitStack { Type = f.Type, Count = f.Count }).ToList(),
            ReturnDate = state.CurrentDate.AddDays(days),
        };
        state.TroopLoans.Add(loan);
        return loan;
    }

    /// <summary>Ends a loan: what is left of the lent soldiers is taken from the host and handed back.</summary>
    public static int ReturnLoan(GameState state, TroopLoan loan, string why)
    {
        state.TroopLoans.Remove(loan);
        var owner = state.AllNations().FirstOrDefault(n => n.Id == loan.OwnerId);
        var host = state.AllNations().FirstOrDefault(n => n.Id == loan.HostId);
        if (owner is null || host is null || owner.IsEliminated) return 0;

        var back = ArmyHelper.TakeBack(host, loan.Force);
        ArmyHelper.MergeStacks(owner, back);
        int returned = back.Sum(s => s.Count);
        state.LogMovement(MovementKind.Troops, MovementStatus.Completed, host, owner,
            $"{returned:N0} of {loan.Soldiers:N0} loaned soldiers returned from {host.Name} to {owner.Name} ({why}).");
        return returned;
    }

    // ---------------- Marches on behalf of allies ----------------

    /// <summary>Sends <paramref name="count"/> of a nation's own soldiers marching on a target (the existing march/battle system resolves it).</summary>
    public static MarchingArmy? LaunchMarch(GameState state, Nation from, Nation target, int count)
    {
        var force = ArmyHelper.ExtractSoldiers(from, count);
        if (force.Count == 0) return null;
        int days = Warfare.TravelDays(from.MapX, from.MapY, target.MapX, target.MapY);
        var march = new MarchingArmy
        {
            AttackerNationId = from.Id,
            AttackerNationName = from.Name,
            TargetNationId = target.Id,
            TargetNationName = target.Name,
            Force = force,
            DaysLeft = days,
            TotalDays = days,
        };
        state.MarchingArmies.Add(march);
        return march;
    }

    /// <summary>Whether a nation already has an army marching on a target.</summary>
    public static bool HasMarchOn(GameState state, string fromId, string targetId) =>
        state.MarchingArmies.Any(m => m.AttackerNationId == fromId && m.TargetNationId == targetId);

    /// <summary>
    /// The player was attacked: every defensive ally that can and will honour the pact sends part of its army
    /// against the aggressor. Returns how many allies marched.
    /// </summary>
    public static int AllianceDefence(GameState state, Nation aggressor, Nation victim)
    {
        int marched = 0;
        foreach (var ally in state.OtherNations.ToList())
        {
            if (ally.IsEliminated || ally.Id == aggressor.Id || ally.AtWarWithPlayer) continue;
            if (!Has(state, TreatyType.DefensiveAlliance, victim.Id, ally.Id)) continue;
            if (DiplomacyService.ToDisplayRating(ally.RelationToPlayer) < Balance.AllianceHonorMinRating)
            {
                state.LogMovement(MovementKind.March, MovementStatus.Failed, ally, aggressor, $"{ally.Name} ignores the call to defend you against {aggressor.Name}.");
                continue;
            }
            int commit = (int)(ally.Soldiers * Balance.AllianceAidFraction);
            if (commit < Balance.MinInvasionForce)
            {
                state.LogMovement(MovementKind.March, MovementStatus.Failed, ally, aggressor, $"{ally.Name} is too weak to march against {aggressor.Name}.");
                continue;
            }
            var march = LaunchMarch(state, ally, aggressor, commit);
            if (march is null) continue;
            marched++;
            state.LogMovement(MovementKind.March, MovementStatus.UnderWay, ally, aggressor,
                $"🛡 {ally.Name} honours the alliance: {commit:N0} soldiers march on {aggressor.Name} ({march.DaysLeft} days).");
            state.ActiveWarnings.Add($"🛡 {ally.Name} marches to defend you against {aggressor.Name}!");
        }
        return marched;
    }

    // ---------------- Daily upkeep of treaties ----------------

    /// <summary>
    /// Once a day: lapse expired treaties, return loans that are due, and let embassies
    /// and alliances warm relations a little.
    /// </summary>
    public static void Advance(GameState state)
    {
        foreach (var t in state.Treaties.ToList())
        {
            if (t.IsActiveOn(state.CurrentDate)) continue;
            Remove(state, t);
            string partnerId = t.Other(state.PlayerNation.Id);
            state.LogMovement(t.Type == TreatyType.Embassy ? MovementKind.Mission : MovementKind.Treaty, MovementStatus.Completed,
                state.PlayerNation.Id, state.PlayerNation.Name, partnerId, NameOf(state, partnerId),
                $"The {TypeName(t.Type)} with {NameOf(state, partnerId)} has expired.");
        }

        foreach (var loan in state.TroopLoans.Where(l => state.CurrentDate >= l.ReturnDate).ToList())
            ReturnLoan(state, loan, "term ended");

        var player = state.PlayerNation;
        foreach (var other in state.OtherNations)
        {
            if (other.IsEliminated || other.AtWarWithPlayer) continue;
            double warmth = 0;
            if (HasEmbassy(state, player.Id, other.Id)) warmth += Balance.EmbassyRelationPerDay;
            if (Has(state, TreatyType.DefensiveAlliance, player.Id, other.Id)) warmth += Balance.AllianceRelationPerDay;
            if (warmth > 0) other.RelationToPlayer = Math.Min(100, other.RelationToPlayer + warmth);
        }
    }

    /// <summary>Old saves kept the trade pact only as a flag: give each one its treaty record.</summary>
    public static void MigrateLegacy(GameState state)
    {
        var player = state.PlayerNation;
        foreach (var other in state.OtherNations.Where(n => n.HasTradePactWithPlayer && !n.IsEliminated))
            if (!Has(state, TreatyType.TradeAgreement, player.Id, other.Id))
                Add(state, TreatyType.TradeAgreement, player, other);
    }
}
