namespace EmpireSim.Core.Models;

/// <summary>Who an Assembly proposal applies to.</summary>
public enum AssemblyScope
{
    /// <summary>One chosen country: the proposal needs a target, and only that country is affected.</summary>
    Country,
    /// <summary>Every country covered by the Assembly rules (player and AI alike): no target is chosen or stored.</summary>
    Global,
}

/// <summary>Assembly proposal type definition.</summary>
/// <param name="Scope">Whether it applies to one chosen country or to all of them.</param>
/// <param name="NeedsProduct">Whether the proposer must also choose a product (from the trade catalogue).</param>
/// <param name="Offered">Whether the form offers it. A type retired from the form stays here so proposals and laws already in a save still read and still hold.</param>
public sealed record AssemblyProposalType(
    string Id,
    string Name,
    string Icon,
    string Description,
    string EffectDescription,
    AssemblyScope Scope = AssemblyScope.Country,
    bool NeedsProduct = false,
    bool Offered = true);

/// <summary>Registry of proposal types.</summary>
public static class AssemblyProposalTypes
{
    public const string MilitaryRestriction = "military_restriction";
    public const string WeaponSalesBan = "weapon_sales_ban";
    public const string ProductionBan = "production_ban";
    public const string AnnexationBan = "annexation_ban";
    public const string WarBan = "war_ban";

    public static readonly IReadOnlyList<AssemblyProposalType> All = new List<AssemblyProposalType>
    {
        new(MilitaryRestriction, "Military Training Restriction", "🎖️",
            "Restrict military training in one country.",
            "Recruiting in that country is halved while the law lasts (its soldiers take twice as long to train).",
            AssemblyScope.Country),
        new(WeaponSalesBan, "Prohibition on Weapon Sales", "🚫",
            "Prohibit the sale of military equipment everywhere.",
            "No country may buy or sell military equipment while the law lasts.",
            AssemblyScope.Global),
        new(ProductionBan, "Prohibition on Production", "🏭",
            "Prohibit the production of one product everywhere.",
            "No country can produce the chosen product while the law lasts; everything else is untouched.",
            AssemblyScope.Global, NeedsProduct: true),
        new(AnnexationBan, "Prohibition of Annexation", "🛡️",
            "Prohibit annexation everywhere.",
            "No country can be annexed while the law lasts, by anyone, by battle or by treaty.",
            AssemblyScope.Global),
        new(WarBan, "Prohibition on War", "🕊️",
            "Prohibit declarations of war everywhere.",
            "No country can declare a new war while the law lasts. Wars already under way go on.",
            AssemblyScope.Global),
        // Retired from the form (it was a country-by-country version of the weapon-sales ban); laws already passed still hold.
        new("weapon_embargo", "Weapon Sales Embargo", "⛔",
            "Embargo on military equipment exports to one country.",
            "Military equipment exports to that country are blocked.",
            AssemblyScope.Country, Offered: false),
    };

    /// <summary>The types the Make a Suggestion form offers, in order.</summary>
    public static IEnumerable<AssemblyProposalType> Offered => All.Where(t => t.Offered);

    public static AssemblyProposalType? Get(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>Proposal status.</summary>
public enum ProposalStatus
{
    VotingOpen,
    Approved,
    Rejected,
    Active,
    Expired
}

/// <summary>An assembly proposal.</summary>
public sealed class AssemblyProposal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TypeId { get; set; } = "";
    public string ProposerId { get; set; } = "";
    /// <summary>The country a country-scoped proposal is about; empty for a global one (a global proposal has no target).</summary>
    public string TargetId { get; set; } = "";

    /// <summary>The product a production ban is about (a trade-catalogue id); empty for every other type.</summary>
    public string ProductId { get; set; } = "";
    public int EffectDurationDays { get; set; }
    public DateOnly CreatedDate { get; set; }
    public DateOnly VotingDeadline { get; set; }
    public ProposalStatus Status { get; set; }
    public Dictionary<string, bool> Votes { get; set; } = new(); // countryId -> for(true)/against(false)
    public DateOnly? ActiveUntil { get; set; }

    /// <summary>Votes cast for, weighted by voting power. <paramref name="nations"/> must be every nation (annexed territories count toward their master's power).</summary>
    public int VotesFor(IEnumerable<Nation> nations) => Tally(nations, true);
    public int VotesAgainst(IEnumerable<Nation> nations) => Tally(nations, false);

    private int Tally(IEnumerable<Nation> nations, bool forProposal)
    {
        var all = nations as IReadOnlyList<Nation> ?? nations.ToList();
        return Votes.Where(v => v.Value == forProposal)
            .Sum(v => all.FirstOrDefault(n => n.Id == v.Key) is { } voter ? AssemblyService.VotingPower(voter, all) : 0);
    }
}

/// <summary>Active assembly policy effect.</summary>
public sealed class ActiveAssemblyPolicy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProposalId { get; set; } = "";
    public string TypeId { get; set; } = "";

