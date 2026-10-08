using EmpireSim.Core.Models;

namespace EmpireSim.Core.Models;

/// <summary>A building under construction in a province. Costs are paid
/// upfront; when <see cref="DaysLeft"/> reaches zero the building is added.</summary>
public sealed class ConstructionProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProvinceId { get; set; } = "";
    public string ProvinceName { get; set; } = "";
    public BuildingType Building { get; set; }
    public int DaysLeft { get; set; }
    public int TotalDays { get; set; }

    public double Progress => TotalDays <= 0 ? 1 : 1 - (double)DaysLeft / TotalDays;
}
