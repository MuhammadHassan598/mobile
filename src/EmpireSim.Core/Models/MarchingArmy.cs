namespace EmpireSim.Core.Models;

/// <summary>
/// An army marching to invade a whole nation. The force is committed up front;
/// the battle resolves when the march completes.
/// </summary>
public sealed class MarchingArmy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AttackerNationId { get; set; } = "";
    public string AttackerNationName { get; set; } = "";
    public string TargetNationId { get; set; } = "";
    public string TargetNationName { get; set; } = "";
    public List<UnitStack> Force { get; set; } = new();
    public int DaysLeft { get; set; }
    public int TotalDays { get; set; }

    public double Progress => TotalDays <= 0 ? 1 : 1 - (double)DaysLeft / TotalDays;
    public int Strength => Force.Sum(s => s.Count);
}
