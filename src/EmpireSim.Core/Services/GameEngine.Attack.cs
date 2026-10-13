using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>What the Attack popup shows before the player confirms: the home army, the sizes that may be sent, the march and why not.</summary>
/// <param name="AtWar">War is already on, so confirming only sends troops; otherwise confirming also declares war.</param>
/// <param name="HomeArmy">Soldiers at home right now (the troops that could be sent).</param>
/// <param name="MinForce">The smallest army that may be sent (the same minimum the AI's marches obey).</param>
/// <param name="TravelDays">How long the march takes: capital to capital over the one movement speed.</param>
/// <param name="Distance">Map distance between the capitals (null when no coordinates exist: the fallback applies).</param>
/// <param name="GeographyNote">Set when a capital lacks coordinates: names the countries that need geographical data.</param>
/// <param name="TargetArmy">The target's home army (what its own side of the balance of forces shows).</param>
/// <param name="BlockReason">Why the attack cannot be launched at all (treaty, protection, no army...); null when it can.</param>
public sealed record AttackPlan(
    string TargetId,
    string TargetName,
    bool AtWar,
    int HomeArmy,
    int MinForce,
    int TravelDays,
    DateOnly Arrival,
    double? Distance,
    string? GeographyNote,
    int TargetArmy,
    string? BlockReason)
{
    public bool CanAttack => BlockReason is null;
}

/// <summary>How exposed the home country is if a given number of soldiers is sent away.</summary>
/// <param name="Level">"ok" (nothing special), "caution" (an enemy could answer) or "danger" (the home guard would be gone).</param>
public sealed record HomeRisk(int Remaining, string Level, string Text);

/// <summary>The outcome of confirming an attack: it either happened completely (war if needed, plus the march) or not at all.</summary>
public sealed record AttackResult(bool Ok, string Message, bool WarDeclared = false, MarchingArmy? March = null);

