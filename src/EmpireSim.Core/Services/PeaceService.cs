using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>How a war stands from one AI country's side, 0 = nothing special (see <see cref="PeaceService.LeverageOf"/>).</summary>
/// <param name="Lean">-1 (it is being beaten) .. 1 (it is winning): armies in the field and at home, battles won and lost, soldiers lost.</param>
/// <param name="Weariness">0..1: how worn down the war has left it (its age).</param>
/// <param name="Scale">The stakes of the war in gold: what a fair settlement is measured against.</param>
/// <param name="Exhausted">Its home army is too small to go on fighting.</param>
/// <param name="PLose">How likely it is to lose everything if the war goes on, 0..1.</param>
public sealed record PeaceLeverage(double Lean, double Weariness, double Scale, bool Exhausted, double PLose, WarAim Aim,
    double OwnPower, double EnemyPower);

public enum PeaceDecision { Accept, Reject, Counter }

/// <summary>An AI country's answer to terms: accept, reject, or counter with the given terms; with its reason.</summary>
public sealed record PeaceVerdict(PeaceDecision Decision, string Reason, List<PeaceTerm>? Counter = null);

/// <summary>How the war with one country has gone and how its ruler sees it (for the negotiation screen).</summary>
public sealed record WarSummary(int AgeDays, int PlayerBattlesWon, int EnemyBattlesWon, long PlayerLosses, long EnemyLosses,
    int PlayerHome, int EnemyHome, string Assessment);

/// <summary>
/// Peace negotiations. A war between the player and another country ends by agreement: either side proposes terms (gold, goods,
/// a colony, the other country itself, and peace), the other side accepts, rejects or counters. Nothing moves and no war ends when
/// terms are merely proposed; accepting applies every term exactly once (all checked first, so a term that cannot be carried out
/// changes nothing). An AI country judges terms by how the war stands (<see cref="LeverageOf"/>): the losing side concedes more,
/// the winning side demands more, and neither always says yes or no.
/// </summary>
public static class PeaceService
{
    // ================= War fronts: how each war has gone =================

    /// <summary>The record of the war with a country (made when first asked for: an old save has none).</summary>
    public static WarFront Front(GameState state, Nation enemy)
    {
        var front = state.WarFronts.FirstOrDefault(f => f.NationId == enemy.Id);
        if (front is null)
        {
            front = new WarFront { NationId = enemy.Id, StartDate = state.CurrentDate };
            state.WarFronts.Add(front);
        }
        return front;
    }

    /// <summary>A war with the player begins: a fresh record, and no talks left over from an earlier war.</summary>
    public static void OnWarStarted(GameState state, Nation enemy, bool playerStarted)
    {
        state.WarFronts.RemoveAll(f => f.NationId == enemy.Id);
        state.PeaceOffers.RemoveAll(o => o.NationId == enemy.Id);
        state.WarFronts.Add(new WarFront
        {
            NationId = enemy.Id,
            StartDate = state.CurrentDate,
            // A war the enemy began is fought for what its ruler wanted; one forced on it is fought to break the threat.
            Aim = playerStarted ? WarAim.Containment : AiWorldService.Motives(state, enemy, state.PlayerNation).Aim,
        });
    }

    /// <summary>The war with a country is over (by whatever route): its record and every offer of it go.</summary>
    public static void OnWarEnded(GameState state, Nation enemy)
    {
        foreach (var offer in state.PeaceOffers.Where(o => o.NationId == enemy.Id && o.IsPending))
        {
            offer.Status = PeaceOfferStatus.Expired;
            offer.Outcome = "The war ended.";
            offer.ResolvedDate = state.CurrentDate;
        }
        state.PeaceOffers.RemoveAll(o => o.NationId == enemy.Id);
        state.WarFronts.RemoveAll(f => f.NationId == enemy.Id);
    }

    /// <summary>A battle in the war with <paramref name="enemy"/> was fought: who won it and what each side lost.</summary>
    public static void RecordBattle(GameState state, Nation enemy, bool playerWon, long playerLosses, long enemyLosses)
    {
        if (!enemy.AtWarWithPlayer) return;
        var front = Front(state, enemy);
        if (playerWon) front.PlayerBattlesWon++; else front.EnemyBattlesWon++;
        front.PlayerLosses += Math.Max(0, playerLosses);
        front.EnemyLosses += Math.Max(0, enemyLosses);
        front.LastBattleDate = state.CurrentDate;
    }

    // ================= Reading =================

    private static Nation? Find(GameState state, string id) => state.AllNations().FirstOrDefault(n => n.Id == id);

    /// <summary>The proposal waiting for an answer in the war with a country, if any (there is at most one).</summary>
    public static PeaceOffer? Pending(GameState state, string nationId) =>
        state.PeaceOffers.FirstOrDefault(o => o.NationId == nationId && o.IsPending);

    /// <summary>Proposals waiting for the player's answer.</summary>
    public static List<PeaceOffer> PendingForPlayer(GameState state) =>
        state.PeaceOffers.Where(o => o.IsPending && o.ReceiverId == state.PlayerNation.Id).ToList();

