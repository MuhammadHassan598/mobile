using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The player's side of peace negotiations (the rules live in <see cref="PeaceService"/>). Proposing, rejecting, countering and
/// withdrawing change nothing but the state of the talks; only an accepted offer moves anything, and only once.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>Why peace cannot be negotiated with a country right now (null = it can).</summary>
    public string? PeaceBlock(string nationId) => PeaceService.NegotiationBlock(State, FindNation(nationId));

    /// <summary>The proposal waiting for an answer in the war with a country (the player's own, or theirs), if any.</summary>
    public PeaceOffer? PendingPeaceOffer(string nationId) => PeaceService.Pending(State, nationId);

    /// <summary>Proposals waiting for the player's answer: accept, reject or counter.</summary>
    public List<PeaceOffer> PeaceOffersForPlayer => PeaceService.PendingForPlayer(State);

    /// <summary>Every proposal and counteroffer so far in the war with a country, oldest first.</summary>
    public List<PeaceOffer> PeaceHistory(string nationId) => PeaceService.History(State, nationId);

    /// <summary>How the war with a country has gone and how its ruler sees it (null when the player is not at war with it).</summary>
    public WarSummary? WarSummaryOf(string nationId) =>
        FindNation(nationId) is { AtWarWithPlayer: true, IsEliminated: false } n ? PeaceService.Summary(State, n) : null;

    /// <summary>Puts peace terms to the enemy. Nothing moves and the war goes on until they accept (their answer comes after a day).</summary>
    public DipResult ProposePeace(string nationId, IEnumerable<PeaceTerm> terms)
    {
        var enemy = FindNation(nationId);
        if (enemy is null) return DipResult.Invalid("Nation not found.");
        var (result, _) = PeaceService.Propose(State, State.PlayerNation.Id, enemy, terms);
        if (result.Ok) StateChanged?.Invoke();
        return result;
    }

    private PeaceOffer? OfferToPlayer(string offerId) =>
        State.PeaceOffers.FirstOrDefault(o => o.Id == offerId && o.ReceiverId == State.PlayerNation.Id);

    /// <summary>Accepts terms put to the player: they are carried out (exactly once) and, if peace was among them, the war ends.</summary>
    public DipResult AcceptPeaceOffer(string offerId)
    {
        var offer = OfferToPlayer(offerId);
        if (offer is null) return DipResult.Invalid("There is no proposal of theirs to accept.");
        var result = PeaceService.Accept(State, offer);
        StateChanged?.Invoke();
        return result;
    }

    /// <summary>Rejects terms put to the player: nothing moves, the war goes on.</summary>
    public DipResult RejectPeaceOffer(string offerId)
    {
        var offer = OfferToPlayer(offerId);
        if (offer is null) return DipResult.Invalid("There is no proposal of theirs to reject.");
        var result = PeaceService.Reject(State, offer, "Rejected by you.");
        StateChanged?.Invoke();
        return result;
    }

    /// <summary>Answers terms put to the player with different terms (the next round of the talks).</summary>
    public DipResult CounterPeaceOffer(string offerId, IEnumerable<PeaceTerm> terms)
    {
        var offer = OfferToPlayer(offerId);
        if (offer is null) return DipResult.Invalid("There is no proposal of theirs to answer.");
        var enemy = FindNation(offer.NationId);
        if (enemy is null) return DipResult.Invalid("Nation not found.");
        var (result, _) = PeaceService.Propose(State, State.PlayerNation.Id, enemy, terms, answering: offer);
        if (result.Ok) StateChanged?.Invoke();
        return result;
    }

    /// <summary>Takes back the player's own proposal before it is answered.</summary>
    public DipResult WithdrawPeaceOffer(string offerId)
    {
        var offer = State.PeaceOffers.FirstOrDefault(o => o.Id == offerId && o.ProposerId == State.PlayerNation.Id);
        if (offer is null) return DipResult.Invalid("There is no proposal of yours to withdraw.");
        var result = PeaceService.Withdraw(State, offer);
        StateChanged?.Invoke();
        return result;
    }
}
