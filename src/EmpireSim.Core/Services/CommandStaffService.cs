using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>A post of the command staff: what it is called, what it does, and what appointing someone costs.</summary>
public sealed record StaffPostSpec(CommanderRole Role, string Title, string Icon, StaffGroup Group, string Duty, double HireCost, double DailyWage);

/// <summary>Someone who could be appointed to a post (a country's shortlist for it has one candidate of each skill tier).</summary>
/// <param name="Tier">1 capable, 2 distinguished, 3 renowned.</param>
/// <param name="Strength">How strongly the post's effects work with this person in it (1.0 for a capable holder).</param>
public sealed record StaffCandidate(string Id, CommanderRole Role, int Tier, string Name, string TierName, double HireCost, double DailyWage, double Strength);

/// <summary>
/// A country's command staff: the commander-in-chief, the land and fleet commanders, the chief tribute collector, the head of trade
/// and the head of construction. Every country keeps its own appointments (<see cref="Nation.Commanders"/>, at most one holder per post,
/// saved with the country), and every effect below is computed from the country's own staff — so the player and the AI countries are
/// treated exactly alike. Nothing here is decoration: each effect is read by the rule it changes (upkeep, recruitment, battle strength,
/// attrition, tax, tribute, trade prices, construction time).
/// </summary>
public static class CommandStaffService
{
    public static readonly IReadOnlyList<StaffPostSpec> Posts = new List<StaffPostSpec>
    {
        new(CommanderRole.FleetCommander, "Fleet Commander", "⚓", StaffGroup.MilitaryLeadership,
            "Runs the navy: cheaper to keep and to build up.", Balance.FleetCommanderHireCost, Balance.FleetCommanderWage),
        new(CommanderRole.LandCommander, "Land Forces Commander", "🪖", StaffGroup.MilitaryLeadership,
            "Runs the army: cheaper to keep and to raise, and stronger in the field.", Balance.LandCommanderHireCost, Balance.LandCommanderWage),
        new(CommanderRole.CommanderInChief, "Commander-in-Chief", "🎖️", StaffGroup.MilitaryLeadership,
            "Commands the whole establishment: a lighter bill for every force, and fewer men lost to the hardships of war.", Balance.CommanderInChiefHireCost, Balance.CommanderInChiefWage),
        new(CommanderRole.ChiefTributeCollector, "Chief Tribute Collector", "🪙", StaffGroup.CivilAdministration,
            "Gathers the crown's revenue: more from taxes, and more from the countries that pay you tribute.", Balance.CivilPostHireCost, Balance.CivilPostWage),
        new(CommanderRole.HeadOfTrade, "Head of Trade", "⚖️", StaffGroup.CivilAdministration,
            "Runs the crown's commerce: better prices on what you buy and sell, and richer trade pacts.", Balance.CivilPostHireCost, Balance.CivilPostWage),
        new(CommanderRole.HeadOfConstruction, "Head of Construction", "🏗️", StaffGroup.CivilAdministration,
            "Directs the builders: farms, mines, workshops and mills go up sooner.", Balance.CivilPostHireCost, Balance.CivilPostWage),
    };

    public static StaffPostSpec Post(CommanderRole role) => Posts.First(p => p.Role == role);

    public static string TierName(int tier) => tier switch { <= 1 => "Capable", 2 => "Distinguished", _ => "Renowned" };

    /// <summary>How strongly a holder of this skill works the post's effects (a capable holder is 1.0).</summary>
    public static double Strength(int skill) => skill switch
    {
        <= 1 => 1.0,
        2 => Balance.StaffTier2Strength,
        _ => Balance.StaffTier3Strength,
    };

    private static double HireCostMult(int tier) => tier switch { <= 1 => 1.0, 2 => Balance.StaffTier2HireCost, _ => Balance.StaffTier3HireCost };
    private static double WageMult(int tier) => tier switch { <= 1 => 1.0, 2 => Balance.StaffTier2Wage, _ => Balance.StaffTier3Wage };

    // ================= Who holds what =================

    /// <summary>The country's holder of a post, or null when it is vacant.</summary>
    public static Commander? Holder(Nation n, CommanderRole role) => n.Commanders.FirstOrDefault(c => c.Role == role);

    /// <summary>0 when the post is vacant, otherwise how strongly its holder works the post's effects.</summary>
    public static double Strength(Nation n, CommanderRole role) => Holder(n, role) is { } h ? Strength(h.Skill) : 0;

