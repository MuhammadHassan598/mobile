namespace EmpireSim.Core.Models;

/// <summary>Commander appointments. Each role grants an upkeep/tax bonus while filled.</summary>
public enum CommanderRole
{
    CommanderInChief,
    LandCommander,
    FleetCommander
}

public sealed class Commander
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CommanderRole Role { get; set; }
    public double DailyWage { get; set; }
}

/// <summary>Candidate name pools and role definitions for hiring.</summary>
public static class CommanderCatalog
{
    public sealed record RoleSpec(CommanderRole Role, string Title, string Effect, double HireCost, double DailyWage);

    public static readonly IReadOnlyList<RoleSpec> Roles = new List<RoleSpec>
    {
        new(CommanderRole.CommanderInChief, "Commander-in-Chief",
            "−5% all upkeep, +5% taxes",
            HireCost: 2000, DailyWage: 3),
        new(CommanderRole.LandCommander, "Land Commander",
            "−15% land upkeep, −10% land recruit cost",
            HireCost: 1200, DailyWage: 2),
        new(CommanderRole.FleetCommander, "Fleet Commander",
            "−15% naval upkeep, −10% warship recruit cost",
            HireCost: 1200, DailyWage: 2),
    };

    private static readonly Dictionary<CommanderRole, string[]> NamePools = new()
    {
        [CommanderRole.CommanderInChief] = new[] { "Köprülü Mehmed Pasha", "Kara Mustafa Pasha", "Damat Ibrahim Pasha" },
        [CommanderRole.LandCommander] = new[] { "Topal Osman Pasha", "Hasan Pasha", "Ahmed Pasha" },
        [CommanderRole.FleetCommander] = new[] { "Piyale Pasha", "Uluç Ali Reis", "Seydi Ali Reis" },
    };

    public static RoleSpec GetRole(CommanderRole role) =>
        Roles.First(r => r.Role == role);

    /// <summary>Picks the first candidate name not already serving the nation.</summary>
    public static string NextCandidateName(CommanderRole role, IEnumerable<Commander> serving)
    {
        var taken = new HashSet<string>(serving.Select(c => c.Name));
        return NamePools[role].FirstOrDefault(n => !taken.Contains(n))
            ?? $"{GetRole(role).Title} (retainer)";
    }
}
