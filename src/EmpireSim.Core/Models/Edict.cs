namespace EmpireSim.Core.Models;

/// <summary>Laws the ruler can enact. Each trades something for something.</summary>
public enum EdictType
{
    WarTaxes,
    GrainDole,
    MilitaryDrills,
    MerchantCharters
}

public enum ReligiousStance
{
    Pragmatic,
    Devout,
    Tolerant
}

public sealed record EdictSpec(
    EdictType Type,
    string Name,
    string Effect,
    string TradeOff,
    double EnactCost);

/// <summary>The catalogue of enactable laws.</summary>
public static class EdictCatalog
{
    public static readonly IReadOnlyList<EdictSpec> All = new List<EdictSpec>
    {
        new(EdictType.WarTaxes, "War Taxes",
            "+25% tax income",
            "Population growth halved",
            EnactCost: 200),
        new(EdictType.GrainDole, "Grain Dole",
            "+50% population growth",
            "−15% tax income",
            EnactCost: 200),
        new(EdictType.MilitaryDrills, "Military Drills",
            "+10% battle strength",
            "+10% army upkeep",
            EnactCost: 200),
        new(EdictType.MerchantCharters, "Merchant Charters",
            "+50% trade pact income",
            "−10% tax income",
            EnactCost: 200),
    };

    public static EdictSpec Get(EdictType type) => All.First(e => e.Type == type);

    public static string StanceDescription(ReligiousStance stance) => stance switch
    {
        ReligiousStance.Devout => "+20% population growth, −10% tax income",
        ReligiousStance.Tolerant => "+20% trade income, faster warming of relations, −10% growth",
        _ => "No effects. The state keeps its distance from the clergy.",
    };
}
