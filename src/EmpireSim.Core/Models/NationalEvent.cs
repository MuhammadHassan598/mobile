namespace EmpireSim.Core.Models;

/// <summary>National event definition.</summary>
public sealed record NationalEventDefinition(
    string Id,
    string Name,
    string Icon,
    string Description,
    double GoldCost,
    int DurationDays,
    double RulerRatingEffect,
    double FoodRequired = 0);

/// <summary>Registry of national events.</summary>
public static class NationalEventCatalog
{
    public static readonly IReadOnlyList<NationalEventDefinition> All = new List<NationalEventDefinition>
    {
        new("theatre", "Theatre Performance", "🎭",
            "Organize a public theatrical performance to improve the ruler's popularity.",
            1000, 5, 3),
        new("worship", "Worship Service", "🙏",
            "Organize an official public worship service.",
            800, 3, 2),
        new("tournament", "Tournament", "🏆",
            "Organize a public competition or martial tournament.",
            2000, 5, 4),
        new("feast", "Feast", "🍖",
            "Organize a large public feast.",
            3000, 10, 5, FoodRequired: 1000),
        new("carnival", "Carnival", "🎪",
            "Organize a public festival or carnival.",
            5000, 15, 7),
        new("fair", "Fair", "🎡",
            "Organize a public fair that encourages commerce.",
            4000, 20, 6),
    };

    public static NationalEventDefinition? Get(string id) => All.FirstOrDefault(e => e.Id == id);
}

/// <summary>Event instance status.</summary>
public enum NationalEventStatus
{
    InProgress,
    Completed,
    Cancelled
}

/// <summary>An active or completed national event.</summary>
public sealed class NationalEventInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EventTypeId { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly ExpectedCompletion { get; set; }
    public DateOnly? ActualCompletion { get; set; }
    public NationalEventStatus Status { get; set; }
    public double PaidCost { get; set; }
    public bool CompletionApplied { get; set; }

    public int DaysLeft(DateOnly current) =>
        Status == NationalEventStatus.InProgress
            ? Math.Max(0, ExpectedCompletion.DayNumber - current.DayNumber)
            : 0;

    public double Progress(DateOnly current)
    {
        if (Status != NationalEventStatus.InProgress) return 1;
        int total = ExpectedCompletion.DayNumber - StartDate.DayNumber;
        if (total <= 0) return 1;
        return Math.Clamp((double)(current.DayNumber - StartDate.DayNumber) / total, 0, 1);
    }
}