    /// <summary>Every proposal in the war with a country, oldest first.</summary>
    public static List<PeaceOffer> History(GameState state, string nationId) =>
        state.PeaceOffers.Where(o => o.NationId == nationId).OrderBy(o => o.CreatedDate).ThenBy(o => o.Round).ToList();

    /// <summary>Why peace cannot be negotiated with a country right now (null = it can).</summary>
    public static string? NegotiationBlock(GameState state, Nation? enemy)
    {
        if (enemy is null) return "Nation not found.";
        if (enemy.IsEliminated) return $"{enemy.Name} no longer exists.";
        if (!enemy.AtWarWithPlayer) return $"You are not at war with {enemy.Name}.";
        if (VictoryService.Pending(state, enemy.Id) is not null)
            return $"You have beaten {enemy.Name} in battle: choose its fate (annex it, take its resources, or let it go) instead of negotiating.";
        return null;
    }

    // ================= Values =================

    /// <summary>Gold plus everything tradable in a country's stores, at base prices: what it could pay without selling a building.</summary>
    public static double LiquidWealth(Nation n) =>
        n.Gold + TradeCatalog.All.Sum(p => n.GetProductStock(p.Id) * p.BasePricePer1000 / 1000);

    /// <summary>What a whole country is worth if it changes hands: its liquid wealth and its people.</summary>
    public static double Worth(Nation n) => LiquidWealth(n) + n.Population * Balance.PeaceValuePerPerson;

    /// <summary>What a colony is worth: its people and its buildings (for a colony that is a territory of its own, those of its own record).</summary>
    private static double ColonyValue(GameState state, Colony c)
    {
        if (c.IsTerritory && state.AllNations().FirstOrDefault(n => n.Id == c.Id) is { } record)
        {
            int buildings = record.Farms + record.Mines + record.Sawmills + record.Workshops + record.ProductionBuildings.Values.Sum();
            return record.Population * Balance.PeaceValuePerPerson + buildings * Balance.PeaceValuePerBuilding;
        }
        return c.Population * Balance.PeaceValuePerPerson + (c.Farms + c.Mines) * Balance.PeaceValuePerBuilding;
    }

    /// <summary>What a term is worth, in gold, to the country that receives it (and costs the one that gives it).</summary>
    public static double TermValue(GameState state, PeaceTerm t)
    {
        switch (t.Kind)
        {
            case PeaceTermKind.Gold: return t.Amount;
            case PeaceTermKind.Goods: return t.Amount * (TradeCatalog.Get(t.ProductId)?.BasePricePer1000 ?? 0) / 1000;
            case PeaceTermKind.Colony: return state.Colonies.FirstOrDefault(c => c.Id == t.ColonyId) is { } c ? ColonyValue(state, c) : 0;
            case PeaceTermKind.Annexation: return Find(state, t.GiverId) is { } giver ? Worth(giver) : 0;
            default: return 0;
        }
    }

    private static string NameOf(GameState state, string id) => Find(state, id)?.Name ?? "unknown";

    /// <summary>One line saying what a term does and between whom.</summary>
    public static string Describe(GameState state, PeaceTerm t)
    {
        string giver = NameOf(state, t.GiverId), receiver = NameOf(state, t.ReceiverId);
        return t.Kind switch
        {
            PeaceTermKind.Gold => $"{giver} pays {Currency.Cost(t.Amount)} to {receiver}",
            PeaceTermKind.Goods => $"{giver} gives {t.Amount:N0} {TradeCatalog.Get(t.ProductId)?.Name ?? t.ProductId} to {receiver}",
            PeaceTermKind.Colony => $"{giver} hands the colony of {state.Colonies.FirstOrDefault(c => c.Id == t.ColonyId)?.Name ?? "?"} to {receiver}",
            PeaceTermKind.Annexation => $"{giver} is annexed by {receiver}",
            _ => "The war ends",
        };
    }

    /// <summary>The terms of an offer, one line each.</summary>
    public static List<string> Describe(GameState state, PeaceOffer offer) =>
        offer.Terms.Select(t => Describe(state, t)).ToList();

    private static string Summarize(GameState state, PeaceOffer offer) => string.Join("; ", Describe(state, offer));

    // ================= Validation =================