    /// <summary>The country the law is about (country-scoped types), or empty for a global law that binds every country.</summary>
    public string TargetId { get; set; } = "";

    /// <summary>The product a production ban prohibits (empty for every other law).</summary>
    public string ProductId { get; set; } = "";
    public DateOnly ActivationDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
}

/// <summary>Central assembly service.</summary>
public static class AssemblyService
{
    public const int DefaultVotingDays = 30;

    /// <summary>Voting power from a country's own population (annexed territories are not counted here: see the overload with every nation).</summary>
    public static int VotingPower(Nation nation)
    {
        if (nation.IsEliminated) return 0;   // an annexed country has no voice of its own
        return Math.Max(1, (int)(nation.Population / 500000));
    }

    /// <summary>
    /// Voting power by the existing rule (one vote per 500,000 people, at least one), counting the people of the territories a country
    /// has annexed: their voice is their master's, once. An annexed territory itself has no vote.
    /// </summary>
    public static int VotingPower(Nation nation, IEnumerable<Nation> allNations)
    {
        if (nation.IsEliminated) return 0;
        long people = nation.Population + allNations.Where(t => t.IsEliminated && t.AnnexedById == nation.Id).Sum(t => t.Population);
        return Math.Max(1, (int)(people / 500000));
    }

    /// <summary>Estimate votes for/against a proposal.</summary>
    public static (int forVotes, int againstVotes) EstimateVotes(
        AssemblyProposal proposal, IEnumerable<Nation> nations, string proposerId)
    {
        int forV = 0, againstV = 0;
        nations = nations.ToList();   // (every nation, read once: territories count toward their master's voting power)
        var proposer = nations.FirstOrDefault(n => n.Id == proposerId);
        var target = nations.FirstOrDefault(n => n.Id == proposal.TargetId);

        foreach (var n in nations)
        {
            if (n.Id == proposal.TargetId || n.IsEliminated) continue; // the target doesn't vote, nor does an annexed country
            int power = VotingPower(n, nations);

            // Score based on relationships
            double score = 50; // base
            if (proposer is not null && n.Id != proposerId)
            {
                int rel = DiplomacyService.ToDisplayRating(n.RelationToPlayer);
                // If proposer is player, use relation to player
                // For AI vs AI, use neutral
                score += (rel - 50) * 0.5;
            }
            if (n.Id == proposerId) score = 100; // proposer votes for

            // Target relationship (AI dislikes harming friends)
            // Simplified: random-ish but deterministic
            int hash = (n.Id + proposal.Id).GetHashCode();
            score += (Math.Abs(hash) % 21) - 10;

            if (score >= 50) forV += power;
            else againstV += power;
        }
        return (forV, againstV);
    }

    /// <summary>Check if a policy type is active against a target (a country-scoped law about that very country).</summary>
    public static bool HasActivePolicy(IEnumerable<ActiveAssemblyPolicy> policies, string typeId, string targetId, DateOnly current)
    {
        return policies.Any(p => p.TypeId == typeId && p.TargetId == targetId && current < p.ExpirationDate);
    }

    /// <summary>
    /// The law of this type that binds <paramref name="nationId"/> now, if any: a global law (no target) binds every country; a law
    /// from an older save that names a target binds only that country.
    /// </summary>
    public static ActiveAssemblyPolicy? Binding(IEnumerable<ActiveAssemblyPolicy> policies, string typeId, string nationId, DateOnly current) =>
        policies.Where(p => p.TypeId == typeId && current < p.ExpirationDate && (p.TargetId == "" || p.TargetId == nationId))
                .OrderByDescending(p => p.ExpirationDate).FirstOrDefault();

    /// <summary>The global law of this type in force now, if any (the one that lasts longest).</summary>
    public static ActiveAssemblyPolicy? GlobalPolicy(IEnumerable<ActiveAssemblyPolicy> policies, string typeId, DateOnly current) =>
        policies.Where(p => p.TypeId == typeId && p.TargetId == "" && current < p.ExpirationDate)
                .OrderByDescending(p => p.ExpirationDate).FirstOrDefault();

    /// <summary>The production ban in force on a product (a trade-catalogue id), if any.</summary>
    public static ActiveAssemblyPolicy? ProductionBan(IEnumerable<ActiveAssemblyPolicy> policies, string productId, DateOnly current) =>
        policies.Where(p => p.TypeId == AssemblyProposalTypes.ProductionBan && p.ProductId == productId && current < p.ExpirationDate)
                .OrderByDescending(p => p.ExpirationDate).FirstOrDefault();

