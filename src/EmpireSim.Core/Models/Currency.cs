namespace EmpireSim.Core.Models;

/// <summary>
/// Period currency: Silver and Gold.
/// 1 Gold = 100 Silver. Everyday prices — taxes, upkeep, recruitment,
/// construction — are denominated in Silver; Gold is the high-value
/// purse where wealth is stored.
/// </summary>
public static class Currency
{
    public const double SilverPerGold = 100;
    public const string SilverName = "Silver";
    public const string GoldName = "Gold";

    /// <summary>Full form: "1,240 Gold · 35 Silver".</summary>
    public static string Format(double silverAmount)
    {
        long total = (long)Math.Floor(silverAmount);
        long gold = total / (long)SilverPerGold;
        long silver = total % (long)SilverPerGold;
        if (gold > 0 && silver > 0) return $"{gold:N0} {GoldName} · {silver} {SilverName}";
        if (gold > 0) return $"{gold:N0} {GoldName}";
        return $"{silver} {SilverName}";
    }

    /// <summary>Short form for cost labels: "500 Silver", "12 Gold".</summary>
    public static string Cost(double silverAmount)
    {
        long total = (long)Math.Round(silverAmount);
        if (total >= (long)SilverPerGold && total % (long)SilverPerGold == 0)
            return $"{total / (long)SilverPerGold:N0} {GoldName}";
        return $"{total:N0} {SilverName}";
    }
}