    /// <summary>
    /// Why a set of terms cannot be put or carried out (null = they can). Every giver must really own what it gives, never more gold
    /// or goods than it holds; a colony must be the giver's; an annexation must be allowed by the usual rules. Checked when the terms are
    /// proposed and again when they are accepted.
    /// </summary>
    public static string? Validate(GameState state, Nation enemy, IReadOnlyList<PeaceTerm> terms)
    {
        var player = state.PlayerNation;
        if (terms.Count == 0) return "An offer needs at least one term.";
        if (terms.Count(t => t.Kind == PeaceTermKind.Peace) > 1) return "Peace can be listed only once.";
        if (terms.Count(t => t.Kind == PeaceTermKind.Annexation) > 1) return "A country can be annexed only once.";

        var gold = new Dictionary<string, double>();
        var goods = new Dictionary<(string, string), double>();
        var colonies = new HashSet<string>();
        var colonyTotals = new Dictionary<string, (long People, int Farms, int Mines)>();

        foreach (var t in terms)
        {
            if (t.Kind == PeaceTermKind.Peace) continue;

            var giver = t.GiverId == player.Id ? player : t.GiverId == enemy.Id ? enemy : null;
            var receiver = t.ReceiverId == player.Id ? player : t.ReceiverId == enemy.Id ? enemy : null;
            if (giver is null || receiver is null || giver.Id == receiver.Id)
                return "Every term must pass something from one of the two countries to the other.";

            switch (t.Kind)
            {
                case PeaceTermKind.Gold:
                    if (!(t.Amount > 0) || !double.IsFinite(t.Amount)) return "A payment must be more than zero gold.";
                    gold[giver.Id] = gold.GetValueOrDefault(giver.Id) + t.Amount;
                    break;

                case PeaceTermKind.Goods:
                    if (TradeCatalog.Get(t.ProductId) is not { } product) return "Unknown goods in the offer.";
                    if (!(t.Amount > 0) || !double.IsFinite(t.Amount)) return $"The quantity of {product.Name} must be more than zero.";
                    goods[(giver.Id, product.Id)] = goods.GetValueOrDefault((giver.Id, product.Id)) + t.Amount;
                    break;

                case PeaceTermKind.Colony:
                    var colony = state.Colonies.FirstOrDefault(c => c.Id == t.ColonyId);
                    if (colony is null) return "That colony does not exist.";
                    if (colony.OwnerId != giver.Id) return $"{giver.Name} does not own {colony.Name}.";
                    if (!colonies.Add(colony.Id)) return $"{colony.Name} is listed twice.";
                    // A colony that is a territory of its own holds its people and buildings itself: nothing is taken from the giver's totals.
                    if (colony.IsTerritory) break;
                    // Several colonies from one giver are checked together: its people and buildings must cover all of them.
                    var sum = colonyTotals.GetValueOrDefault(giver.Id);
                    sum = (sum.People + colony.Population, sum.Farms + colony.Farms, sum.Mines + colony.Mines);
                    colonyTotals[giver.Id] = sum;
                    if (giver.Population < sum.People || giver.Farms < sum.Farms || giver.Mines < sum.Mines)
                        return $"{colony.Name}'s people and buildings are no longer all {giver.Name}'s to give.";
                    break;

                case PeaceTermKind.Annexation:
                    if (giver.IsPlayer) return "Your own country cannot be offered for annexation.";
                    if (terms.Any(x => x.Kind is not (PeaceTermKind.Annexation or PeaceTermKind.Peace)))
                        return $"Annexing {giver.Name} takes everything it has: no other terms are needed (only the peace itself may be listed with it).";
                    if (TreatyService.AnnexationBlock(state, giver.Id) is { } block) return block;
                    break;
            }
        }

        foreach (var (giverId, total) in gold)
        {
            var giver = Find(state, giverId)!;
            if (giver.Gold + 1e-9 < total) return $"{giver.Name} has only {Currency.Cost(giver.Gold)}: it cannot give {Currency.Cost(total)}.";
        }
        foreach (var ((giverId, productId), total) in goods)
        {
            var giver = Find(state, giverId)!;
            double have = giver.GetProductStock(productId);
            if (have + 1e-9 < total)
                return $"{giver.Name} has only {have:N0} {TradeCatalog.Get(productId)!.Name}: it cannot give {total:N0}.";
        }
        return null;
    }

    // ================= Making and answering offers =================

