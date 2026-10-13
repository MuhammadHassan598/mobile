using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The player's side of Annexed States (the rules are in <see cref="AnnexedStateService"/>). The player manages only the territories
/// their own country annexed; every operation names the territory and is refused, with the reason, for any other country.
/// </summary>
public sealed partial class GameEngine
{
    /// <summary>The territories the player's country rules (annexed countries now under it), in the order they were annexed.</summary>
    public List<Nation> AnnexedTerritories => AnnexedStateService.TerritoriesOf(State, State.PlayerNation);

    /// <summary>Why the player may not manage a territory (null = they may).</summary>
    public string? TerritoryAuthority(string territoryId) =>
        AnnexedStateService.Authority(State, State.PlayerNation, State.AllNations().FirstOrDefault(n => n.Id == territoryId));

    /// <summary>A territory the player rules, by id (null for anything else).</summary>
    public Nation? Territory(string territoryId) => AnnexedStateService.Find(State, State.PlayerNation, territoryId);

    /// <summary>The colonies the player's country owns (settled land, not conquered countries), in the order they were founded.</summary>
    public List<Nation> OwnedColonyTerritories => State.ColoniesOf(State.PlayerNation.Id);

    /// <summary>Reports for every colony the player owns.</summary>
    public List<TerritoryReport> ColonyReports() =>
        OwnedColonyTerritories.Select(t => AnnexedStateService.Report(State, State.PlayerNation, t)).ToList();

    public TerritoryReport? ReportTerritory(string territoryId) =>
        Territory(territoryId) is { } t ? AnnexedStateService.Report(State, State.PlayerNation, t) : null;

    /// <summary>Reports for every territory the player rules.</summary>
    public List<TerritoryReport> TerritoryReports() =>
        AnnexedTerritories.Select(t => AnnexedStateService.Report(State, State.PlayerNation, t)).ToList();

    public TaxPreview? PreviewTerritoryTaxes(string territoryId, TaxRates rates) =>
        Territory(territoryId) is { } t ? AnnexedStateService.PreviewTaxes(State, State.PlayerNation, t, rates) : null;

    /// <summary>Sets a territory's tax rates. Returns an error message, or null on success.</summary>
    public string? SetTerritoryTaxRates(string territoryId, TaxRates rates)
    {
        if (TerritoryAuthority(territoryId) is { } why) return why;
        var error = AnnexedStateService.SetTaxRates(State, State.PlayerNation, Territory(territoryId)!, rates);
        if (error is null) StateChanged?.Invoke();
        return error;
    }

    public BuildQuote? QuoteTerritoryProduction(string territoryId, string buildingId, int count) =>
        Territory(territoryId) is { } t ? AnnexedStateService.QuoteProduction(State, State.PlayerNation, t, buildingId, count) : null;

    public BuildQuote? QuoteTerritoryBuilding(string territoryId, BuildingType type) =>
        Territory(territoryId) is { } t ? AnnexedStateService.QuoteBasic(State, State.PlayerNation, t, type) : null;

    public (int Max, string LimitedBy)? MaxTerritoryProduction(string territoryId, string buildingId) =>
        Territory(territoryId) is { } t ? AnnexedStateService.MaxProduction(State, State.PlayerNation, t, buildingId) : null;

    /// <summary>Starts building production buildings in a territory (gold from the treasury, materials from the territory's stockpile).</summary>
    public string? BuildProductionInTerritory(string territoryId, string buildingId, int count)
    {
        if (TerritoryAuthority(territoryId) is { } why) return why;
        var error = AnnexedStateService.BuildProduction(State, State.PlayerNation, Territory(territoryId)!, buildingId, count);
        if (error is null) StateChanged?.Invoke();
        return error;
    }

    /// <summary>Starts building a farm, mine, sawmill or workshop in a territory.</summary>
    public string? BuildBasicInTerritory(string territoryId, BuildingType type)
    {
        if (TerritoryAuthority(territoryId) is { } why) return why;
        var error = AnnexedStateService.BuildBasic(State, State.PlayerNation, Territory(territoryId)!, type);
        if (error is null) StateChanged?.Invoke();
        return error;
    }

    /// <summary>Sends stock from a territory to the national stockpile.</summary>
    public string? ShipTerritoryStock(string territoryId, string productId, double quantity)
    {
        if (TerritoryAuthority(territoryId) is { } why) return why;
        var error = AnnexedStateService.ShipStock(State, State.PlayerNation, Territory(territoryId)!, productId, quantity);
        if (error is null) StateChanged?.Invoke();
        return error;
    }

    /// <summary>
    /// The realm's daily income as the top bar shows it: the player's own country plus the tax and gold its territories pay it
    /// (each counted once).
    /// </summary>
    public long RealmDailyIncome()
    {
        var player = State.PlayerNation;
        double territories = State.TerritoriesOf(player.Id).Sum(t => AnnexedStateService.TaxRevenue(player, t) + AnnexedStateService.OtherIncome(State, t));   // annexed countries and colonies, each once
        return EstimateDailyIncome(player) + (long)territories;
    }

    /// <summary>Everyone under the player's rule, counted once: their own people and their territories'.</summary>
    public long RealmPopulation => State.TotalPopulation(State.PlayerNation);
}
