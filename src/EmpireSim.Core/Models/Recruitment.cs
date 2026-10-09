namespace EmpireSim.Core.Models;

/// <summary>A batch of soldiers being recruited over game time.</summary>
public sealed class RecruitmentProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public UnitType Type { get; set; }
    public int Count { get; set; }
    public double TotalDays { get; set; }
    public double DaysLeft { get; set; }
    public double Progress => TotalDays > 0 ? 1 - DaysLeft / TotalDays : 0;
    /// <summary>Equipment consumed upfront (item ID -> amount).</summary>
    public Dictionary<string, double> EquipmentUsed { get; set; } = new();
    public double GoldPaid { get; set; }
}

/// <summary>A hireable mercenary company.</summary>
public sealed record MercenaryCompany(
    string Id,
    string Name,
    string Icon,
    UnitType UnitType,
    int Available,
    double HireCostPerSoldier,
    double UpkeepPerSoldierPerDay,
    int ContractDays,
    double Morale);

/// <summary>Catalogue of mercenary companies.</summary>
public static class MercenaryCatalog
{
    public static readonly IReadOnlyList<MercenaryCompany> All = new List<MercenaryCompany>
    {
        new("swiss_guard", "Swiss Guard", "🛡️", UnitType.Pikeman, 2000, 1.5, 0.002, 90, 0.8),
        new("landsknecht", "Landsknecht", "⚔️", UnitType.Musketeer, 1500, 2.0, 0.0025, 60, 0.7),
        new("cossacks", "Cossack Host", "🐎", UnitType.Cavalry, 800, 3.0, 0.004, 60, 0.75),
        new("artillery_co", "Artillery Company", "💣", UnitType.Cannon, 200, 8.0, 0.01, 90, 0.7),
    };

    public static MercenaryCompany? Get(string id) => All.FirstOrDefault(c => c.Id == id);
}

/// <summary>An active mercenary contract.</summary>
public sealed class MercenaryContract
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = "";
    public int Count { get; set; }
    public DateOnly Expires { get; set; }
}

/// <summary>Conscription level configuration.</summary>
public sealed record ConscriptionLevel(
    string Id,
    string Name,
    string Icon,
    double PopulationFraction,
    double GoldCostPerSoldier,
    int DaysToTrain,
    double UnrestPenalty,
    string Description);

/// <summary>The three conscription levels.</summary>
public static class ConscriptionLevels
{
    public static readonly IReadOnlyList<ConscriptionLevel> All = new List<ConscriptionLevel>
    {
        new("limited", "Limited Conscription", "📋", 0.02, 0.1, 3, 5,
            "Call up 2% of population. Fast, low unrest."),
        new("national", "National Conscription", "📜", 0.05, 0.08, 5, 12,
            "Call up 5% of population. Medium speed and unrest."),
        new("total", "Total Mobilization", "🚨", 0.10, 0.05, 7, 25,
            "Call up 10% of population. Slow, high unrest."),
    };

    public static ConscriptionLevel? Get(string id) => All.FirstOrDefault(l => l.Id == id);
}