    /// <summary>
    /// Puts terms to the other country (<paramref name="answering"/> = the pending offer being countered). Nothing moves and no war
    /// ends: the offer waits for the receiver's answer. A refused proposal changes nothing.
    /// </summary>
    public static (DipResult Result, PeaceOffer? Offer) Propose(GameState state, string proposerId, Nation enemy,
        IEnumerable<PeaceTerm> terms, PeaceOffer? answering = null)
    {
        if (NegotiationBlock(state, enemy) is { } blocked) return (DipResult.Invalid(blocked), null);
        var player = state.PlayerNation;
        if (proposerId != player.Id && proposerId != enemy.Id) return (DipResult.Invalid("Only the two countries at war can negotiate."), null);
        string receiverId = proposerId == player.Id ? enemy.Id : player.Id;
        var list = terms.Select(t => t.Copy()).ToList();
        var today = state.CurrentDate;
        var front = Front(state, enemy);

        var open = Pending(state, enemy.Id);
        if (answering is null)
        {
            if (open is not null)
                return (DipResult.Invalid("A proposal is already waiting: answer it (or take it back) before putting new terms."), null);
            if (proposerId == player.Id && front.LastPlayerRejected is { } turnedDown && today < turnedDown.AddDays(Balance.PeaceRetryDays))
                return (DipResult.Invalid(
                    $"{enemy.Name} turned your terms down recently: wait {turnedDown.AddDays(Balance.PeaceRetryDays).DayNumber - today.DayNumber} more day(s) before opening again, or put a counteroffer to one of theirs."), null);
        }
        else
        {
            if (open is null || open.Id != answering.Id || answering.ReceiverId != proposerId)
                return (DipResult.Invalid("There is no proposal of theirs waiting to be answered."), null);
            if (answering.Round >= Balance.PeaceMaxRounds)
                return (DipResult.Invalid("This is the last round of talks: accept or reject the terms."), null);
        }

        if (Validate(state, enemy, list) is { } error) return (DipResult.Invalid(error), null);

        if (answering is not null)
        {
            answering.Status = PeaceOfferStatus.Countered;
            answering.ResolvedDate = today;
            answering.Outcome = "Answered with a counteroffer.";
        }
        var offer = new PeaceOffer
        {
            NationId = enemy.Id,
            ProposerId = proposerId,
            ReceiverId = receiverId,
            Terms = list,
            CreatedDate = today,
            DueDate = receiverId == player.Id ? today.AddDays(Balance.PeaceOfferDays) : today.AddDays(Balance.PeaceReplyDays),
            Round = answering is null ? 1 : answering.Round + 1,
            ParentId = answering?.Id,
        };
        state.PeaceOffers.Add(offer);

        string proposer = NameOf(state, proposerId), receiver = NameOf(state, receiverId);
        string kind = answering is null ? "proposes peace terms to" : "answers with a counteroffer to";
        bool fromPlayer = proposerId == player.Id;
        string headline = fromPlayer
            ? (answering is null ? $"You proposed terms to {enemy.Name}." : $"You answered {enemy.Name}'s terms with a counteroffer.")
            : (answering is null ? $"{enemy.Name} proposes peace terms." : $"{enemy.Name} answers your terms with a counteroffer.");
        Post(state, InboxTopic.PeaceOffer, enemy, headline, offer,
            answering is null ? "Peace proposal" : "Counteroffer",
            fromPlayer ? "Waiting for their answer." : (answering is null ? $"Answer within {Balance.PeaceOfferDays} days in Diplomacy: accept, reject or counter." : "Answer in Diplomacy: accept, reject or counter."));
        state.LogMovement(MovementKind.War, MovementStatus.UnderWay, proposerId, proposer,
            receiverId, receiver, $"🕊 {proposer} {kind} {receiver}: {Summarize(state, offer)}.");
        if (!fromPlayer)
            state.ActiveWarnings.Add($"🕊 {enemy.Name} {(answering is null ? "proposes peace terms" : "answers with a counteroffer")} — see Diplomacy.");

        return (DipResult.Done(fromPlayer
            ? $"Terms sent to {enemy.Name}. Their answer will come in {Balance.PeaceReplyDays} day(s); nothing has changed yet."
            : "Terms received."), offer);
    }

    /// <summary>The receiver accepts: every term is carried out exactly once and, if peace was in them, the war ends.</summary>
    public static DipResult Accept(GameState state, PeaceOffer offer)
    {
        if (!offer.IsPending) return DipResult.Invalid("This proposal has already been settled.");
        var enemy = Find(state, offer.NationId);
        if (NegotiationBlock(state, enemy) is { } blocked) { Settle(state, offer, PeaceOfferStatus.Expired, blocked); return DipResult.Invalid(blocked); }
        var player = state.PlayerNation;

        // Everything is checked against what the countries hold NOW; if any term cannot be carried out, nothing is.
        if (Validate(state, enemy!, offer.Terms) is { } error)
        {
            Settle(state, offer, PeaceOfferStatus.Expired, $"The terms could no longer be carried out: {error}");
            Post(state, InboxTopic.PeaceOffer, enemy!, $"The terms with {enemy!.Name} fell through: {error}", offer, "Terms fell through", error);
            return DipResult.Invalid($"The terms can no longer be carried out ({error}). Nothing was moved.");
        }

        // Who comes out ahead decides how the enemy regards the player afterwards.
        double enemyNet = offer.Terms.Where(t => t.Kind != PeaceTermKind.Peace)
            .Sum(t => (t.ReceiverId == enemy!.Id ? 1 : -1) * TermValue(state, t));
        double scale = Math.Max(Balance.PeaceMinScale, Balance.PeaceStakeFraction * (LiquidWealth(enemy!) + LiquidWealth(player)) / 2);

        string description = Summarize(state, offer);
        bool annexed = offer.Terms.Any(t => t.Kind == PeaceTermKind.Annexation);
        Settle(state, offer, PeaceOfferStatus.Accepted, "Accepted.");

        foreach (var t in offer.Terms.Where(t => t.Kind is PeaceTermKind.Gold or PeaceTermKind.Goods or PeaceTermKind.Colony))
            Transfer(state, t);

        if (annexed)
        {
            // The annexed country's own state is cleaned up there (treaties, marches, armies, stocks, offers).
            Warfare.AnnexNation(state, player, enemy!, byBattle: false);
        }
        else if (offer.Terms.Any(t => t.Kind == PeaceTermKind.Peace))
        {
            enemy!.AtWarWithPlayer = false;
            enemy.HasTradePactWithPlayer = false;
            enemy.RelationToPlayer = DiplomacyService.FromDisplayRating(
                enemyNet < -0.1 * scale ? Balance.PeaceHumbledRating : enemyNet > 0.1 * scale ? Balance.PeaceSatisfiedRating : Balance.PeaceAgreementRating);
            state.PendingVictories.RemoveAll(v => v.LoserId == enemy.Id);
            OnWarEnded(state, enemy);
        }

        state.LogMovement(MovementKind.War, MovementStatus.Completed, player, enemy!, annexed
            ? $"🕊 {enemy!.Name} accepted annexation: {description}."
            : offer.SpecifiesPeace ? $"🕊 Peace with {enemy!.Name}: {description}." : $"🤝 Terms agreed with {enemy!.Name} (the war goes on): {description}.");
        Post(state, InboxTopic.PeaceAgreement, enemy!,
            annexed ? $"{enemy.Name} was annexed by agreement." : offer.SpecifiesPeace ? $"Peace agreed with {enemy.Name}." : $"Terms agreed with {enemy.Name}; the war goes on.",
            offer, "Final agreement", null);
        return DipResult.Done(annexed ? $"{enemy!.Name} is annexed by agreement."
            : offer.SpecifiesPeace ? $"Peace with {enemy!.Name}. The terms are in force." : $"The terms with {enemy!.Name} are in force; the war goes on.");
    }