    /// <summary>What the whole staff costs per day in wages.</summary>
    public static double DailyWages(Nation n) => n.Commanders.Sum(c => c.DailyWage);

    // ================= Candidates =================

    /// <summary>The shortlist for a post: one candidate of each skill tier, the same every time for the same country.</summary>
    public static List<StaffCandidate> Candidates(Nation n, CommanderRole role)
    {
        var spec = Post(role);
        var names = NamesFor(n);
        return Enumerable.Range(1, 3).Select(tier => new StaffCandidate(
            Id: $"{(int)role}:{tier}", Role: role, Tier: tier, Name: names[(role, tier)], TierName: TierName(tier),
            HireCost: Math.Round(spec.HireCost * HireCostMult(tier), 2), DailyWage: Math.Round(spec.DailyWage * WageMult(tier), 3),
            Strength: Strength(tier))).ToList();
    }

    public static StaffCandidate? Candidate(Nation n, CommanderRole role, int tier) =>
        Candidates(n, role).FirstOrDefault(c => c.Tier == tier);

    private static int Hash(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in text) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    /// <summary>Every candidate's name for a country: drawn from its culture's names by its id, each name used once across the staff.</summary>
    private static Dictionary<(CommanderRole, int), string> NamesFor(Nation n)
    {
        var pool = PoolFor(n);
        var used = new HashSet<string>();
        var names = new Dictionary<(CommanderRole, int), string>();
        foreach (var post in Posts)
            for (int tier = 1; tier <= 3; tier++)
            {
                int i = Hash($"{n.Id}|{(int)post.Role}|{tier}") % pool.Length;
                while (!used.Add(pool[i])) i = (i + 1) % pool.Length;   // (the pool has more names than the staff has candidates)
                names[(post.Role, tier)] = pool[i];
            }
        return names;
    }

