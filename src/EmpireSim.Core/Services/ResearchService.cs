using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// Research: the player's nation banks points every day (from its population plus a share of
/// each research-contract partner's output) and spends them on the technology it chose.
/// A researched technology feeds an existing multiplier (battle strength, trade income, production).
/// </summary>
public static class ResearchService
{
    /// <summary>Points a nation produces per day: a base plus one point per 10 million people.</summary>
    public static double DailyPoints(Nation n) =>
        Balance.ResearchBasePerDay + n.Population / 1_000_000.0 * Balance.ResearchPerMillionPeople;

    public static bool Has(Nation n, string techId) => n.Technologies.Contains(techId);

    private static double Product(Nation n, Func<TechnologySpec, double> effect)
    {
        double result = 1.0;
        foreach (var id in n.Technologies)
            if (TechnologyCatalog.Get(id) is { } tech) result *= effect(tech);
        return result;
    }

    public static double BattleStrengthMult(Nation n) => Product(n, t => t.BattleStrength);
    public static double TradeIncomeMult(Nation n) => Product(n, t => t.TradeIncome);
    public static double ProductionMult(Nation n) => Product(n, t => t.Production);

    /// <summary>The player's active research contracts (the partner shares part of its research).</summary>
    public static List<Treaty> Contracts(GameState state) =>
        state.Treaties
            .Where(t => t.Type == TreatyType.ResearchContract && t.NationAId == state.PlayerNation.Id && t.IsActiveOn(state.CurrentDate))
            .ToList();

    /// <summary>Daily points the player receives from its research contracts.</summary>
    public static double ContractIncome(GameState state)
    {
        double total = 0;
        foreach (var contract in Contracts(state))
        {
            var partner = state.OtherNations.FirstOrDefault(n => n.Id == contract.NationBId);
            if (partner is not null && !partner.IsEliminated)
                total += DailyPoints(partner) * Balance.ResearchContractShare;
        }
        return total;
    }

    /// <summary>The player's total daily research: own output plus contract income.</summary>
    public static double DailyIncome(GameState state) => DailyPoints(state.PlayerNation) + ContractIncome(state);

    /// <summary>Once a day: bank the day's points and complete the chosen technology when it is paid for.</summary>
    public static void Advance(GameState state)
    {
        var player = state.PlayerNation;
        if (player.IsEliminated) return;
        player.ResearchPoints += DailyIncome(state);

        if (player.CurrentResearchId is not { } id) return;
        var tech = TechnologyCatalog.Get(id);
        if (tech is null || Has(player, id)) { player.CurrentResearchId = null; return; }
        if (player.ResearchPoints < tech.Cost) return;

        player.ResearchPoints -= tech.Cost;
        player.Technologies.Add(id);
        player.CurrentResearchId = null;
        state.Log($"🔬 Research complete: {tech.Name} ({tech.Effect}).");
        InboxService.Notify(state, InboxTopic.Research, $"{tech.Name} researched: {tech.Effect}.", title: $"Research complete — {tech.Name}");
    }
}
