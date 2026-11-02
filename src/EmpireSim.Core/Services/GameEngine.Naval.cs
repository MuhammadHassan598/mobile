using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

public sealed partial class GameEngine
{
    // =========================================================================
    // NAVAL STRATEGIC WARFARE & FLEET OPERATIONS
    // =========================================================================

    /// <summary>Warships docked in home port and available for mission deployment.</summary>
    public int AvailableWarships => State.AvailableWarships;

    /// <summary>Warships actively deployed across all ongoing naval missions.</summary>
    public int DeployedWarships => State.DeployedWarships;

    /// <summary>Active naval missions.</summary>
    public List<NavalMission> ActiveNavalMissions =>
        State.NavalMissions.Where(m => m.Status == NavalMissionStatus.Active).ToList();

    /// <summary>
    /// Deploys warships to institute an economic and naval blockade on an enemy nation at war.
    /// </summary>
    public string? DeployNavalBlockade(string targetNationId, int warships)
    {
        var target = FindNation(targetNationId);
        if (target is null) return "Nation not found.";
        if (!target.AtWarWithPlayer) return $"You can only blockade nations you are actively at war with ({target.Name} is not at war with you).";
        if (warships < Balance.NavalBlockadeMinWarships)
            return $"Blockading a coastline requires at least {Balance.NavalBlockadeMinWarships} warships.";
        if (warships > State.AvailableWarships)
            return $"Only {State.AvailableWarships} warships available in home port.";

        var mission = new NavalMission
        {
            OwnerId = State.PlayerNation.Id,
            TargetNationId = target.Id,
            Type = NavalMissionType.Blockade,
            Warships = warships,
            StartDate = State.CurrentDate,
            Status = NavalMissionStatus.Active
        };
        State.NavalMissions.Add(mission);

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, State.PlayerNation, target,
            $"Dispatched {warships:N0} warships to enforce a naval blockade on {target.Name}.", military: true, inbox: InboxTopic.ArmyMovement);
        State.ActiveWarnings.Add($"⚓ Enforcing naval blockade on {target.Name} with {warships:N0} warships.");

        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// Deploys warships to patrol imperial shipping lanes and escort merchant trade convoys.
    /// </summary>
    public string? DeployNavalPatrol(int warships)
    {
        if (warships < Balance.NavalPatrolMinWarships)
            return $"Patrolling imperial shipping lanes requires at least {Balance.NavalPatrolMinWarships} warships.";
        if (warships > State.AvailableWarships)
            return $"Only {State.AvailableWarships} warships available in home port.";

        var mission = new NavalMission
        {
            OwnerId = State.PlayerNation.Id,
            TargetNationId = State.PlayerNation.Id,
            Type = NavalMissionType.Patrol,
            Warships = warships,
            StartDate = State.CurrentDate,
            Status = NavalMissionStatus.Active
        };
        State.NavalMissions.Add(mission);

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, State.PlayerNation, State.PlayerNation,
            $"Dispatched {warships:N0} warships to patrol imperial shipping lanes and protect merchant convoys.", military: true, inbox: InboxTopic.ArmyMovement);

        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// Issues royal letters of marque to dispatch privateer corsairs against a rival's merchant shipping.
    /// </summary>
    public string? DeployPrivateering(string targetNationId, int warships)
    {
        var target = FindNation(targetNationId);
        if (target is null) return "Nation not found.";
        if (target.Id == State.PlayerNation.Id) return "Cannot privateer against your own crown.";
        if (TreatyService.ForbidsAttack(State, State.PlayerNation.Id, target.Id) is { } forbidden)
            return forbidden;
        if (warships < Balance.NavalPrivateerMinWarships)
            return $"Commissioning privateers requires at least {Balance.NavalPrivateerMinWarships} warships.";
        if (warships > State.AvailableWarships)
            return $"Only {State.AvailableWarships} warships available in home port.";

        var mission = new NavalMission
        {
            OwnerId = State.PlayerNation.Id,
            TargetNationId = target.Id,
            Type = NavalMissionType.Privateering,
            Warships = warships,
            StartDate = State.CurrentDate,
            Status = NavalMissionStatus.Active
        };
        State.NavalMissions.Add(mission);

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, State.PlayerNation, target,
            $"Issued royal letters of marque to {warships:N0} privateers against {target.Name}.", military: true, inbox: InboxTopic.ArmyMovement);

        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// Recalls an active naval squadron back to home dockyards.
    /// </summary>
    public string? RecallNavalMission(string missionId)
    {
        var mission = State.NavalMissions.FirstOrDefault(m => m.Id == missionId && m.Status == NavalMissionStatus.Active);
        if (mission is null) return "Mission not found or no longer active.";

        mission.Status = NavalMissionStatus.Recalled;
        string role = mission.Type switch
        {
            NavalMissionType.Blockade => "blockade squadron",
            NavalMissionType.Patrol => "patrol fleet",
            NavalMissionType.Privateering => "privateer fleet",
            _ => "naval squadron"
        };

        var target = FindNation(mission.TargetNationId);
        string targetText = target is not null && target.Id != State.PlayerNation.Id ? $" from {target.Name}" : "";

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, State.PlayerNation, State.PlayerNation,
            $"Recalled {mission.Warships:N0} warships ({role}){targetText} back to imperial dockyards.", military: true, inbox: InboxTopic.ArmyMovement);

        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// Simulates a high-seas naval engagement between two opposing fleets.
    /// </summary>
    public NavalEngagementResult ResolveNavalBattle(Nation attacker, Nation defender, int attackerShips, int defenderShips)
    {
        double attMult = CommandStaffService.NavalCombatMult(attacker);
        double defMult = CommandStaffService.NavalCombatMult(defender);

        double attPower = attackerShips * attMult * (1.0 + _sim.NextDouble() * 0.2);
        double defPower = defenderShips * defMult * (1.0 + _sim.NextDouble() * 0.2);

        bool attackerWon = attPower >= defPower;
        int attLosses, defLosses, prizes = 0;

        if (attackerWon)
        {
            double ratio = Math.Clamp(attPower / Math.Max(1.0, defPower), 1.0, 3.0);
            defLosses = Math.Clamp((int)Math.Ceiling(defenderShips * (0.4 + 0.2 * (ratio - 1))), 1, defenderShips);
            attLosses = Math.Clamp((int)Math.Floor(attackerShips * (0.2 / ratio)), 0, attackerShips - 1);

            int survivingDef = defenderShips - defLosses;
            prizes = (int)Math.Round(survivingDef * Balance.NavalPrizeCaptureChance);
        }
        else
        {
            double ratio = Math.Clamp(defPower / Math.Max(1.0, attPower), 1.0, 3.0);
            attLosses = Math.Clamp((int)Math.Ceiling(attackerShips * (0.4 + 0.2 * (ratio - 1))), 1, attackerShips);
            defLosses = Math.Clamp((int)Math.Floor(defenderShips * (0.2 / ratio)), 0, defenderShips - 1);

            int survivingAtt = attackerShips - attLosses;
            prizes = (int)Math.Round(survivingAtt * Balance.NavalPrizeCaptureChance);
        }

        // Apply ship losses
        attacker.Warships = Math.Max(0, attacker.Warships - attLosses);
        defender.Warships = Math.Max(0, defender.Warships - defLosses);

        // Apply prize captures
        if (attackerWon && prizes > 0)
        {
            defender.Warships = Math.Max(0, defender.Warships - prizes);
            attacker.Warships += prizes;
        }
        else if (!attackerWon && prizes > 0)
        {
            attacker.Warships = Math.Max(0, attacker.Warships - prizes);
            defender.Warships += prizes;
        }

        string victor = attackerWon ? attacker.Name : defender.Name;
        string loser = attackerWon ? defender.Name : attacker.Name;
        int vLoss = attackerWon ? attLosses : defLosses;
        int lLoss = attackerWon ? defLosses : attLosses;
        string prizeText = prizes > 0 ? $" and captured {prizes:N0} prize warships!" : ".";

        string summary = $"Naval Battle: {victor} defeated {loser}! {victor} lost {vLoss:N0} ships; {loser} lost {lLoss:N0} ships{prizeText}";

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, attacker, defender, summary, military: true, inbox: InboxTopic.Battle);

        StateChanged?.Invoke();

        return new NavalEngagementResult
        {
            AttackerId = attacker.Id,
            DefenderId = defender.Id,
            AttackerShips = attackerShips,
            DefenderShips = defenderShips,
            AttackerLosses = attLosses,
            DefenderLosses = defLosses,
            PrizesCaptured = prizes,
            AttackerWon = attackerWon,
            Summary = summary
        };
    }
}
