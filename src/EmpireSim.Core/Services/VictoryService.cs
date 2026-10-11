using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// What happens after a war is won. When the PLAYER beats a country in battle the country is not annexed at once:
/// the player gets a pending decision with three choices, each with its own flow —
/// <see cref="VictoryChoice.Annex"/> (the country is absorbed), <see cref="VictoryChoice.Resources"/> (a share of its treasury
/// and stocks is taken and it survives, resentful) and <see cref="VictoryChoice.Nothing"/> (white peace, it is grateful).
/// AI winners choose among the same three by what their war was for (<see cref="ResolveForAi"/>).
/// </summary>
public static class VictoryService
{
    public static VictoryDecision? Pending(GameState state, string loserId) =>
        state.PendingVictories.FirstOrDefault(v => v.LoserId == loserId);

    /// <summary>
    /// The player won a battle against <paramref name="loser"/>: open the decision (once per country).
    /// The country stays formally at war with the player, but it is beaten and does nothing until the player decides.
    /// </summary>
    public static void Begin(GameState state, Nation winner, Nation loser)
    {
        winner.BattlesWon++;   // the battle is won whatever is decided afterwards
        if (Pending(state, loser.Id) is not null) return;

        state.PendingVictories.Add(new VictoryDecision
        {
            LoserId = loser.Id,
            LoserName = loser.Name,
            WonDate = state.CurrentDate,
            ExpiresDate = state.CurrentDate.AddDays(Balance.VictoryDecisionDays),
        });
        state.LogMovement(MovementKind.War, MovementStatus.UnderWay, winner, loser,
            $"🏆 Victory over {loser.Name}! Choose: annex it, take its resources, or let it go (within {Balance.VictoryDecisionDays} days).", inbox: InboxTopic.Victory);
    }

    /// <summary>Carries out the player's choice for a pending victory. Nothing happens if the choice is refused.</summary>
    public static DipResult Resolve(GameState state, string loserId, VictoryChoice choice, bool expired = false)
    {
        var decision = Pending(state, loserId);
        if (decision is null) return DipResult.Invalid("There is no victory to claim over that country.");
        var winner = state.PlayerNation;
        var loser = state.OtherNations.FirstOrDefault(n => n.Id == loserId);
        if (loser is null || loser.IsEliminated)
        {
            state.PendingVictories.Remove(decision);
            return DipResult.Invalid($"{decision.LoserName} no longer exists.");
        }

        switch (choice)
        {
            case VictoryChoice.Annex:
                // Flow 1: the whole country becomes the winner's. Refused if the Assembly or a guarantee protects it.
                if (TreatyService.AnnexationBlock(state, loser.Id) is { } block) return DipResult.Invalid(block);
                state.PendingVictories.Remove(decision);
                Warfare.AnnexNation(state, winner, loser, byBattle: false);
                return DipResult.Done($"{loser.Name} is annexed: its people, treasury and lands are now yours.");

            case VictoryChoice.Resources:
            {
                // Flow 2: take a share of its wealth; the country survives, at peace but resentful.
                state.PendingVictories.Remove(decision);
                var (gold, goods) = TakeSpoils(state, winner, loser);
                EndWar(loser, Balance.VictorySpoilsMaxRating);
                state.LogMovement(MovementKind.War, MovementStatus.Completed, winner, loser,
                    $"You made peace with {loser.Name} after taking its resources.");
                return DipResult.Done(
                    $"You took {Currency.Cost(gold)} and {goods:N0} units of goods from {loser.Name}. The war is over; they will not forget it.");
            }

            default:
                // Flow 3: nothing taken — a white peace, and the country is grateful.
                state.PendingVictories.Remove(decision);
                EndWar(loser, Balance.VictoryMercyRating);
                state.LogMovement(MovementKind.War, MovementStatus.Completed, winner, loser,
                    expired ? $"No decision was made: {loser.Name} was let go with nothing taken."
                            : $"You let {loser.Name} go: a white peace, nothing taken.");
                return DipResult.Done($"{loser.Name} is let go. The war is over and nothing was taken.");
        }
    }

    /// <summary>Once a day: a victory left undecided for too long ends as "nothing".</summary>
    public static void Advance(GameState state)
    {
        foreach (var v in state.PendingVictories.ToList())
            if (state.CurrentDate >= v.ExpiresDate)
                Resolve(state, v.LoserId, VictoryChoice.Nothing, expired: true);
    }