    // Candidate names are generated placeholders drawn from the naming of the country's culture (by faith and region); they are not
    // historical office-holders. Each pool has at least 18 names, one per candidate of the six posts.
    private static readonly string[] IslamicNames =
    {
        "Mehmed Köprülü", "Mustafa Kemankeş", "Hasan Sokollu", "Ibrahim Rüstem", "Ali Hamidi", "Murad Kasimi", "Süleyman Aydın",
        "Osman Yaman", "Yusuf Sinan", "Hüseyin Çelebi", "Ahmed Bostancı", "Kasım Reis", "Ismail Tahir", "Davud Halil", "Selim Aga",
        "Bayram Çavuş", "Zeynel Abidin", "Nasuh Hızır", "Idris Bitlisi", "Fazıl Mirza",
    };
    private static readonly string[] WesternChristianNames =
    {
        "Alonso de Vargas", "Pierre de Montclair", "Johan van der Hoeven", "Henry Calloway", "Giovanni Morosini", "Karl von Reiden",
        "Diego de Salazar", "Antoine Beaumont", "Willem Bakker", "Edmund Fairfax", "Lorenzo Contarini", "Friedrich Holstein",
        "Íñigo de Luna", "Jacques de Verdun", "Anders Lindqvist", "Robert Haldane", "Matteo Doria", "Gerhard Falkenstein",
        "Thomas Wyndham", "Hendrik Coen",
    };
    private static readonly string[] SlavicNames =
    {
        "Piotr Zamoyski", "Ivan Vorontsov", "Stefan Ostrogski", "Dmitri Pozharsky", "Jan Chodkiewicz", "Boris Saltykov", "Mikołaj Radziwiłł",
        "Fyodor Sheremetev", "Andrzej Tarnowski", "Vasili Golitsyn", "Zdenko Frankopan", "Pavel Obolensky", "Krzysztof Sapieha",
        "Aleksei Trubetskoy", "Nikola Zrinski", "Grigori Morozov", "Tomasz Kmita", "Semyon Odoevsky", "Ivo Gundulić", "Matvei Belsky",
    };
    private static readonly string[] AfricanNames =
    {
        "Ras Takla Giyorgis", "Ato Wolde Mariam", "Dom Afonso Nkuwu", "Fitawrari Haile", "Ngola Mbandi", "Dejazmach Kassa", "Mani Vunda",
        "Blattengeta Zena", "Dom Pedro Mvemba", "Ras Mikael", "Nzinga Cuvo", "Ato Yohannes", "Kimpanzu Lukeni", "Basha Ezana", "Dom Garcia Nkanga",
        "Ras Amda", "Mani Soyo", "Fitawrari Dawit", "Ngunu Mpanzu", "Ato Gebre",
    };
    private static readonly string[] EastAsianNames =
    {
        "Li Chengliang", "Toyotomi Masanori", "Yi Sun-sin", "Zhang Juzheng", "Kato Kiyomasa", "Wang Shizhen", "Ming Zhuoran", "Konishi Yukinaga",
        "Kim Myeong-won", "Qi Jiguang", "Shimazu Tadatsune", "Hu Zongxian", "Won Gyun", "Tokugawa Hidetada", "Yu Dayou", "Cho Heon",
        "Maeda Toshinaga", "Liu Ting", "Kuroda Nagamasa", "Shin Rip",
    };
    private static readonly string[] SoutheastAsianNames =
    {
        "Phraya Kosa", "Nguyen Hoang", "Bayinnaung Thet", "Okya Chakrei", "Lê Văn Hựu", "Phraya Sri Suriyawong", "Maha Thray Sithu",
        "Trần Quốc Tuấn", "Chao Fa Ngum", "Nanda Yazathu", "Khun Luang Pha", "Pho Ngo Khan", "Tay Min Sa", "Mac Kinh Dien", "Phraya Chakri",
        "Sihathura Min", "Boramaracha Sen", "Dai Viet Quang", "Setthathirath Souk", "Chakkraphat Sen",
    };
    private static readonly string[] IndianNames =
    {
        "Raja Ramchandra Rao", "Man Singh Kachhwaha", "Todar Mal Khatri", "Lachit Phukan", "Krishnadeva Nayak", "Birbal Dube", "Pratap Rana",
        "Bhagwan Das Rathore", "Pratap Malla", "Raghunatha Nayaka", "Madho Singh Hada", "Shivaji Bhonsle", "Jai Singh Sisodia", "Mukunda Deva",
        "Narasimha Verma", "Raja Gajapati", "Bhim Shah Thapa", "Chandra Sen Rathod", "Amar Singh Chundawat", "Gopal Rai",
    };
    private static readonly string[] IslandNames =
    {
        "Tui Manu'a", "Ariki Tangaroa", "Nahnmwarki Soukise", "Kaha Nui", "Moana Tavake", "Tamatoa Rapu", "Ariki Hotu", "Lapita Maro",
        "Matua Hiro", "Rongo Atua", "Tane Mahuta", "Pele Aumoe", "Ku Kaili", "Makahiki Ono", "Haumea Lani", "Iolani Kane", "Tui Tonga Fa",
        "Maui Nui", "Ariki Roa", "Kapu Aina",
    };

    private static string[] PoolFor(Nation n)
    {
        if (n.Id is "ethiopia" or "kongo") return AfricanNames;
        if (n.Id is "poland" or "russia" or "croatia") return SlavicNames;
        return (n.Religion ?? "") switch
        {
            "Islam" => IslamicNames,
            "Christianity" => WesternChristianNames,
            "Confucianism" or "Shinto" => EastAsianNames,
            "Buddhism" or "Shamanism" => SoutheastAsianNames,
            "Hinduism" => IndianNames,
            "Animism" => IslandNames,
            _ => WesternChristianNames,
        };
    }

    // ================= Appointing and dismissing =================

    /// <summary>Why this appointment cannot be made (null = it can): the country exists, the candidate is a real one, and the treasury can pay.</summary>
    public static string? AppointBlock(Nation n, CommanderRole role, int tier)
    {
        if (n.IsEliminated) return $"{n.Name} no longer exists.";
        if (Candidate(n, role, tier) is not { } c) return "Choose one of the candidates.";
        if (Holder(n, role) is { } holder && holder.Name == c.Name) return $"{c.Name} already holds this post.";
        if (n.Commanders.Any(x => x.Name == c.Name && x.Role != role)) return $"{c.Name} already serves in another post.";
        if (!n.CanPay(c.HireCost)) return $"Appointing {c.Name} costs {Currency.Cost(c.HireCost)}; the treasury holds {Currency.Cost(n.Gold)}.";
        return null;
    }