    /// <summary>The receiver turns the terms down: nothing moves and the war goes on.</summary>
    public static DipResult Reject(GameState state, PeaceOffer offer, string? reason = null)
    {
        if (!offer.IsPending) return DipResult.Invalid("This proposal has already been settled.");
        var enemy = Find(state, offer.NationId);
        Settle(state, offer, PeaceOfferStatus.Rejected, reason ?? "Rejected.");
        if (enemy is null) return DipResult.Done("The terms were rejected.");
        if (offer.ProposerId == state.PlayerNation.Id) Front(state, enemy).LastPlayerRejected = state.CurrentDate;
        bool byPlayer = offer.ReceiverId == state.PlayerNation.Id;
        Post(state, InboxTopic.PeaceOffer, enemy,
            byPlayer ? $"You rejected {enemy.Name}'s terms. The war goes on." : $"{enemy.Name} rejected your terms. The war goes on.",
            offer, "Terms rejected", reason);
        state.LogMovement(MovementKind.War, MovementStatus.Failed, offer.ReceiverId, NameOf(state, offer.ReceiverId), offer.ProposerId, NameOf(state, offer.ProposerId),
            byPlayer ? $"You rejected {enemy.Name}'s peace terms." : $"{enemy.Name} rejected your peace terms.");
        return DipResult.Done("The terms were rejected; the war goes on.");
    }

    /// <summary>The proposer takes back its pending proposal.</summary>
    public static DipResult Withdraw(GameState state, PeaceOffer offer)
    {
        if (!offer.IsPending) return DipResult.Invalid("This proposal has already been settled.");
        Settle(state, offer, PeaceOfferStatus.Withdrawn, "Withdrawn by its proposer.");
        return DipResult.Done("The proposal was withdrawn.");
    }

    private static void Settle(GameState state, PeaceOffer offer, PeaceOfferStatus status, string outcome)
    {
        offer.Status = status;
        offer.Outcome = outcome;
        offer.ResolvedDate = state.CurrentDate;
    }

    /// <summary>Moves one gold, goods or colony term (already validated).</summary>
    private static void Transfer(GameState state, PeaceTerm t)
    {
        var giver = Find(state, t.GiverId)!;
        var receiver = Find(state, t.ReceiverId)!;
        switch (t.Kind)
        {
            case PeaceTermKind.Gold:
                giver.Gold -= t.Amount;
                receiver.Gold += t.Amount;
                state.LogMovement(MovementKind.Gold, MovementStatus.Completed, giver, receiver, $"{giver.Name} paid {Currency.Cost(t.Amount)} to {receiver.Name} under the peace terms.", military: true);
                break;
            case PeaceTermKind.Goods:
                giver.AdjustProductStock(t.ProductId, -t.Amount);
                receiver.AdjustProductStock(t.ProductId, t.Amount);
                state.LogMovement(MovementKind.Goods, MovementStatus.Completed, giver, receiver,
                    $"{giver.Name} handed {t.Amount:N0} {TradeCatalog.Get(t.ProductId)?.Name} to {receiver.Name} under the peace terms.", military: true);
                break;
            case PeaceTermKind.Colony:
                var colony = state.Colonies.First(c => c.Id == t.ColonyId);
                TreatyService.TransferColony(state, colony, giver, receiver);
                state.LogMovement(MovementKind.Colony, MovementStatus.Completed, giver, receiver,
                    $"{giver.Name} ceded the colony of {colony.Name} to {receiver.Name} under the peace terms.", military: true);
                break;
        }
    }

    private static void Post(GameState state, InboxTopic topic, Nation enemy, string summary, PeaceOffer offer, string title, string? note)
    {
        var lines = new List<string> { summary, "" };
        lines.AddRange(Describe(state, offer).Select(l => "• " + l));
        if (offer.Round > 1) lines.Add($"(round {offer.Round} of {Balance.PeaceMaxRounds})");
        if (!string.IsNullOrEmpty(note)) { lines.Add(""); lines.Add(note!); }
        InboxService.Notify(state, topic, summary, enemy.Id, title: $"{title} — {enemy.Name}", details: string.Join("\n", lines));
    }

    // ================= The daily step =================

