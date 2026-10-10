namespace EmpireSim.Core.Models;

/// <summary>Population demographics (4 groups, must sum to total population).</summary>
public sealed class Demographics
{
    public long Children { get; set; }
    public long AdultMen { get; set; }
    public long AdultWomen { get; set; }
    public long Elderly { get; set; }

    public long Total => Children + AdultMen + AdultWomen + Elderly;

    /// <summary>Initialize from total population using default shares.</summary>
    public static Demographics FromPopulation(long total)
    {
        long children = (long)(total * 0.35);
        long adultMen = (long)(total * 0.25);
        long adultWomen = (long)(total * 0.25);
        long elderly = total - children - adultMen - adultWomen; // remainder
        return new Demographics { Children = children, AdultMen = adultMen, AdultWomen = adultWomen, Elderly = elderly };
    }
}

/// <summary>Occupational tax groups (classification, not additional people).</summary>
public sealed class Workforce
{
    public long Peasants { get; set; }
    public long Craftsmen { get; set; }
    public long MilitaryPersonnel { get; set; }
    public long Merchants { get; set; }
    public long Spies { get; set; }
    public long Saboteurs { get; set; }

    /// <summary>Initialize from population and military count.</summary>
    public static Workforce FromPopulation(long total, int soldiers)
    {
        long adults = (long)(total * 0.5);
        return new Workforce
        {
            Peasants = (long)(adults * 0.60),
            Craftsmen = (long)(adults * 0.15),
            MilitaryPersonnel = soldiers,
            Merchants = (long)(adults * 0.08),
            Spies = Math.Max(10, adults / 1000),
            Saboteurs = Math.Max(10, adults / 1000),
        };
    }
}

/// <summary>Tax rates for the six groups (0-100).</summary>
public sealed class TaxRates
{
    public double Peasants { get; set; } = 15;
    public double Craftsmen { get; set; } = 10;
    public double MilitaryPersonnel { get; set; } = 5;
    public double Merchants { get; set; } = 20;
    public double Spies { get; set; } = 10;
    public double Saboteurs { get; set; } = 10;
}

/// <summary>Central taxation calculations.</summary>
public static class TaxationService
{
    // Food consumption weights (relative)
    public const double ChildFood = 0.50;
    public const double AdultManFood = 1.00;
    public const double AdultWomanFood = 0.85;
    public const double ElderlyFood = 0.70;

    // Max tax per person per day (at a 100% tax rate; the rate slider scales it from 0)
    public const double PeasantIncome = 0.00002;
    public const double CraftsmanIncome = 0.0001;
    public const double MilitaryIncome = 0.001;
    public const double MerchantIncome = 0.00013;
    public const double SpyIncome = 0.0013;
    public const double SaboteurIncome = 0.0013;

    // Food tolerance
    public const double NormalSupplyRatio = 1.00;
    public const double FullToleranceRatio = 1.50;
    public const double MaxPenaltyReduction = 0.70; // 70% reduction at 150%+

    /// <summary>Daily food need from demographics (in food units).</summary>
    public static double DailyFoodNeed(Demographics d, double basePerPerson)
    {
        double weight = d.Children * ChildFood + d.AdultMen * AdultManFood
                      + d.AdultWomen * AdultWomanFood + d.Elderly * ElderlyFood;
        long total = d.Total;
        if (total <= 0) return 0;
        // Scale to match existing per-person rate
        double avgWeight = 0.35 * ChildFood + 0.25 * AdultManFood + 0.25 * AdultWomanFood + 0.15 * ElderlyFood;
        return weight / avgWeight * basePerPerson;
    }

    /// <summary>Food supply ratio (1.0 = 100%).</summary>
    public static double FoodSupplyRatio(double foodStock, double dailyProduction, double dailyNeed)
    {
        if (dailyNeed <= 0) return 1.5; // safe default
        // 30-day horizon: stock + production
        double available = foodStock + dailyProduction * 30;
        double required = dailyNeed * 30;
        return available / required;
    }

    /// <summary>Tax tolerance modifier (1.0 = full penalty, lower = reduced).</summary>
    public static double ToleranceModifier(double ratio)
    {
        if (ratio >= FullToleranceRatio) return 1.0 - MaxPenaltyReduction;
        if (ratio <= NormalSupplyRatio) return 1.0;
        // Linear interpolation
        double t = (ratio - NormalSupplyRatio) / (FullToleranceRatio - NormalSupplyRatio);
        return 1.0 - (MaxPenaltyReduction * t);
    }

    /// <summary>Daily tax revenue for one group.</summary>
    public static double GroupRevenue(long count, double incomePerPerson, double rate)
    {
        return count * incomePerPerson * (rate / 100.0);
    }

    /// <summary>Total daily tax revenue.</summary>
    public static double TotalRevenue(Workforce w, TaxRates r)
    {
        return GroupRevenue(w.Peasants, PeasantIncome, r.Peasants)
             + GroupRevenue(w.Craftsmen, CraftsmanIncome, r.Craftsmen)
             + GroupRevenue(w.MilitaryPersonnel, MilitaryIncome, r.MilitaryPersonnel)
             + GroupRevenue(w.Merchants, MerchantIncome, r.Merchants)
             + GroupRevenue(w.Spies, SpyIncome, r.Spies)
             + GroupRevenue(w.Saboteurs, SaboteurIncome, r.Saboteurs);
    }

    /// <summary>Weighted tax burden (0-1).</summary>
    public static double WeightedBurden(Workforce w, TaxRates r)
    {
        double totalIncome = w.Peasants * PeasantIncome + w.Craftsmen * CraftsmanIncome
                           + w.MilitaryPersonnel * MilitaryIncome + w.Merchants * MerchantIncome
                           + w.Spies * SpyIncome + w.Saboteurs * SaboteurIncome;
        if (totalIncome <= 0) return 0;
        double totalTax = TotalRevenue(w, r);
        return totalTax / totalIncome;
    }

    /// <summary>Calculate target approval (0-100).</summary>
    public static double TargetApproval(double burden, double foodRatio)
    {
        // Base: 50 - (burden * 100)
        // e.g., 15% burden -> 35 approval
        double baseApproval = 50 - (burden * 100);
        double modifier = ToleranceModifier(foodRatio);
        // Apply tolerance to the penalty portion
        double penalty = (50 - baseApproval) * modifier;
        double approval = 50 - penalty;
        // Food shortage penalty
        if (foodRatio < 1.0)
            approval -= (1.0 - foodRatio) * 30;
        // Surplus bonus (small)
        if (foodRatio > 1.0)
            approval += Math.Min(10, (foodRatio - 1.0) * 20);
        return Math.Clamp(approval, 0, 100);
    }

    public static string ApprovalLabel(double approval) => approval switch
    {
        < 20 => "Severe dissatisfaction",
        < 40 => "Poor approval",
        < 60 => "Moderate",
        < 80 => "Good approval",
        _ => "Excellent"
    };
}
