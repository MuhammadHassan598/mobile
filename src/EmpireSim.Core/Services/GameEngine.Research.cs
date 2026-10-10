using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>Choosing what the player's nation researches (the daily accrual lives in <see cref="ResearchService"/>).</summary>
public sealed partial class GameEngine
{
    /// <summary>The player's total daily research points: own output plus research-contract income.</summary>
    public double ResearchPerDay => ResearchService.DailyIncome(State);

    /// <summary>
    /// Chooses the technology to research next. Points keep banking whatever is chosen; the technology
    /// completes on the first day the bank covers its cost. Returns an error, or null on success.
    /// </summary>
    public string? StartResearch(string techId)
    {
        var tech = TechnologyCatalog.Get(techId);
        if (tech is null) return "Unknown technology.";
        var player = State.PlayerNation;
        if (ResearchService.Has(player, techId)) return $"{tech.Name} is already researched.";
        if (player.CurrentResearchId == techId) return $"{tech.Name} is already being researched.";

        player.CurrentResearchId = techId;
        State.Log($"Research started: {tech.Name} ({tech.Cost:N0} points).");
        StateChanged?.Invoke();
        return null;
    }
}