    /// <summary>
    /// Once a day: records for wars an old save never recorded, lapsing proposals, the answers AI countries owe, and AI countries
    /// that put terms to the player on their own.
    /// </summary>
    public static void Advance(GameState state)
    {
        // Once the player's country has fallen there is nobody left to talk to.
        if (state.Defeated || state.PlayerNation.IsEliminated) return;
        var today = state.CurrentDate;
        var player = state.PlayerNation;
        var atWar = state.OtherNations.Where(n => n.AtWarWithPlayer && !n.IsEliminated).ToList();
        foreach (var enemy in atWar) Front(state, enemy);
        state.WarFronts.RemoveAll(f => atWar.All(n => n.Id != f.NationId));

        foreach (var offer in state.PeaceOffers.Where(o => o.IsPending).ToList())
        {
            var enemy = Find(state, offer.NationId);
            if (enemy is null || enemy.IsEliminated || !enemy.AtWarWithPlayer)
            {
                Settle(state, offer, PeaceOfferStatus.Expired, "The war ended.");
                continue;
            }
            if (VictoryService.Pending(state, enemy.Id) is not null)
            {
                Settle(state, offer, PeaceOfferStatus.Expired, "The battle was won: the victory decides.");
                continue;
            }
            if (today < offer.DueDate) continue;

            if (offer.ReceiverId == player.Id)
            {
                Settle(state, offer, PeaceOfferStatus.Expired, "Not answered in time.");
                Post(state, InboxTopic.PeaceOffer, enemy, $"{enemy.Name}'s terms lapsed unanswered. The war goes on.", offer, "Proposal lapsed", null);
            }
            else
                AiRespond(state, enemy, offer);
        }

        foreach (var enemy in atWar)
            if (!enemy.IsEliminated && enemy.AtWarWithPlayer) AiMaybePropose(state, enemy);
    }

    // ================= The AI's side =================

    /// <summary>How the war stands from an AI country's side: armies, battles, losses, age, and what is at stake.</summary>
    public static PeaceLeverage LeverageOf(GameState state, Nation ai)
    {
        var player = state.PlayerNation;
        var front = Front(state, ai);

        double InFlight(string from, string to) => state.MarchingArmies
            .Where(m => m.AttackerNationId == from && m.TargetNationId == to).Sum(m => ArmyHelper.ArmyPower(m.Force));
        double own = (ArmyHelper.ArmyPower(ai.Units) + InFlight(ai.Id, player.Id)) * ai.BattleStrengthMult;
        double enemy = (ArmyHelper.ArmyPower(player.Units) + InFlight(player.Id, ai.Id)) * player.BattleStrengthMult;
        double power = (own - enemy) / Math.Max(1, own + enemy);

        int battles = front.PlayerBattlesWon + front.EnemyBattlesWon;
        double record = battles == 0 ? 0 : (double)(front.EnemyBattlesWon - front.PlayerBattlesWon) / battles * Math.Min(1.0, battles / 3.0);
        double lost = front.PlayerLosses + front.EnemyLosses;
        double casualties = lost <= 0 ? 0 : (front.PlayerLosses - front.EnemyLosses) / lost * Math.Min(1.0, lost / 2000.0);

        bool exhausted = ai.Soldiers < Balance.AiMinWarForce;
        double lean = Math.Clamp(0.55 * power + 0.25 * record + 0.20 * casualties - (exhausted ? 0.25 : 0), -1, 1);
        double weariness = Math.Clamp((state.CurrentDate.DayNumber - front.StartDate.DayNumber) / (double)Balance.PeaceWearyDays, 0, 1);
        double scale = Math.Max(Balance.PeaceMinScale, Balance.PeaceStakeFraction * (LiquidWealth(ai) + LiquidWealth(player)) / 2);
        double pLose = Math.Clamp((-lean - 0.3) / 0.7, 0, 1);
        return new PeaceLeverage(lean, weariness, scale, exhausted, pLose, front.Aim, own, enemy);
    }

    /// <summary>The war with a country as the negotiation screen shows it: the record, and how its ruler sees things.</summary>
    public static WarSummary Summary(GameState state, Nation enemy)
    {
        var front = Front(state, enemy);
        var lev = LeverageOf(state, enemy);
        string mood = lev.Lean switch
        {
            > 0.4 => $"{enemy.Name}'s ruler is confident: the war is going their way.",
            > 0.1 => $"{enemy.Name}'s ruler thinks they have the upper hand.",
            > -0.1 => $"The war is evenly balanced; {enemy.Name}'s ruler could be tempted by a fair peace.",
            > -0.4 => $"{enemy.Name}'s ruler is losing ground and would concede something for peace.",
            _ => $"{enemy.Name}'s ruler is being beaten and is desperate for an end to the war.",
        };
        return new WarSummary(state.CurrentDate.DayNumber - front.StartDate.DayNumber, front.PlayerBattlesWon, front.EnemyBattlesWon,
            front.PlayerLosses, front.EnemyLosses, state.PlayerNation.Soldiers, enemy.Soldiers, mood);
    }

    /// <summary>What ending the war is worth to an AI country, beyond the terms: it grows as the war wears on, and a ruler who wants land cares less.</summary>
    private static double PeaceBonus(Nation ai, IReadOnlyList<PeaceTerm> terms, PeaceLeverage lev)
    {
        if (!terms.Any(t => t.Kind == PeaceTermKind.Peace)) return 0;
        double aim = lev.Aim switch { WarAim.Conquest => 0.4, WarAim.Humiliation => 0.7, _ => 1.0 };
        double friendship = 1 + Math.Clamp(ai.BaselineRelation, -100, 100) / 400.0;
        return lev.Scale * (0.10 + 0.40 * lev.Weariness) * aim * friendship;
    }