    /// <summary>
    /// An AI winner settles the war the way the player would — annex, take resources, or let go — according to what the war was
    /// for (<see cref="WarAim"/>), with nothing about the two countries' relative power, how lately it last won, or how many
    /// annexations the world has seen. Independent wars end in independent outcomes.
    /// <list type="bullet">
    /// <item><b>Conquest</b>: the country is annexed — unless the Assembly or a sovereignty guarantee forbids it (a real game rule),
    /// in which case the battle is won but the war goes on; nothing is taken instead.</item>
    /// <item><b>Humiliation</b>: the winner takes a share of the loser's wealth and the war ends.</item>
    /// <item><b>Containment</b>: the threat is beaten back; a white peace.</item>
    /// </list>
    /// </summary>
    public static void ResolveForAi(GameState state, Nation winner, Nation loser)
    {
        var war = state.Wars.FirstOrDefault(w => w.Links(winner.Id, loser.Id));
        var aim = AiWorldService.AimOf(state, winner, loser, war);

        switch (aim)
        {
            case WarAim.Conquest:
                if (TreatyService.AnnexationBlock(state, loser.Id) is { } block)
                {
                    state.LogMovement(MovementKind.War, MovementStatus.Failed, winner, loser,
                        $"{winner.Name} won the battle for {loser.Name} but cannot annex it ({block}) — the war goes on.", inbox: InboxTopic.AllyAtWar);
                    return;
                }
                // It only interrupts the player when it touches their side (asked before the annexation ends the war and the treaties).
                bool touchesPlayer = loser.AtWarWithPlayer || state.IsPlayerAlly(loser.Id);
                Warfare.AnnexNation(state, winner, loser);
                if (touchesPlayer)
                    state.ActiveWarnings.Add($"🏳 {loser.Name} has been annexed by {winner.Name}!");
                return;

            case WarAim.Humiliation:
                TakeSpoils(state, winner, loser);
                state.LogMovement(MovementKind.War, MovementStatus.Completed, winner, loser,
                    $"{winner.Name} beat {loser.Name} and took its resources: the rival is humbled.", inbox: InboxTopic.AllyAtWar);
                break;

            default:
                state.LogMovement(MovementKind.War, MovementStatus.Completed, winner, loser,
                    $"{winner.Name} beat back {loser.Name}: the threat is broken and it asks for nothing more.", inbox: InboxTopic.AllyAtWar);
                break;
        }
        AiWorldService.EndWarBetween(state, winner, loser);
    }

    /// <summary>
    /// Takes <see cref="Balance.VictorySpoilsFraction"/> of the loser's gold, minerals, manufactured goods and food/goods
    /// stocks. The amounts move: the loser loses exactly what the winner gains.
    /// </summary>
    public static (double Gold, double Goods) TakeSpoils(GameState state, Nation winner, Nation loser)
    {
        double share = Balance.VictorySpoilsFraction;
        double gold = Math.Floor(loser.Gold * share);
        loser.Gold -= gold;
        winner.Gold += gold;

        double goods = 0;
        foreach (var mineral in new[] { "Wood", "Stone", "Iron", "Copper", "Lead" })
        {
            double amount = Math.Floor(loser.GetProduct(mineral) * share);
            if (amount <= 0) continue;
            loser.AddProduct(mineral, -amount);
            winner.AddProduct(mineral, amount);
            goods += amount;
        }
        double manufactured = Math.Floor(loser.Goods * share);
        loser.Goods -= manufactured;
        winner.Goods += manufactured;
        goods += manufactured;
        foreach (var (item, stock) in loser.GoodsInventory.ToList())
        {
            double amount = Math.Floor(stock * share);
            if (amount <= 0) continue;
            loser.AddGood(item, -amount);
            winner.AddGood(item, amount);
            goods += amount;
        }

        // Spoils are a result of war: military news, although what moves is gold and goods.
        if (gold > 0)
            state.LogMovement(MovementKind.Gold, MovementStatus.Completed, loser, winner, $"{winner.Name} took {Currency.Cost(gold)} from {loser.Name}.", military: true);
        if (goods > 0)
            state.LogMovement(MovementKind.Goods, MovementStatus.Completed, loser, winner, $"{winner.Name} took {goods:N0} units of goods from {loser.Name}.", military: true);
        return (gold, goods);
    }

    /// <summary>Ends the war with a beaten country and sets how it regards the player afterwards (0-100 scale).</summary>
    private static void EndWar(Nation loser, int rating)
    {
        loser.AtWarWithPlayer = false;
        loser.HasTradePactWithPlayer = false;
        loser.RelationToPlayer = DiplomacyService.FromDisplayRating(rating);
    }
}