    /// <summary>The production ban that stops a product being made, looked up by the name the production system uses ("Wheat", "Iron"...).</summary>
    public static ActiveAssemblyPolicy? ProductionBanByName(IEnumerable<ActiveAssemblyPolicy> policies, string productName, DateOnly current)
    {
        var product = TradeCatalog.All.FirstOrDefault(p => p.Name.Equals(productName, StringComparison.OrdinalIgnoreCase) || p.Id.Equals(productName, StringComparison.OrdinalIgnoreCase));
        return product is null ? null : ProductionBan(policies, product.Id, current);
    }

    /// <summary>Why a proposal of this type cannot be put, and the target and product it would store (a global type stores no target; only a production ban stores a product).</summary>
    public static (string? Error, string TargetId, string ProductId) Check(AssemblyProposalType? type, string? targetId, string? productId,
        Func<string, bool> isLiveCountry, string proposerId)
    {
        if (type is null || !type.Offered) return ("Invalid proposal type.", "", "");
        string target = "", product = "";
        if (type.Scope == AssemblyScope.Country)
        {
            if (string.IsNullOrEmpty(targetId)) return ("Choose the country this proposal is about.", "", "");
            if (targetId == proposerId || !isLiveCountry(targetId)) return ("Invalid target.", "", "");
            target = targetId;
        }
        if (type.NeedsProduct)
        {
            if (string.IsNullOrEmpty(productId)) return ("Choose the product to prohibit.", "", "");
            if (TradeCatalog.Get(productId) is null) return ("Unknown product.", "", "");
            product = productId;
        }
        return (null, target, product);
    }

    /// <summary>Every product (trade-catalogue id) a production ban in force prohibits today.</summary>
    public static HashSet<string> BannedProducts(GameState state)
    {
        var ids = new HashSet<string>();
        foreach (var p in state.ActiveAssemblyPolicies)
            if (p.TypeId == AssemblyProposalTypes.ProductionBan && p.ProductId != "" && state.CurrentDate < p.ExpirationDate)
                ids.Add(p.ProductId);
        return ids;
    }

    /// <summary>Whether a product, named the way the production system names it ("Wheat", "Iron") or by catalogue id, is in a set of banned ids.</summary>
    public static bool IsBanned(HashSet<string> bannedIds, string productNameOrId)
    {
        if (bannedIds.Count == 0) return false;
        if (bannedIds.Contains(productNameOrId)) return true;
        var product = TradeCatalog.All.FirstOrDefault(p => p.Name.Equals(productNameOrId, StringComparison.OrdinalIgnoreCase));
        return product is not null && bannedIds.Contains(product.Id);
    }

    /// <summary>Why a product cannot be produced right now: the Assembly has prohibited it (null = it can).</summary>
    public static string? ProductionBlock(GameState state, string productNameOrId)
    {
        var ban = ProductionBanByName(state.ActiveAssemblyPolicies, productNameOrId, state.CurrentDate);
        if (ban is null) return null;
        string name = TradeCatalog.Get(ban.ProductId)?.Name ?? productNameOrId;
        return $"The Assembly has prohibited producing {name} until {ban.ExpirationDate:dd-MM-yyyy}.";
    }

    /// <summary>Why military equipment cannot be bought from or sold to <paramref name="partnerId"/> right now (null = it can).</summary>
    public static string? WeaponSalesBlock(GameState state, string partnerId)
    {
        var ban = Binding(state.ActiveAssemblyPolicies, AssemblyProposalTypes.WeaponSalesBan, partnerId, state.CurrentDate)
                  ?? Binding(state.ActiveAssemblyPolicies, "weapon_embargo", partnerId, state.CurrentDate);
        return ban is null ? null : $"Weapon sales are prohibited by the Assembly until {ban.ExpirationDate:dd-MM-yyyy}.";
    }

    /// <summary>A proposal or law in words: "Prohibition on War", "Prohibition on Production: Wheat", "Military Training Restriction against France".</summary>
    public static string Describe(GameState state, string typeId, string targetId, string productId)
    {
        string name = AssemblyProposalTypes.Get(typeId)?.Name ?? typeId;
        if (!string.IsNullOrEmpty(productId)) name += $": {TradeCatalog.Get(productId)?.Name ?? productId}";
        if (!string.IsNullOrEmpty(targetId)) name += $" against {state.AllNations().FirstOrDefault(n => n.Id == targetId)?.Name ?? "unknown"}";
        return name;
    }

    /// <summary>Who a proposal or law is "against" in the Movement Report: the target country, or the Assembly itself for a global one.</summary>
    public static (string Id, string Name) Party(GameState state, string targetId) =>
        string.IsNullOrEmpty(targetId) ? ("assembly", "the Assembly") : (targetId, Services.TreatyService.NameOf(state, targetId));
}