    /// <summary>
    /// Appoints a candidate to a post, replacing whoever held it (the old holder leaves; the post never has two). The country pays the
    /// candidate's hire cost, and from then on their wage with the army's maintenance. Returns an error message, or null on success.
    /// </summary>
    public static string? Appoint(GameState state, Nation n, CommanderRole role, int tier)
    {
        if (AppointBlock(n, role, tier) is { } why) return why;
        var c = Candidate(n, role, tier)!;
        var spec = Post(role);

        var previous = Holder(n, role);
        n.Commanders.RemoveAll(x => x.Role == role);
        n.PayGold(c.HireCost);
        n.Commanders.Add(new Commander
        {
            Name = c.Name, Role = role, Skill = tier, DailyWage = c.DailyWage, AppointedDate = state.CurrentDate,
        });
        if (n.IsPlayer)
            state.Log(previous is null
                ? $"Appointed {c.Name} ({c.TierName}) as {spec.Title}."
                : $"Replaced {previous.Name} with {c.Name} ({c.TierName}) as {spec.Title}.");
        return null;
    }

    /// <summary>Dismisses the holder of a post (nothing is refunded). Returns the one who left, or null if it was vacant.</summary>
    public static Commander? Dismiss(GameState state, Nation n, CommanderRole role)
    {
        var holder = Holder(n, role);
        if (holder is null) return null;
        n.Commanders.RemoveAll(x => x.Role == role);
        if (n.IsPlayer) state.Log($"Dismissed {holder.Name} as {Post(role).Title}.");
        return holder;
    }

    /// <summary>
    /// An AI country fills a vacant post the way a careful ruler would: one post per turn, with its capable candidate, only for posts it
    /// has something to run in (an army, a fleet, taxes — it has no trade or construction of its own), and only when its treasury is
    /// many times the hire cost.
    /// </summary>
    public static void AiAppoint(GameState state, Nation n)
    {
        foreach (var post in Posts)
        {
            bool useful = post.Role switch
            {
                CommanderRole.LandCommander or CommanderRole.CommanderInChief => n.Soldiers > 0,
                CommanderRole.FleetCommander => n.Warships > 0,
                CommanderRole.ChiefTributeCollector => true,
                _ => false,
            };
            if (!useful || Holder(n, post.Role) is not null) continue;
            var candidate = Candidates(n, post.Role)[0];
            if (n.Gold < candidate.HireCost * Balance.AiStaffReserve) return;
            Appoint(state, n, post.Role, 1);
            return;
        }
    }

    // ================= Effects (each read by the rule it changes) =================

    private static double Reduce(double baseMult, double strength) => strength <= 0 ? 1.0 : Math.Max(Balance.StaffMinMult, 1 - (1 - baseMult) * strength);
    private static double Raise(double baseMult, double strength) => strength <= 0 ? 1.0 : 1 + (baseMult - 1) * strength;
    private static double Floor(double mult) => Math.Max(Balance.StaffMinMult, mult);

    /// <summary>Land upkeep: the land commander's cut and the commander-in-chief's.</summary>
    public static double LandUpkeepMult(Nation n) =>
        Floor(Reduce(Balance.LandCommanderUpkeepMult, Strength(n, CommanderRole.LandCommander))
              * Reduce(Balance.CinCUpkeepMult, Strength(n, CommanderRole.CommanderInChief)));

    /// <summary>Naval upkeep: the fleet commander's cut and the commander-in-chief's.</summary>
    public static double NavalUpkeepMult(Nation n) =>
        Floor(Reduce(Balance.FleetCommanderUpkeepMult, Strength(n, CommanderRole.FleetCommander))
              * Reduce(Balance.CinCUpkeepMult, Strength(n, CommanderRole.CommanderInChief)));

    public static double LandRecruitMult(Nation n) => Reduce(Balance.LandCommanderRecruitMult, Strength(n, CommanderRole.LandCommander));
    public static double WarshipRecruitMult(Nation n) => Reduce(Balance.FleetCommanderRecruitMult, Strength(n, CommanderRole.FleetCommander));

    /// <summary>Battle strength of the country's armies (multiplies the existing <see cref="Nation.BattleStrengthMult"/>).</summary>
    public static double BattleStrengthMult(Nation n) => Raise(Balance.LandCommanderBattleMult, Strength(n, CommanderRole.LandCommander));