    /// <summary>What a set of terms is worth to <paramref name="ai"/>: what it receives minus what it gives (giving land costs more than its price).</summary>
    private static double ValueToAi(GameState state, Nation ai, IReadOnlyList<PeaceTerm> terms, PeaceLeverage lev)
    {
        double v = 0;
        foreach (var t in terms)
        {
            if (t.Kind == PeaceTermKind.Peace) continue;
            if (t.Kind == PeaceTermKind.Annexation)
            {
                // Being annexed costs the whole country — less than it would cost to be conquered anyway, if defeat is near.
                if (t.GiverId == ai.Id) v -= TermValue(state, t) * (1 - lev.PLose);
                continue;
            }
            double value = TermValue(state, t);
            if (t.ReceiverId == ai.Id) v += value;
            else if (t.GiverId == ai.Id) v -= value * (t.Kind == PeaceTermKind.Colony ? Balance.PeaceGiveLandMult : 1);
        }
        return v;
    }

    private static int Hash(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in text) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>A ruler's mood for this offer, -1..1: the same offer on the same day always gets the same answer, but two different offers do not line up.</summary>
    private static double Noise(Nation ai, PeaceOffer offer)
    {
        string signature = string.Join(",", offer.Terms.Select(t => $"{(int)t.Kind}:{t.GiverId}:{Math.Round(t.Amount)}:{t.ProductId}:{t.ColonyId}"));
        int h = Hash($"{ai.Id}|{offer.CreatedDate.DayNumber}|{offer.Round}|{signature}");
        return (h % 2001 - 1000) / 1000.0;
    }

    /// <summary>A deterministic 0..1 roll for a country on a day (so AI peace behaviour never disturbs the simulation's random stream).</summary>
    private static double Roll(GameState state, Nation ai, string salt) =>
        Hash($"{ai.Id}|{salt}|{state.CurrentDate.DayNumber}") % 10000 / 10000.0;

    /// <summary>
    /// How an AI country answers terms. It compares what the terms are worth to it against what the war entitles it to
    /// (<c>Lean × stakes</c>: positive when it is winning — it expects to be paid; negative when it is losing — it will pay), with its
    /// mood, how weary it is, its war aim and its regard for the player moving the price. If it wants only a little more it counters
    /// with terms it would accept itself; if far more, or it is the last round, it rejects.
    /// </summary>
    public static PeaceVerdict Evaluate(GameState state, Nation ai, PeaceOffer offer)
    {
        var lev = LeverageOf(state, ai);
        double fair = lev.Lean * lev.Scale;
        double noise = Noise(ai, offer) * Balance.PeaceNoise * lev.Scale;
        double net = ValueToAi(state, ai, offer.Terms, lev) + PeaceBonus(ai, offer.Terms, lev);
        double gap = fair - (net + noise);

        if (gap <= 0) return new PeaceVerdict(PeaceDecision.Accept, $"{ai.Name} finds the terms acceptable.");

        string why = lev.Lean > 0.2 ? $"{ai.Name} believes it is winning and wants far more."
            : lev.Lean < -0.2 ? $"Even beaten, {ai.Name} will not pay that."
            : $"{ai.Name} finds the terms unreasonable.";
        if (offer.Round >= Balance.PeaceMaxRounds) return new PeaceVerdict(PeaceDecision.Reject, why + " The talks have run their course.");
        if (gap > Balance.PeaceCounterBand * lev.Scale) return new PeaceVerdict(PeaceDecision.Reject, why);

        var counter = BuildCounter(state, ai, offer, lev, noise);
        return counter is null
            ? new PeaceVerdict(PeaceDecision.Reject, why + " It could not name terms you could meet.")
            : new PeaceVerdict(PeaceDecision.Counter, $"{ai.Name} wants different terms.", counter);
    }