/// <summary>
/// The player's attack as ONE confirmed action: choose a hostile country, choose how many soldiers to send, confirm — the war is
/// declared (if none is on) and the army sets out together. Every rule is checked before anything changes, so a refused attack
/// declares no war and takes no soldier from home.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>Days before war can be declared again on the same country (the Attack button's long-standing cooldown).</summary>
    public const int AttackCooldownDays = 30;

    /// <summary>
    /// Why the player cannot attack a country at all right now, whatever the army: it does not exist, a victory over it awaits its fate,
    /// a treaty or protection forbids it, or the declaration is on cooldown. Null = the attack is allowed. A war already on is not an
    /// obstacle (the army is simply sent into it).
    /// </summary>
    public string? AttackBlock(string nationId)
    {
        var target = State.OtherNations.FirstOrDefault(n => n.Id == nationId);
        if (target is null) return "Nation not found.";
        if (target.IsEliminated) return $"{target.Name} no longer exists.";
        if (PendingVictory(target.Id) is not null)
            return $"You have already beaten {target.Name}: decide its fate (annex, take its resources or let it go) before sending another army.";
        if (!target.AtWarWithPlayer)
        {
            // A non-aggression pact, an alliance or a guarantee we gave forbids declaring war.
            if (TreatyService.ForbidsAttack(State, State.PlayerNation.Id, target.Id) is { } forbidden) return forbidden;
            if (TreatyService.WarDeclarationBlock(State) is { } warBanned) return warBanned;
            if (CooldownMessage("attack", target) is { } wait) return $"You cannot declare war on {target.Name} again yet. {wait}";
        }
        // An Assembly ban means the country could not be taken: the army is not sent.
        if (TreatyService.AnnexationBlock(State, target.Id) is { } protectedBy)
            return $"{protectedBy} An invasion could not take the country.";
        return null;
    }

    /// <summary>Why the player cannot send this many soldiers (null = the number is fine). Checked before war is declared.</summary>
    public string? TroopsBlock(int troops)
    {
        int home = State.PlayerNation.Soldiers;
        if (troops <= 0) return "Choose how many soldiers to send.";
        if (troops > home) return $"You only have {home:N0} soldiers at home — you cannot send {troops:N0}.";
        if (troops < Balance.MinInvasionForce)
            return $"An army needs at least {Balance.MinInvasionForce:N0} soldiers to march (you chose {troops:N0}).";
        return null;
    }

    /// <summary>Everything the Attack popup shows for a target (null when the country does not exist).</summary>
    public AttackPlan? PlanAttack(string nationId)
    {
        var target = State.OtherNations.FirstOrDefault(n => n.Id == nationId);
        if (target is null) return null;
        var player = State.PlayerNation;
        var route = MapGeography.Route(player, target);
        string? reason = AttackBlock(nationId);
        if (reason is null && player.Soldiers < Balance.MinInvasionForce)
            reason = $"You have {player.Soldiers:N0} soldiers at home; an army needs at least {Balance.MinInvasionForce:N0} to march.";
        return new AttackPlan(target.Id, target.Name, target.AtWarWithPlayer, player.Soldiers, Balance.MinInvasionForce,
            route.Days, State.CurrentDate.AddDays(route.Days), route.Distance, MapGeography.FallbackNote(player, target, route),
            target.Soldiers, reason);
    }

    /// <summary>
    /// How exposed the home country would be with <paramref name="troops"/> soldiers away. Sending troops leaves fewer at home, and an
    /// enemy that outnumbers the home army by enough sends half of its own army against the capital (see the daily diplomacy step).
    /// </summary>
    public HomeRisk HomeRiskIfSent(string nationId, int troops)
    {
        var player = State.PlayerNation;
        int remaining = Math.Max(0, player.Soldiers - Math.Max(0, troops));
        const string general = "Soldiers you send away cannot defend your home while they march and fight.";

        if (remaining < Balance.MinHomeGuard)
            return new HomeRisk(remaining, "danger",
                $"Only {remaining:N0} soldiers would stay at home — below the home guard of {Balance.MinHomeGuard:N0}. Your country could be overrun while the army is away. {general}");

        var threat = State.OtherNations
            .Where(n => !n.IsEliminated && (n.AtWarWithPlayer || n.Id == nationId))
            .OrderByDescending(n => n.Soldiers)
            .FirstOrDefault();
        if (threat is not null && threat.Soldiers > remaining * Balance.AiInvasionArmyRatio)
            return new HomeRisk(remaining, "caution",
                $"{threat.Name} ({threat.Soldiers:N0} soldiers) would outnumber the {remaining:N0} left at home, and could answer with an invasion of about {threat.Soldiers / 2:N0} soldiers. {general}");

        return new HomeRisk(remaining, "ok", $"{remaining:N0} soldiers would stay at home. {general}");
    }

    /// <summary>
    /// The confirmed attack: validate everything (target, treaties, protection, troop count), and only then declare war (if none is on)
    /// and send the army — together. If any check fails nothing happens: no war is declared and no soldier leaves home.
    /// </summary>
    public AttackResult ConfirmAttack(string nationId, int troops)
    {
        if (AttackBlock(nationId) is { } blocked) return new AttackResult(false, blocked);
        if (TroopsBlock(troops) is { } badTroops) return new AttackResult(false, badTroops);

        var target = State.OtherNations.First(n => n.Id == nationId);
        bool declare = !target.AtWarWithPlayer;
        if (declare)
        {
            // (All the checks passed above, so this cannot refuse; if it ever did, no soldier has left home yet.)
            if (DeclareWar(nationId) is { } refused) return new AttackResult(false, refused);
            StartCooldown("attack", target, AttackCooldownDays);
        }

        var march = StartMarch(target, troops);
        StateChanged?.Invoke();
        string went = $"{march.Strength:N0} soldiers march on {target.Name} — arrival in {march.TotalDays} days ({march.ArrivalDate:dd-MM-yyyy}).";
        return new AttackResult(true, declare ? $"War declared on {target.Name}! {went}" : went, declare, march);
    }
}
