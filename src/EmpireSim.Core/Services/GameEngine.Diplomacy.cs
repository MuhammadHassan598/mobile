using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The diplomatic actions of the country panel (treaties, military cooperation, gifts,
/// colonies, missionaries). Every action has ONE validator ("why not?") that both the
/// button state (<see cref="CheckDiplomaticAction"/>) and the action itself use, so the UI
/// can never offer something the engine would refuse. Nothing is charged for a refused
/// or invalid action.
/// </summary>
public sealed partial class GameEngine
{
    // ---------------- Shared helpers ----------------

    private int Rating(Nation n) => DiplomacyService.ToDisplayRating(n.RelationToPlayer);

    private string? CooldownMessage(string action, Nation t)
    {
        if (State.PlayerNation.DiplomacyCooldowns.TryGetValue($"{action}_{t.Id}", out var until)
            && State.CurrentDate < until)
            return $"Available again in {until.DayNumber - State.CurrentDate.DayNumber} days.";
        return null;
    }

    private void StartCooldown(string action, Nation t, int days) =>
        State.PlayerNation.DiplomacyCooldowns[$"{action}_{t.Id}"] = State.CurrentDate.AddDays(days);

    /// <summary>Common gate for friendly actions: found, alive, at peace, embassy, rating, cooldown, gold — in that order.</summary>
    private string? Gate(Nation? t, int minRating, string? cooldownAction = null, double cost = 0, bool needEmbassy = false)
    {
        if (t is null) return "Nation not found.";
        if (t.IsEliminated) return $"{t.Name} no longer exists.";
        if (t.AtWarWithPlayer) return $"You are at war with {t.Name}.";
        if (needEmbassy && !TreatyService.HasEmbassy(State, State.PlayerNation.Id, t.Id))
            return $"Needs an embassy in {t.Name} first.";
        int r = Rating(t);
        if (r < minRating) return $"{t.Name} regards you too poorly ({r}; needs {minRating}).";
        if (cooldownAction is not null && CooldownMessage(cooldownAction, t) is { } cd) return cd;
        if (cost > 0 && !State.PlayerNation.CanPay(cost))
            return $"Needs {Currency.Cost(cost)} (you have {Currency.Cost(State.PlayerNation.Gold)}).";
        return null;
    }

    /// <summary>Soldiers the player could send or give away while keeping the home guard.</summary>
    public int SpareSoldiers => Math.Max(0, State.PlayerNation.Soldiers - Balance.MinHomeGuard);

    /// <summary>Why the player cannot declare war on a nation because of a treaty (null = no treaty objection).</summary>
    public string? WarBlockReason(string nationId) =>
        TreatyService.ForbidsAttack(State, State.PlayerNation.Id, nationId);

    /// <summary>Countries the player is at war with (candidates for a Call to Arms).</summary>
    public IEnumerable<Nation> Enemies(string? exceptId = null) =>
        State.OtherNations.Where(n => n.AtWarWithPlayer && !n.IsEliminated && n.Id != exceptId);

    /// <summary>Colonies the player currently owns.</summary>
    public IEnumerable<Colony> OwnedColonies =>
        State.Colonies.Where(c => c.OwnerId == State.PlayerNation.Id);

    /// <summary>Resources that can be asked for as aid.</summary>
    public static readonly string[] AidResources = { "Gold", "Wood", "Stone", "Iron", "Wheat" };

    // ---------------- Availability (drives the buttons) ----------------

    /// <summary>
    /// Whether an action can be used on a country right now, why not if it cannot, and what it costs.
    /// Rules only — an AI country's decision (accept / decline) is made when you actually ask.
    /// </summary>
    public DipStatus CheckDiplomaticAction(string actionId, string nationId)
    {
        var spec = DiplomaticActionCatalog.Get(actionId);
        if (spec is null) return new DipStatus(false, "Unknown action.", "");
        if (spec.BlockedReason is not null) return new DipStatus(false, spec.BlockedReason, "");

        var t = FindNation(nationId);
        string? why = actionId switch
        {
            DiplomaticActionCatalog.Embassy => WhyEmbassy(t),
            DiplomaticActionCatalog.NonAggression => WhyNonAggression(t),
            DiplomaticActionCatalog.Alliance => WhyAlliance(t),
            DiplomaticActionCatalog.Trade => WhyTradeAgreement(t),
            DiplomaticActionCatalog.SendTroops => WhySendTroops(t),
            DiplomaticActionCatalog.CallToArms => WhyCallToArms(t),
            DiplomaticActionCatalog.GiveArmy => WhyGiveArmy(t),
            DiplomaticActionCatalog.Gift => WhyGift(t),
            DiplomaticActionCatalog.Improve => WhyImprove(t),
            DiplomaticActionCatalog.Aid => WhyAid(t),
            DiplomaticActionCatalog.PresentColony => WhyPresentColony(t),
            DiplomaticActionCatalog.Missionary => WhyMissionary(t),
            DiplomaticActionCatalog.Research => WhyResearchContract(t),
            DiplomaticActionCatalog.Sovereignty => WhySovereignty(t),
            DiplomaticActionCatalog.AskAttack => WhyAskAttack(t),
            DiplomaticActionCatalog.Annex => WhyAnnex(t),
            _ => "Unknown action."
        };
        return new DipStatus(why is null, why ?? "", CostText(actionId));
    }

    private static string CostText(string actionId) => actionId switch
    {
        DiplomaticActionCatalog.Embassy => Currency.Cost(Balance.EmbassyCost),
        DiplomaticActionCatalog.NonAggression => Currency.Cost(Balance.NapCost),
        DiplomaticActionCatalog.Alliance => Currency.Cost(Balance.AllianceCost),
        DiplomaticActionCatalog.Trade => Currency.Cost(Balance.TradeAgreementCost),
        DiplomaticActionCatalog.Improve => Currency.Cost(Balance.ImproveRelationsCost),
        DiplomaticActionCatalog.Missionary => Currency.Cost(Balance.MissionaryCost),
        DiplomaticActionCatalog.Research => Currency.Cost(Balance.ResearchContractCost),
        DiplomaticActionCatalog.Sovereignty => Currency.Cost(Balance.SovereigntyCost),
        DiplomaticActionCatalog.Annex => "free",
        DiplomaticActionCatalog.Gift => $"from {Currency.Cost(Balance.GiftMinAmount)}",
        _ => "free"
    };

    // ---------------- Treaties ----------------

    private string? WhyEmbassy(Nation? t)
    {
        if (t is not null && TreatyService.HasEmbassy(State, State.PlayerNation.Id, t.Id))
            return $"You already have an embassy in {t.Name}.";
        return Gate(t, Balance.EmbassyMinRating, "embassy", Balance.EmbassyCost);
    }

    private string? WhyNonAggression(Nation? t)
    {
        if (t is not null && TreatyService.Has(State, TreatyType.NonAggression, State.PlayerNation.Id, t.Id))
            return $"A non-aggression pact with {t.Name} is already in force.";
        return Gate(t, Balance.NapMinRating, "nap", Balance.NapCost, needEmbassy: true);
    }

    private string? WhyAlliance(Nation? t)
    {
        if (t is not null && TreatyService.Has(State, TreatyType.DefensiveAlliance, State.PlayerNation.Id, t.Id))
            return $"You are already allied with {t.Name}.";
        return Gate(t, Balance.AllianceMinRating, "alliance", Balance.AllianceCost, needEmbassy: true);
    }

    private string? WhyTradeAgreement(Nation? t)
    {
        if (t is not null && (t.HasTradePactWithPlayer
                              || TreatyService.Has(State, TreatyType.TradeAgreement, State.PlayerNation.Id, t.Id)))
            return $"A trade agreement with {t.Name} is already in force.";
        return Gate(t, Balance.TradeAgreementMinRating, "trade", Balance.TradeAgreementCost, needEmbassy: true);
    }

    /// <summary>
    /// Asks a country to sign a treaty. Rules are checked first (nothing charged); then the AI weighs its attitude
    /// toward the player against the treaty's threshold. A refusal stings relations and starts a cooldown;
    /// only an accepted treaty costs gold.
    /// </summary>
    private DipResult Propose(Nation t, string action, TreatyType type, double cost, int acceptScore, int? days, string doneText)
    {
        int score = TreatyService.Score(State, t);
        if (score < acceptScore)
        {
            DiplomacyService.AddRating(t, -Balance.ProposalRejectedRatingPenalty);
            StartCooldown(action, t, Balance.ProposalCooldownDays);
            State.LogMovement(type == TreatyType.Embassy ? MovementKind.Mission : MovementKind.Treaty, MovementStatus.Failed, State.PlayerNation, t,
                $"{t.Name} declined our proposal of {TreatyService.WithArticle(type)}.", inbox: InboxTopic.Diplomacy);
            StateChanged?.Invoke();
            return DipResult.Rejected(
                $"{t.Name} declines: they do not trust you enough yet ({score} of {acceptScore} needed). " +
                $"Gifts, an embassy and a shared faith help. (-{Balance.ProposalRejectedRatingPenalty} relations)");
        }

        State.PlayerNation.PayGold(cost);
        TreatyService.Add(State, type, State.PlayerNation, t, days);
        State.LogMovement(type == TreatyType.Embassy ? MovementKind.Mission : MovementKind.Treaty, MovementStatus.Completed, State.PlayerNation, t,
            $"Signed {TreatyService.WithArticle(type)} with {t.Name}.", inbox: InboxTopic.Diplomacy);
        StateChanged?.Invoke();
        return DipResult.Done(doneText);
    }

    public DipResult EstablishEmbassy(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyEmbassy(t) is { } why) return DipResult.Invalid(why);
        return Propose(t!, "embassy", TreatyType.Embassy, Balance.EmbassyCost, Balance.EmbassyAcceptScore, null,
            $"{t!.Name} welcomes your embassy. Relations will warm a little every day.");
    }

    public DipResult ProposeNonAggression(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyNonAggression(t) is { } why) return DipResult.Invalid(why);
        return Propose(t!, "nap", TreatyType.NonAggression, Balance.NapCost, Balance.NapAcceptScore, Balance.NapDays,
            $"Non-aggression pact signed with {t!.Name} for {Balance.NapDays} days.");
    }

    public DipResult ProposeAlliance(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyAlliance(t) is { } why) return DipResult.Invalid(why);
        return Propose(t!, "alliance", TreatyType.DefensiveAlliance, Balance.AllianceCost, Balance.AllianceAcceptScore, null,
            $"{t!.Name} is now your defensive ally.");
    }

    private string? WhyResearchContract(Nation? t)
    {
        var player = State.PlayerNation;
        if (t is not null && TreatyService.Has(State, TreatyType.ResearchContract, player.Id, t.Id))
            return $"You already have a research contract with {t.Name}.";
        if (ResearchService.Contracts(State).Count >= Balance.ResearchContractMax)
            return $"You already hold {Balance.ResearchContractMax} research contracts (the most allowed).";
        return Gate(t, Balance.ResearchContractMinRating, "research", Balance.ResearchContractCost, needEmbassy: true);
    }

    /// <summary>Signs a research contract: for a year the partner hands you part of its daily research points.</summary>
    public DipResult ProposeResearchContract(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyResearchContract(t) is { } why) return DipResult.Invalid(why);
        double perDay = ResearchService.DailyPoints(t!) * Balance.ResearchContractShare;
        return Propose(t!, "research", TreatyType.ResearchContract, Balance.ResearchContractCost, Balance.ResearchContractAcceptScore,
            Balance.ResearchContractDays,
            $"{t!.Name} will share {Balance.ResearchContractShare:P0} of its research with you for {Balance.ResearchContractDays} days (+{perDay:0.0} points a day).");
    }

    private string? WhySovereignty(Nation? t)
    {
        if (t is not null && TreatyService.Has(State, TreatyType.SovereigntyGuarantee, State.PlayerNation.Id, t.Id))
            return $"You already guarantee {t.Name}'s sovereignty.";
        return Gate(t, Balance.SovereigntyMinRating, "sovereignty", Balance.SovereigntyCost, needEmbassy: true);
    }

    /// <summary>
    /// Guarantees a country's independence for two years. Nobody may annex it (the same shield as an Assembly ban) and
    /// you may not attack it; it warms to you and trusts you more.
    /// </summary>
    public DipResult ProposeSovereigntyGuarantee(string nationId)
    {
        var t = FindNation(nationId);
        if (WhySovereignty(t) is { } why) return DipResult.Invalid(why);
        return Propose(t!, "sovereignty", TreatyType.SovereigntyGuarantee, Balance.SovereigntyCost, Balance.SovereigntyAcceptScore,
            Balance.SovereigntyDays,
            $"You now guarantee {t!.Name}'s sovereignty for {Balance.SovereigntyDays} days: nobody may annex it, and you may not attack it.");
    }

    public DipResult ProposeTradeAgreement(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyTradeAgreement(t) is { } why) return DipResult.Invalid(why);
        return Propose(t!, "trade", TreatyType.TradeAgreement, Balance.TradeAgreementCost, Balance.TradeAgreementAcceptScore, null,
            $"Trade agreement signed with {t!.Name}: cheaper imports, richer exports.");
    }

    /// <summary>Cancels one of the player's treaties with a country. Walking away costs relations.</summary>
    public DipResult CancelTreaty(TreatyType type, string nationId)
    {
        var t = FindNation(nationId);
        if (t is null) return DipResult.Invalid("Nation not found.");
        var treaty = TreatyService.Find(State, type, State.PlayerNation.Id, t.Id);
        if (treaty is null) return DipResult.Invalid($"There is no {TreatyService.TypeName(type)} with {t.Name}.");

        TreatyService.Remove(State, treaty);
        int penalty = type is TreatyType.NonAggression or TreatyType.DefensiveAlliance or TreatyType.SovereigntyGuarantee
            ? Balance.BreakPactRatingPenalty
            : Balance.BreakMinorTreatyRatingPenalty;
        DiplomacyService.AddRating(t, -penalty);
        State.LogMovement(type == TreatyType.Embassy ? MovementKind.Mission : MovementKind.Treaty, MovementStatus.Completed, State.PlayerNation, t,
            $"Cancelled the {TreatyService.TypeName(type)} with {t.Name} (-{penalty} relations).");
        StateChanged?.Invoke();
        return DipResult.Done($"The {TreatyService.TypeName(type)} with {t.Name} is cancelled. (-{penalty} relations)");
    }

    // ---------------- Military cooperation ----------------

    private string? WhySendTroops(Nation? t)
    {
        if (Gate(t, Balance.SendTroopsMinRating, needEmbassy: true) is { } why) return why;
        if (SpareSoldiers <= 0) return $"You must keep {Balance.MinHomeGuard:N0} soldiers at home — none to spare.";
        return null;
    }

    /// <summary>Lends soldiers to a friendly country for <see cref="Balance.TroopLoanDays"/> days. The soldiers move (never copied) and come back, minus losses.</summary>
    public DipResult SendTroops(string nationId, int count)
    {
        var t = FindNation(nationId);
        if (WhySendTroops(t) is { } why) return DipResult.Invalid(why);
        if (count < 1) return DipResult.Invalid("Send at least 1 soldier.");
        if (count > SpareSoldiers)
            return DipResult.Invalid($"You can spare at most {SpareSoldiers:N0} soldiers (home guard {Balance.MinHomeGuard:N0}).");

        int score = TreatyService.Score(State, t!);
        if (score < Balance.SendTroopsAcceptScore)
        {
            State.LogMovement(MovementKind.Troops, MovementStatus.Failed, State.PlayerNation, t!, $"{t!.Name} declined our offer of troops.", inbox: InboxTopic.Reinforcements);
            StateChanged?.Invoke();
            return DipResult.Rejected($"{t.Name} does not want foreign troops yet ({score} of {Balance.SendTroopsAcceptScore} needed).");
        }

        var loan = TreatyService.Lend(State, State.PlayerNation, t!, count, Balance.TroopLoanDays);
        if (loan is null) return DipResult.Invalid("No soldiers to send.");
        int gain = Math.Clamp(loan.Soldiers / 1000, 1, Balance.SendTroopsMaxGain);
        DiplomacyService.AddRating(t!, gain);
        State.LogMovement(MovementKind.Troops, MovementStatus.UnderWay, State.PlayerNation, t!,
            $"Lent {loan.Soldiers:N0} soldiers to {t!.Name} for {Balance.TroopLoanDays} days (+{gain} relations).");
        StateChanged?.Invoke();
        return DipResult.Done($"{loan.Soldiers:N0} soldiers join {t.Name}'s army for {Balance.TroopLoanDays} days. (+{gain} relations)");
    }

    private string? WhyCallToArms(Nation? t)
    {
        if (t is null) return "Nation not found.";
        if (t.IsEliminated) return $"{t.Name} no longer exists.";
        if (t.AtWarWithPlayer) return $"You are at war with {t.Name}.";
        if (!TreatyService.Has(State, TreatyType.DefensiveAlliance, State.PlayerNation.Id, t.Id))
            return $"{t.Name} is not your defensive ally.";
        if (!Enemies(t.Id).Any()) return "You are not at war with anyone else.";
        return CooldownMessage("calltoarms", t);
    }

    /// <summary>
    /// Asks a defensive ally to join the war against <paramref name="enemyId"/>. The ally decides; if it agrees it
    /// marches a share of its OWN army on the enemy through the normal march/battle system (nothing is copied or given).
    /// </summary>
    public DipResult CallToArms(string allyId, string enemyId)
    {
        var ally = FindNation(allyId);
        if (WhyCallToArms(ally) is { } why) return DipResult.Invalid(why);
        return JoinWar(ally!, enemyId, "calltoarms", Balance.CallToArmsAcceptScore, Balance.CallToArmsCooldownDays, isCall: true);
    }

    private string? WhyAskAttack(Nation? t)
    {
        if (t is null) return "Nation not found.";
        if (t.IsEliminated) return $"{t.Name} no longer exists.";
        if (t.AtWarWithPlayer) return $"You are at war with {t.Name}.";
        int rating = Rating(t);
        if (rating < Balance.AskAttackMinRating) return $"{t.Name} regards you too poorly ({rating}; needs {Balance.AskAttackMinRating}).";
        if (!Enemies(t.Id).Any()) return "You are not at war with anyone else.";
        return CooldownMessage("askattack", t);
    }

    /// <summary>
    /// Asks a friendly country (relations 70+, no alliance needed) to join the war against <paramref name="enemyId"/>.
    /// It decides exactly as an ally would: if it agrees it marches a share of its OWN army on the enemy.
    /// </summary>
    public DipResult AskToAttack(string nationId, string enemyId)
    {
        var t = FindNation(nationId);
        if (WhyAskAttack(t) is { } why) return DipResult.Invalid(why);
        return JoinWar(t!, enemyId, "askattack", Balance.AskAttackAcceptScore, Balance.AskAttackCooldownDays, isCall: false);
    }

    /// <summary>Shared by Call to Arms and Ask Attack: the country weighs the request and, if it agrees, marches its own soldiers.</summary>
    private DipResult JoinWar(Nation ally, string enemyId, string action, int acceptScore, int cooldownDays, bool isCall)
    {
        var enemy = FindNation(enemyId);
        if (enemy is null || enemy.IsEliminated || !enemy.AtWarWithPlayer || enemy.Id == ally.Id)
            return DipResult.Invalid("Choose a country you are at war with.");
        if (TreatyService.HasMarchOn(State, ally.Id, enemy.Id))
            return DipResult.Invalid($"{ally.Name} already has an army marching on {enemy.Name}.");

        int commit = (int)(ally.Soldiers * Balance.CallToArmsFraction);
        if (commit < Balance.MinInvasionForce)
            return DipResult.Invalid($"{ally.Name} has too few soldiers to send an army ({commit:N0}; needs {Balance.MinInvasionForce:N0}).");

        int score = TreatyService.Score(State, ally);
        bool strongEnough = ally.Soldiers >= enemy.Soldiers * Balance.CallToArmsMinStrengthRatio;
        if (score < acceptScore || !strongEnough)
        {
            StartCooldown(action, ally, Balance.ProposalCooldownDays);
            State.LogMovement(MovementKind.March, MovementStatus.Failed, ally, enemy,
                isCall ? $"{ally.Name} refused the call to arms against {enemy.Name}."
                       : $"{ally.Name} refused our request to attack {enemy.Name}.", inbox: InboxTopic.CallToArms);
            StateChanged?.Invoke();
            return DipResult.Rejected(strongEnough
                ? $"{ally.Name} refuses to join: they do not trust you enough ({score} of {acceptScore} needed)."
                : $"{ally.Name} refuses to join: {enemy.Name} is too strong for them.");
        }

        var march = TreatyService.LaunchMarch(State, ally, enemy, commit)!;
        StartCooldown(action, ally, cooldownDays);
        State.LogMovement(MovementKind.March, MovementStatus.UnderWay, ally, enemy,
            isCall ? $"📯 {ally.Name} answers the call: {commit:N0} soldiers march on {enemy.Name} ({march.DaysLeft} days)."
                   : $"📯 {ally.Name} agrees to attack {enemy.Name}: {commit:N0} soldiers march ({march.DaysLeft} days).", inbox: InboxTopic.CallToArms);
        StateChanged?.Invoke();
        return DipResult.Done($"{ally.Name} joins the war: {commit:N0} soldiers march on {enemy.Name}, arriving in {march.DaysLeft} days.");
    }

    private string? WhyGiveArmy(Nation? t)
    {
        if (Gate(t, Balance.GiveArmyMinRating) is { } why) return why;
        if (SpareSoldiers <= 0) return $"You must keep {Balance.MinHomeGuard:N0} soldiers at home — none to spare.";
        return null;
    }

    /// <summary>Permanently hands soldiers to a country. They are removed from your stacks as they are added to theirs.</summary>
    public DipResult GiveArmy(string nationId, int count)
    {
        var t = FindNation(nationId);
        if (WhyGiveArmy(t) is { } why) return DipResult.Invalid(why);
        if (count < 1) return DipResult.Invalid("Give at least 1 soldier.");
        if (count > SpareSoldiers)
            return DipResult.Invalid($"You can spare at most {SpareSoldiers:N0} soldiers (home guard {Balance.MinHomeGuard:N0}).");

        var force = ArmyHelper.ExtractSoldiers(State.PlayerNation, count);
        int given = force.Sum(s => s.Count);
        if (given <= 0) return DipResult.Invalid("No soldiers to give.");
        double power = ArmyHelper.ArmyPower(force);
        ArmyHelper.MergeStacks(t!, force);

        int gain = (int)Math.Clamp(power / 500, 1, Balance.GiveArmyMaxGain);
        DiplomacyService.AddRating(t!, gain);
        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, State.PlayerNation, t!, $"Gave {given:N0} soldiers to {t!.Name} (+{gain} relations).");
        StateChanged?.Invoke();
        return DipResult.Done($"{given:N0} soldiers now serve {t.Name}. (+{gain} relations)");
    }

    // ---------------- Gifts and relations ----------------

    private string? WhyGift(Nation? t) => Gate(t, 0, "gift", Balance.GiftMinAmount);

    /// <summary>Sends gold to a country's treasury; relations rise by <see cref="DiplomacyService.AidRelationshipGain"/>.</summary>
    public DipResult GiveGift(string nationId, double amount)
    {
        var t = FindNation(nationId);
        if (Gate(t, 0, "gift") is { } why) return DipResult.Invalid(why);
        if (amount < Balance.GiftMinAmount) return DipResult.Invalid($"A gift must be at least {Currency.Cost(Balance.GiftMinAmount)}.");
        if (!State.PlayerNation.CanPay(amount)) return DipResult.Invalid($"You only have {Currency.Cost(State.PlayerNation.Gold)}.");

        int gain = DiplomacyService.AidRelationshipGain(amount);
        State.PlayerNation.PayGold(amount);
        t!.Gold += amount;
        DiplomacyService.AddRating(t, gain);
        StartCooldown("gift", t, Balance.GiftCooldownDays);
        State.LogMovement(MovementKind.Gold, MovementStatus.Completed, State.PlayerNation, t, $"Sent a gift of {Currency.Cost(amount)} to {t.Name} (+{gain} relations).");
        StateChanged?.Invoke();
        return DipResult.Done($"{t.Name} accepts your gift of {Currency.Cost(amount)}. (+{gain} relations)");
    }

    private string? WhyImprove(Nation? t)
    {
        if (t is not null && !t.AtWarWithPlayer && !t.IsEliminated && Rating(t) >= Balance.ImproveRelationsMaxRating)
            return $"Relations with {t.Name} are already excellent ({Rating(t)}).";
        return Gate(t, 0, "improve", Balance.ImproveRelationsCost);
    }

    /// <summary>Sends an envoy: a fixed-cost mission that raises relations (more with an embassy). The gold is spent, not transferred.</summary>
    public DipResult ImproveRelations(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyImprove(t) is { } why) return DipResult.Invalid(why);

        bool embassy = TreatyService.HasEmbassy(State, State.PlayerNation.Id, t!.Id);
        int gain = embassy ? Balance.ImproveRelationsGainWithEmbassy : Balance.ImproveRelationsGain;
        State.PlayerNation.PayGold(Balance.ImproveRelationsCost);
        DiplomacyService.AddRating(t, gain);
        StartCooldown("improve", t, Balance.ImproveRelationsCooldownDays);
        State.LogMovement(MovementKind.Mission, MovementStatus.Completed, State.PlayerNation, t, $"Envoys to {t.Name} improved relations (+{gain}).");
        StateChanged?.Invoke();
        return DipResult.Done($"Your envoys charm the court of {t.Name}. (+{gain} relations)");
    }

    private string? WhyAid(Nation? t) => Gate(t, 0, "aid");

    /// <summary>
    /// Asks a country for a share of one of its resources. It agrees only if it trusts the player enough
    /// and actually holds some; the amount is taken from its stock and added to the player's.
    /// </summary>
    public DipResult AskForAid(string nationId, string resource)
    {
        var t = FindNation(nationId);
        if (WhyAid(t) is { } why) return DipResult.Invalid(why);
        string res = AidResources.FirstOrDefault(r => r.Equals(resource, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (res.Length == 0) return DipResult.Invalid("Choose Gold, Wood, Stone, Iron or Wheat.");

        int score = TreatyService.Score(State, t!);
        if (score < Balance.AidAcceptScore)
        {
            DiplomacyService.AddRating(t!, -Balance.ProposalRejectedRatingPenalty);
            StartCooldown("aid", t!, Balance.ProposalCooldownDays);
            State.LogMovement(res == "Gold" ? MovementKind.Gold : MovementKind.Goods, MovementStatus.Failed, t!, State.PlayerNation,
                $"{t!.Name} refused our request for {res}.", inbox: InboxTopic.Aid);
            StateChanged?.Invoke();
            return DipResult.Rejected(
                $"{t!.Name} refuses: they do not trust you enough to help ({score} of {Balance.AidAcceptScore} needed). (-{Balance.ProposalRejectedRatingPenalty} relations)");
        }

        double amount = Math.Floor(t!.GetProduct(res) * Balance.AidRequestFraction);
        if (amount < 1)
        {
            StartCooldown("aid", t, Balance.ProposalCooldownDays);
            State.LogMovement(res == "Gold" ? MovementKind.Gold : MovementKind.Goods, MovementStatus.Failed, t, State.PlayerNation,
                $"{t.Name} had no {res} to spare.", inbox: InboxTopic.Aid);
            StateChanged?.Invoke();
            return DipResult.Rejected($"{t.Name} has no {res} to spare.");
        }

        t.AddProduct(res, -amount);
        State.PlayerNation.AddProduct(res, amount);
        DiplomacyService.AddRating(t, -Balance.AidRequestRatingCost);
        StartCooldown("aid", t, Balance.AidCooldownDays);
        State.LogMovement(res == "Gold" ? MovementKind.Gold : MovementKind.Goods, MovementStatus.Completed, t, State.PlayerNation, $"{t.Name} sent {amount:N0} {res} in aid.", inbox: InboxTopic.Aid);
        StateChanged?.Invoke();
        return DipResult.Done($"{t.Name} sends {amount:N0} {res}. (-{Balance.AidRequestRatingCost} relations)");
    }

    // ---------------- Sovereignty and expansion ----------------

    private string? WhyPresentColony(Nation? t)
    {
        if (Gate(t, Balance.GiveArmyMinRating) is { } why) return why;
        if (!OwnedColonies.Any()) return "You own no colony to present (found one on the map first).";
        return null;
    }

    /// <summary>Transfers a colony you own, with the people and buildings it added to your nation, to another country.</summary>
    public DipResult PresentColonyTo(string nationId, string colonyId)
    {
        var t = FindNation(nationId);
        if (WhyPresentColony(t) is { } why) return DipResult.Invalid(why);

        var colony = State.Colonies.FirstOrDefault(c => c.Id == colonyId);
        if (colony is null) return DipResult.Invalid("Colony not found.");
        var player = State.PlayerNation;
        if (colony.OwnerId != player.Id) return DipResult.Invalid($"You do not own {colony.Name}.");
        if (player.Population < colony.Population || player.Farms < colony.Farms || player.Mines < colony.Mines)
            return DipResult.Invalid($"{colony.Name}'s people and buildings are no longer all yours to give.");

        player.Population -= colony.Population;
        player.Farms -= colony.Farms;
        player.Mines -= colony.Mines;
        t!.Population += colony.Population;
        t.Farms += colony.Farms;
        t.Mines += colony.Mines;
        colony.OwnerId = t.Id;

        DiplomacyService.AddRating(t, Balance.ColonyGiftRatingGain);
        State.LogMovement(MovementKind.Colony, MovementStatus.Completed, player, t,
            $"Presented the colony of {colony.Name} to {t.Name} (+{Balance.ColonyGiftRatingGain} relations).");
        StateChanged?.Invoke();
        return DipResult.Done($"{colony.Name} now belongs to {t.Name}. (+{Balance.ColonyGiftRatingGain} relations)");
    }

    private string? WhyMissionary(Nation? t)
    {
        if (t is null) return "Nation not found.";
        var player = State.PlayerNation;
        if (ReligionCatalog.GetByName(player.Religion) is null)
            return $"Your faith ({(string.IsNullOrEmpty(player.Religion) ? "none" : player.Religion)}) has no missionary doctrine in the religion catalogue.";
        if (string.IsNullOrEmpty(t.Religion)) return $"{t.Name} has no state religion to convert.";
        if (t.Religion.Equals(player.Religion, StringComparison.OrdinalIgnoreCase))
            return $"{t.Name} already follows {player.Religion}.";
        return Gate(t, Balance.MissionaryMinRating, "missionary", Balance.MissionaryCost);
    }

    /// <summary>
    /// Sends missionaries: adds religious influence over the country (devout rulers resist, tolerant ones welcome it,
    /// an embassy helps). At the threshold the country adopts the player's faith, and the existing religion effects
    /// (growth, prices, production, construction) then apply to it.
    /// </summary>
    public DipResult SendMissionary(string nationId)
    {
        var t = FindNation(nationId);
        if (WhyMissionary(t) is { } why) return DipResult.Invalid(why);

        var player = State.PlayerNation;
        double stance = t!.Stance switch
        {
            ReligiousStance.Devout => 0.5,
            ReligiousStance.Tolerant => 1.5,
            _ => 1.0
        };
        double embassy = TreatyService.HasEmbassy(State, player.Id, t.Id) ? 1.5 : 1.0;
        double gain = Balance.MissionaryInfluencePerMission * stance * embassy;

        player.PayGold(Balance.MissionaryCost);
        StartCooldown("missionary", t, Balance.MissionaryCooldownDays);
        State.MissionaryInfluence.TryGetValue(t.Id, out double influence);
        influence += gain;

        if (t.Stance == ReligiousStance.Devout)
            DiplomacyService.AddRating(t, -Balance.MissionaryDevoutRatingPenalty);

        if (influence >= Balance.MissionaryConversionThreshold)
        {
            string old = t.Religion;
            t.Religion = player.Religion;
            State.MissionaryInfluence.Remove(t.Id);
            DiplomacyService.AddRating(t, Balance.MissionaryConversionRatingGain);
            State.LogMovement(MovementKind.Mission, MovementStatus.Completed, player, t, $"{t.Name} converted from {old} to {player.Religion} through your missionaries!", inbox: InboxTopic.Religion);
            StateChanged?.Invoke();
            return DipResult.Done($"{t.Name} adopts {player.Religion}! Its people now enjoy that faith's bonuses.");
        }

        State.MissionaryInfluence[t.Id] = influence;
        State.LogMovement(MovementKind.Mission, MovementStatus.Completed, player, t,
            $"Missionaries gained {gain:N1} influence in {t.Name} ({influence:N0}/{Balance.MissionaryConversionThreshold:N0}).");
        StateChanged?.Invoke();
        return DipResult.Done($"Your missionaries spread {player.Religion} in {t.Name}: influence {influence:N0} of {Balance.MissionaryConversionThreshold:N0}.");
    }

    // ---------------- Victory: annex, take resources, or let go ----------------

    /// <summary>Victories over countries the player has beaten and not yet decided about.</summary>
    public IReadOnlyList<VictoryDecision> PendingVictories => State.PendingVictories;

    /// <summary>The pending victory over a country, if the player has just beaten it.</summary>
    public VictoryDecision? PendingVictory(string nationId) => VictoryService.Pending(State, nationId);

    /// <summary>Why Annex cannot be used on this country right now (null = a victory over it is waiting for the player's decision).</summary>
    private string? WhyAnnex(Nation? t)
    {
        if (t is null) return "Nation not found.";
        if (t.IsEliminated) return $"{t.Name} no longer exists.";
        if (VictoryService.Pending(State, t.Id) is null)
            return $"Win a battle against {t.Name} first: a victory lets you annex them, take their resources, or let them go.";
        return null;
    }

    /// <summary>
    /// Decides what to do with a country the player has beaten. Three choices, each with its own flow:
    /// <see cref="VictoryChoice.Annex"/> absorbs the whole country (refused while the Assembly or a guarantee protects it),
    /// <see cref="VictoryChoice.Resources"/> takes a share of its treasury and stocks and leaves it standing, resentful,
    /// <see cref="VictoryChoice.Nothing"/> takes nothing and makes a white peace.
    /// </summary>
    public DipResult ResolveVictory(string nationId, VictoryChoice choice)
    {
        var result = VictoryService.Resolve(State, nationId, choice);
        if (result.Ok) StateChanged?.Invoke();
        return result;
    }

    /// <summary>Current missionary influence over a country (0 if none).</summary>
    public double MissionaryInfluenceOn(string nationId) =>
        State.MissionaryInfluence.TryGetValue(nationId, out double v) ? v : 0;
}