    /// <summary>The terms an AI country puts back: it refuses to give up land or itself, takes back what it was to pay, then asks the player for gold to close the rest.</summary>
    private static List<PeaceTerm>? BuildCounter(GameState state, Nation ai, PeaceOffer offer, PeaceLeverage lev, double noise)
    {
        var player = state.PlayerNation;
        var terms = offer.Terms.Select(t => t.Copy()).ToList();
        double Gap() => lev.Lean * lev.Scale - (ValueToAi(state, ai, terms, lev) + PeaceBonus(ai, terms, lev) + noise);

        // 1. It will not hand over land (a colony or the country) if the rest does not already satisfy it.
        foreach (var land in terms.Where(t => t.GiverId == ai.Id && t.Kind is PeaceTermKind.Colony or PeaceTermKind.Annexation)
                     .OrderByDescending(t => TermValue(state, t)).ToList())
        {
            if (Gap() <= 0) break;
            terms.Remove(land);
        }
        if (terms.Count == 0) terms.Add(PeaceTerm.Peace());

        // 2. It takes back some of what it was to pay.
        foreach (var pay in terms.Where(t => t.GiverId == ai.Id && t.Kind is PeaceTermKind.Gold or PeaceTermKind.Goods).ToList())
        {
            double gap = Gap();
            if (gap <= 0) break;
            double unit = pay.Kind == PeaceTermKind.Gold ? 1 : Math.Max(1e-9, TermValue(state, new PeaceTerm { Kind = pay.Kind, ProductId = pay.ProductId, Amount = 1 }));
            double cut = Math.Min(pay.Amount, Math.Ceiling(gap / unit));
            pay.Amount -= cut;
            if (pay.Amount < 0.5) terms.Remove(pay);
        }

        // 3. It asks the player for gold to close what is left, never more than the player can pay.
        double rest = Gap();
        if (rest > 0)
        {
            double asked = terms.Where(t => t.Kind == PeaceTermKind.Gold && t.GiverId == player.Id).Sum(t => t.Amount);
            double ask = Math.Ceiling(rest * 1.05 / 10) * 10;
            if (asked + ask > player.Gold * 0.9) return null;
            var existing = terms.FirstOrDefault(t => t.Kind == PeaceTermKind.Gold && t.GiverId == player.Id && t.ReceiverId == ai.Id);
            if (existing is not null) existing.Amount += ask;
            else terms.Add(PeaceTerm.Gold(player.Id, ai.Id, ask));
        }

        if (terms.Count == offer.Terms.Count && terms.Zip(offer.Terms).All(p => p.First.Kind == p.Second.Kind && Math.Abs(p.First.Amount - p.Second.Amount) < 0.5 && p.First.ColonyId == p.Second.ColonyId))
            return null;   // nothing changed: that is a rejection, not a counter
        return Validate(state, ai, terms) is null ? terms : null;
    }

    private static void AiRespond(GameState state, Nation ai, PeaceOffer offer)
    {
        var verdict = Evaluate(state, ai, offer);
        switch (verdict.Decision)
        {
            case PeaceDecision.Accept:
                var result = Accept(state, offer);
                if (!result.Ok && offer.IsPending) Reject(state, offer, result.Message);
                break;
            case PeaceDecision.Counter:
                var (res, _) = Propose(state, ai.Id, ai, verdict.Counter!, answering: offer);
                if (!res.Ok && offer.IsPending) Reject(state, offer, verdict.Reason);
                break;
            default:
                Reject(state, offer, verdict.Reason);
                break;
        }
    }

    /// <summary>
    /// An AI country that has had enough (worn down, losing, out of soldiers) or has the upper hand may put terms to the player on its
    /// own: a losing side offers to pay, an even one a white peace, a winning one names a price the player can meet.
    /// </summary>
    private static void AiMaybePropose(GameState state, Nation ai)
    {
        if (VictoryService.Pending(state, ai.Id) is not null || Pending(state, ai.Id) is not null) return;
        var front = Front(state, ai);
        var today = state.CurrentDate;
        if (today.DayNumber - front.StartDate.DayNumber < Balance.AiPeaceMinWarDays) return;
        if (front.LastAiProposal is { } last && today < last.AddDays(Balance.AiPeaceProposalGapDays)) return;

        var lev = LeverageOf(state, ai);
        double want = lev.Weariness * 0.5 + Math.Max(0, -lev.Lean) * 0.8 + (lev.Exhausted ? 0.3 : 0)
                      + (lev.Lean > 0.25 ? 0.15 : 0) - (lev.Aim == WarAim.Conquest && lev.Lean > 0 ? 0.2 : 0);
        want = Math.Clamp(want, 0, 1);
        if (want <= 0 || Roll(state, ai, "peace") >= Balance.AiPeaceProposalChancePerDay * want) return;

        var (result, offer) = Propose(state, ai.Id, ai, ProposalFor(state, ai, lev));
        if (result.Ok && offer is not null) front.LastAiProposal = today;
    }

    /// <summary>The terms an AI country puts forward, from how the war stands.</summary>
    public static List<PeaceTerm> ProposalFor(GameState state, Nation ai, PeaceLeverage lev)
    {
        var player = state.PlayerNation;
        var terms = new List<PeaceTerm> { PeaceTerm.Peace() };
        double fair = lev.Lean * lev.Scale;
        double bonus = PeaceBonus(ai, terms, lev);

        if (lev.Lean >= 0.15)
        {
            // Winning: it names a price, within what the player can pay.
            double due = Math.Max(0, fair - bonus);
            double ask = Math.Floor(Math.Min(due, player.Gold * 0.6) / 10) * 10;
            if (ask >= 50) terms.Add(PeaceTerm.Gold(player.Id, ai.Id, ask));
            if (lev.Lean >= 0.6 && due - ask > lev.Scale * 0.25
                && state.Colonies.Where(c => c.OwnerId == player.Id).OrderBy(c => ColonyValue(state, c)).FirstOrDefault() is { } colony)
                terms.Add(PeaceTerm.Colony(player.Id, ai.Id, colony.Id));
        }
        else if (lev.Lean <= -0.15)
        {
            // Losing: it pays for peace, out of what it has.
            double pay = Math.Floor(Math.Min(ai.Gold * 0.5, -fair * 0.8) / 10) * 10;
            if (pay >= 50) terms.Add(PeaceTerm.Gold(ai.Id, player.Id, pay));
        }
        return terms;
    }
}
