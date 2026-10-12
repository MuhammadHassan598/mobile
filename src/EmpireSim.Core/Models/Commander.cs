namespace EmpireSim.Core.Models;

/// <summary>
/// The six posts of a country's command staff. Saved as numbers: new posts are only ever appended, so old saves keep their meaning.
/// Military leadership: <see cref="CommanderInChief"/>, <see cref="LandCommander"/>, <see cref="FleetCommander"/>.
/// Civil administration: <see cref="ChiefTributeCollector"/>, <see cref="HeadOfTrade"/>, <see cref="HeadOfConstruction"/>.
/// </summary>
public enum CommanderRole
{
    CommanderInChief,
    LandCommander,
    FleetCommander,
    ChiefTributeCollector,
    HeadOfTrade,
    HeadOfConstruction,
}

/// <summary>Which half of the command staff a post belongs to.</summary>
public enum StaffGroup
{
    MilitaryLeadership,
    CivilAdministration,
}

/// <summary>
/// An officer or official serving in one post of a country's command staff (a country has at most one holder per post).
/// Saved with the country. Its wage is part of the army's maintenance bill, like every other wage.
/// </summary>
public sealed class Commander
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CommanderRole Role { get; set; }
    public double DailyWage { get; set; }

    /// <summary>
    /// How able the holder is: 1 capable, 2 distinguished, 3 renowned (see <see cref="Services.CommandStaffService.Strength"/>).
    /// Commanders from before skills existed load as capable, which is exactly what they were.
    /// </summary>
    public int Skill { get; set; } = 1;

    /// <summary>The day the holder took the post (null for one appointed before the date was recorded).</summary>
    public DateOnly? AppointedDate { get; set; }
}
