namespace EmpireSim.Core.Models;

/// <summary>Assembly proposal type definition.</summary>
public sealed record AssemblyProposalType(
    string Id,
    string Name,
    string Icon,
    string Description,
    string EffectDescription);

/// <summary>Registry of proposal types.</summary>
public static class AssemblyProposalTypes
{
    public static readonly IReadOnlyList<AssemblyProposalType> All = new List<AssemblyProposalType>
    {
        new("military_restriction", "Military Training Restriction", "🎖️",
            "Restrict military training in the target country.",
            "Target's army growth rate reduced by 50% during the period."),
        new("weapon_sales_ban", "Prohibition on Weapon Sales", "🚫",
            "Prohibit weapon sales to the target country.",
            "No country may sell military equipment to the target."),
        new("production_ban", "Prohibition on Production", "🏭",
            "Restrict production in the target country.",
            "Target's production output reduced by 50%."),
        new("annexation_ban", "Prohibition of Annexation", "🛡️",
            "Protect the target from annexation.",
            "Target cannot be annexed during the period."),
        new("weapon_embargo", "Weapon Sales Embargo", "⛔",
            "Embargo on military equipment exports to target.",
            "Military equipment exports to target are blocked."),
    };

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
    public string TargetId { get; set; } = "";
    public int EffectDurationDays { get; set; }
    public DateOnly CreatedDate { get; set; }
    public DateOnly VotingDeadline { get; set; }
    public ProposalStatus Status { get; set; }
    public Dictionary<string, bool> Votes { get; set; } = new(); // countryId -> for(true)/against(false)
    public DateOnly? ActiveUntil { get; set; }

    public int VotesFor(IEnumerable<Nation> nations) =>
        Votes.Where(v => v.Value).Sum(v => nations.FirstOrDefault(n => n.Id == v.Key)?.VotingPower ?? 0);
    public int VotesAgainst(IEnumerable<Nation> nations) =>
        Votes.Where(v => !v.Value).Sum(v => nations.FirstOrDefault(n => n.Id == v.Key)?.VotingPower ?? 0);
}

/// <summary>Active assembly policy effect.</summary>
public sealed class ActiveAssemblyPolicy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProposalId { get; set; } = "";
    public string TypeId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public DateOnly ActivationDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
}

/// <summary>Central assembly service.</summary>
public static class AssemblyService
{
    public const int DefaultVotingDays = 30;

    /// <summary>Voting power from population.</summary>
    public static int VotingPower(Nation nation)
    {
        return Math.Max(1, (int)(nation.Population / 500000));
    }

    /// <summary>Estimate votes for/against a proposal.</summary>
    public static (int forVotes, int againstVotes) EstimateVotes(
        AssemblyProposal proposal, IEnumerable<Nation> nations, string proposerId)
    {
        int forV = 0, againstV = 0;
        var proposer = nations.FirstOrDefault(n => n.Id == proposerId);
        var target = nations.FirstOrDefault(n => n.Id == proposal.TargetId);

        foreach (var n in nations)
        {
            if (n.Id == proposal.TargetId) continue; // target doesn't vote
            int power = VotingPower(n);

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

    /// <summary>Check if a policy type is active against a target.</summary>
    public static bool HasActivePolicy(IEnumerable<ActiveAssemblyPolicy> policies, string typeId, string targetId, DateOnly current)
    {
        return policies.Any(p => p.TypeId == typeId && p.TargetId == targetId && current < p.ExpirationDate);
    }
}
