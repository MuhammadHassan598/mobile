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

    /// <summary>The day the army set out (saved, so progress survives save / load).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// The day the army arrives and the battle is fought. It is what the daily update goes by, so an army that is saved and loaded
    /// arrives on the same day. (A march from an old save has no dates: it counts down <see cref="DaysLeft"/> as before.)
    /// </summary>
    public DateOnly ArrivalDate { get; set; }

    /// <summary>Whether this march knows its dates (see <see cref="ArrivalDate"/>).</summary>
    public bool HasDates => ArrivalDate != default;

    /// <summary>Sets the route: set out <paramref name="today"/>, arrive <paramref name="days"/> days later.</summary>
    public void SetRoute(DateOnly today, int days)
    {
        StartDate = today;
        ArrivalDate = today.AddDays(days);
        TotalDays = days;
        DaysLeft = days;
    }

    public double Progress => TotalDays <= 0 ? 1 : 1 - (double)DaysLeft / TotalDays;
    public int Strength => Force.Sum(s => s.Count);
}
