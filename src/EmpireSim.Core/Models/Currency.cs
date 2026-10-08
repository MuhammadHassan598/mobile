namespace EmpireSim.Core.Models;

/// <summary>
/// Gold is the single currency of the realm.
/// </summary>
public static class Currency
{
    public const string GoldName = "Gold";

    /// <summary>Full form: "1,240 Gold".</summary>
    public static string Format(double goldAmount)
    {
        long gold = (long)Math.Floor(goldAmount);
        return $"{gold:N0} {GoldName}";
    }

    /// <summary>Short form for cost labels: "500 Gold".</summary>
    public static string Cost(double goldAmount)
    {
        long total = (long)Math.Round(goldAmount);
        return $"{total:N0} {GoldName}";
    }
}