    /// <summary>Soldiers lost to attrition while at war (multiplies <see cref="Balance.WarAttritionPerDay"/>).</summary>
    public static double AttritionMult(Nation n) => Reduce(Balance.CinCAttritionMult, Strength(n, CommanderRole.CommanderInChief));

    public static double TaxMult(Nation n) => Raise(Balance.TributeCollectorTaxMult, Strength(n, CommanderRole.ChiefTributeCollector));
    public static double TributeMult(Nation n) => Raise(Balance.TributeCollectorTributeMult, Strength(n, CommanderRole.ChiefTributeCollector));

    public static double ImportPriceMult(Nation n) => Reduce(Balance.TradeHeadImportMult, Strength(n, CommanderRole.HeadOfTrade));
    public static double ExportRevenueMult(Nation n) => Raise(Balance.TradeHeadExportMult, Strength(n, CommanderRole.HeadOfTrade));
    public static double TradePactIncomeMult(Nation n) => Raise(Balance.TradeHeadPactIncomeMult, Strength(n, CommanderRole.HeadOfTrade));

    public static double ConstructionTimeMult(Nation n) => Reduce(Balance.ConstructionHeadTimeMult, Strength(n, CommanderRole.HeadOfConstruction));

    /// <summary>
    /// Days a build takes once the head of construction has had his say: the nearest whole day (so even a short build gains a day when the
    /// cut is big enough to matter), never under a day.
    /// </summary>
    public static int BuildDays(Nation n, double baseDays) =>
        Math.Max(1, (int)Math.Round(baseDays * ConstructionTimeMult(n), MidpointRounding.AwayFromZero));

    // ================= What a post does, for the screen (the numbers are the ones above, so they cannot drift) =================

    private static string Pct(double fraction) => $"{Math.Round(Math.Abs(fraction) * 100, 1):0.#}%";

    /// <summary>The effects of a post at a given strength, one line each. Only what the rules really do.</summary>
    public static List<string> EffectLines(CommanderRole role, double strength)
    {
        double s = strength <= 0 ? 1 : strength;
        return role switch
        {
            CommanderRole.LandCommander => new List<string>
            {
                $"−{Pct(1 - Reduce(Balance.LandCommanderUpkeepMult, s))} land upkeep",
                $"−{Pct(1 - Reduce(Balance.LandCommanderRecruitMult, s))} cost of recruiting soldiers",
                $"+{Pct(Raise(Balance.LandCommanderBattleMult, s) - 1)} battle strength of its armies when they attack",
            },
            CommanderRole.FleetCommander => new List<string>
            {
                $"−{Pct(1 - Reduce(Balance.FleetCommanderUpkeepMult, s))} naval upkeep",
                $"−{Pct(1 - Reduce(Balance.FleetCommanderRecruitMult, s))} cost of building warships",
            },
            CommanderRole.CommanderInChief => new List<string>
            {
                $"−{Pct(1 - Reduce(Balance.CinCUpkeepMult, s))} upkeep of all forces, land and naval",
                $"−{Pct(1 - Reduce(Balance.CinCAttritionMult, s))} soldiers lost to attrition while at war",
            },
            CommanderRole.ChiefTributeCollector => new List<string>
            {
                $"+{Pct(Raise(Balance.TributeCollectorTaxMult, s) - 1)} tax revenue",
                $"+{Pct(Raise(Balance.TributeCollectorTributeMult, s) - 1)} tribute collected from other countries",
            },
            CommanderRole.HeadOfTrade => new List<string>
            {
                $"−{Pct(1 - Reduce(Balance.TradeHeadImportMult, s))} price of goods you buy",
                $"+{Pct(Raise(Balance.TradeHeadExportMult, s) - 1)} revenue from goods you sell",
                $"+{Pct(Raise(Balance.TradeHeadPactIncomeMult, s) - 1)} trade-pact income",
            },
            _ => new List<string>
            {
                $"−{Pct(1 - Reduce(Balance.ConstructionHeadTimeMult, s))} construction time (never under a day)",
            },
        };
    }

    /// <summary>What a post cannot do yet, stated plainly (null when everything it is meant to do works).</summary>
    public static string? Limitation(CommanderRole role) => role switch
    {
        CommanderRole.FleetCommander => "Naval battles are not part of the game yet, so a fleet commander has no fighting bonus.",
        _ => null,
    };
}
