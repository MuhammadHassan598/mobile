using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The AI world: AI countries are independent. They feed themselves and rebuild their armies, hold opinions of each
/// other, form defensive alliances (as many as they can find friends for), declare wars on each other (and on the player),
/// call their allies in, send armies marching through the normal march/battle system, and make peace.
///
/// A ruler goes to war from motives, not from a rule about who is stronger: land it covets, a grudge, fear of a neighbour's
/// power, duty to an ally already at war — weighed against its chances (<see cref="Motives"/>, <see cref="Confidence"/>).
/// A weaker country can attack a stronger one; it is just rarer, because the odds weigh on the decision. What a war ends in
/// follows from what it was fought for and how it went (see <see cref="VictoryService.ResolveForAi"/>).
///
/// Everything here runs on its own random stream (so the rest of the simulation is unaffected) and wakes up on
/// <see cref="GameState.AiWorldStartDate"/>. Every event is written to the Movement Report.
/// </summary>
public static class AiWorldService
{
    // ---------------- Relations between AI countries ----------------

    public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    /// <summary>A hash that is the same on every run (string.GetHashCode is randomised per process).</summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in text) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    public static double Distance(Nation a, Nation b) =>
        Math.Sqrt(Math.Pow(a.MapX - b.MapX, 2) + Math.Pow(a.MapY - b.MapY, 2));

    /// <summary>
    /// How two AI countries regard each other at the start, -100..100: shared faith warms, neighbours quarrel,
    /// and a fixed per-pair quirk makes some pairs friends and some rivals. Symmetric and identical on every run.
    /// </summary>
    public static double BaseRelation(Nation a, Nation b)
    {
        bool sameFaith = !string.IsNullOrEmpty(a.Religion) && a.Religion.Equals(b.Religion, StringComparison.OrdinalIgnoreCase);
        double r = sameFaith ? 20 : -5;
        r += StableHash(PairKey(a.Id, b.Id)) % 41 - 20;
        if (Distance(a, b) < Balance.AiNeighbourDistance) r -= 10;
        return Math.Clamp(r, -90, 90);
    }

    /// <summary>The current regard between two AI countries: the starting regard plus what has happened since.</summary>
    public static double Relation(GameState state, Nation a, Nation b)
    {
        state.AiRelations.TryGetValue(PairKey(a.Id, b.Id), out double drift);
        return Math.Clamp(BaseRelation(a, b) + drift, -100, 100);
    }

    public static void AddRelation(GameState state, Nation a, Nation b, double delta)
    {
        string key = PairKey(a.Id, b.Id);
        state.AiRelations.TryGetValue(key, out double drift);
        state.AiRelations[key] = Math.Clamp(drift + delta, -200, 200);
    }

    /// <summary>Sets the regard between two AI countries to exactly <paramref name="target"/> (-100..100), whatever it was before.</summary>
    public static void SetRelation(GameState state, Nation a, Nation b, double target) =>
        state.AiRelations[PairKey(a.Id, b.Id)] = Math.Clamp(Math.Clamp(target, -100, 100) - BaseRelation(a, b), -200, 200);

    private static void DecayRelations(GameState state)
    {
        foreach (var key in state.AiRelations.Keys.ToList())
        {
            double d = state.AiRelations[key];
            d = d > 0 ? Math.Max(0, d - Balance.AiRelationDecayPerDay) : Math.Min(0, d + Balance.AiRelationDecayPerDay);
            if (d == 0) state.AiRelations.Remove(key); else state.AiRelations[key] = d;
        }
    }

    // ---------------- Who is at war / allied with whom ----------------

    /// <summary>Whether two AI countries are at war with each other.</summary>
    public static bool AtWar(GameState state, string a, string b) => state.Wars.Any(w => w.Links(a, b));

    /// <summary>
    /// Whether an army of <paramref name="attacker"/> may fight <paramref name="defender"/>: the two are at war (with the player,
    /// or with each other), or the defender is at war with the player and the attacker is helping the player's cause.
    /// </summary>
    public static bool InWar(GameState state, Nation attacker, Nation defender)
    {
        if (attacker.IsPlayer) return defender.AtWarWithPlayer;
        if (defender.IsPlayer) return attacker.AtWarWithPlayer;
        return AtWar(state, attacker.Id, defender.Id) || defender.AtWarWithPlayer;
    }

    public static bool Allied(GameState state, string a, string b) =>
        TreatyService.Has(state, TreatyType.DefensiveAlliance, a, b);

    /// <summary>Every active alliance at a glance: country id → its allies (the player included where allied). Built once per decision.</summary>
    private static Dictionary<string, List<Nation>> AllianceMap(GameState state)
    {
        var alive = state.AllNations().Where(n => !n.IsEliminated).ToDictionary(n => n.Id);
        var map = new Dictionary<string, List<Nation>>();
        void Link(Nation x, Nation y)
        {
            if (!map.TryGetValue(x.Id, out var list)) map[x.Id] = list = new List<Nation>();
            list.Add(y);
        }
        foreach (var t in state.Treaties)
        {
            if (t.Type != TreatyType.DefensiveAlliance || !t.IsActiveOn(state.CurrentDate)) continue;
            if (!alive.TryGetValue(t.NationAId, out var a) || !alive.TryGetValue(t.NationBId, out var b)) continue;
            Link(a, b);
            Link(b, a);
        }
        return map;
    }

    /// <summary>The AI countries allied with <paramref name="id"/> in an <see cref="AllianceMap"/> (the player never joins an AI war).</summary>
    private static List<Nation> AiAlliesIn(Dictionary<string, List<Nation>> map, string id) =>
        map.TryGetValue(id, out var list) ? list.Where(a => !a.IsPlayer).ToList() : new List<Nation>();

    /// <summary>Countries allied with this one (the player included only if asked).</summary>
    public static List<Nation> AlliesOf(GameState state, Nation n, bool includePlayer = false) =>
        state.AllNations()
            .Where(m => m.Id != n.Id && !m.IsEliminated && (includePlayer || !m.IsPlayer) && Allied(state, n.Id, m.Id))
            .ToList();

    /// <summary>Countries this one is at war with (the player included).</summary>
    public static List<Nation> EnemiesOf(GameState state, Nation n)
    {
        var enemies = state.OtherNations
            .Where(m => !m.IsEliminated && m.Id != n.Id && AtWar(state, n.Id, m.Id))
            .ToList();
        if (n.AtWarWithPlayer && !n.IsPlayer) enemies.Add(state.PlayerNation);
        return enemies;
    }

    private static Nation? Find(GameState state, string id) => state.AllNations().FirstOrDefault(n => n.Id == id);

    private static int OwnWars(GameState state, Nation n) =>
        state.Wars.Count(w => w.AggressorId == n.Id) + (n.AtWarWithPlayer ? 1 : 0);

    // ---------------- The daily tick ----------------

    /// <summary>Once a day: every country that is due thinks; wars advance. Does nothing before the AI world's start date.</summary>
    public static void Advance(GameState state, Random rng)
    {
        if (state.CurrentDate < state.AiWorldStartDate) return;

        DecayRelations(state);
        int day = state.CurrentDate.DayNumber;
        var nations = state.OtherNations;
        for (int i = 0; i < nations.Count; i++)
        {
            var n = nations[i];
            if (n.IsEliminated) continue;
            if ((day + i) % Balance.AiThinkIntervalDays != 0) continue;
            Think(state, n, rng);
        }
        AdvanceWars(state, rng);
    }

    private static void Think(GameState state, Nation n, Random rng)
    {
        Economy(n);
        TryAlliance(state, n, rng);
        TryWar(state, n, rng);
    }

    /// <summary>An AI country feeds its growing people and rebuilds its army (paying from its treasury).</summary>
    private static void Economy(Nation n)
    {
        ConsumptionService.EnsureSupply(n, Balance.AiSupplyCoverage);

        if (n.BaseSoldiers <= 0) return;
        long basePopulation = n.BasePopulation > 0 ? n.BasePopulation : n.HistoricalPopulation;
        double growth = basePopulation > 0 ? (double)n.Population / basePopulation : 1.0;
        int target = (int)(n.BaseSoldiers * Math.Max(1.0, growth));
        int deficit = target - n.Soldiers;
        if (deficit <= 0) return;

        int add = Math.Min(deficit, Math.Max(20, (int)(target * Balance.AiRecruitPerThink)));
        double cost = add * UnitCatalog.Get(UnitType.Musketeer).GoldCost;
        if (!n.CanPay(cost)) return;
        n.PayGold(cost);
        ArmyHelper.MergeStacks(n, UnitCatalog.SeedArmy(add));
    }

    // ---------------- Alliances ----------------

    /// <summary>
    /// A country looks for a friend to ally with: the warmest country within reach that it is not already allied with and not
    /// at war with. There is no limit on how many alliances a country can hold.
    /// </summary>
    private static void TryAlliance(GameState state, Nation n, Random rng)
    {
        if (rng.NextDouble() >= Balance.AiAllianceChance) return;

        Nation? best = null;
        double bestRelation = Balance.AiAllianceMinRelation;
        foreach (var m in state.OtherNations)
        {
            if (m.Id == n.Id || m.IsEliminated) continue;
            if (AtWar(state, n.Id, m.Id) || Allied(state, n.Id, m.Id)) continue;
            if (Distance(n, m) > Balance.AiWarRange * 1.2) continue;   // alliances stay regional
            double r = Relation(state, n, m);
            if (r >= bestRelation) { best = m; bestRelation = r; }
        }
        if (best is null) return;

        TreatyService.Add(state, TreatyType.DefensiveAlliance, n, best);
        AddRelation(state, n, best, Balance.AiAllianceRelationGain);
        state.LogMovement(MovementKind.Treaty, MovementStatus.Completed, n, best,
            $"🛡 {n.Name} and {best.Name} formed a defensive alliance.");
    }

    // ---------------- Motives: why a ruler goes to war ----------------

    /// <summary>
    /// What draws one ruler toward a war with another. Each part runs from 0 to 1 (duty is 0 or 1):
    /// <list type="bullet">
    /// <item><b>Land</b> — the prize: how near the country lies, times how big it is next to the ruler's own (a populous neighbour is worth more than a hamlet).</item>
    /// <item><b>Grudge</b> — how much the ruler dislikes it (its regard below zero).</item>
    /// <item><b>Fear</b> — the menace: a near neighbour with an army to match the ruler's, that the ruler is not friendly with.</item>
    /// <item><b>Duty</b> — it is at war with one of the ruler's allies.</item>
    /// </list>
    /// Rulers weigh them by <see cref="Balance.AiGrudgeWeight"/> and friends: wars are mostly fought between rivals, with
    /// land and fear as the lesser pushes. The heaviest of land, grudge and fear is what a war would be fought for (<see cref="Aim"/>).
    /// </summary>
    public readonly record struct WarMotives(double Land, double Grudge, double Fear, double Duty)
    {
        private double WeightedLand => Balance.AiLandWeight * Land;
        private double WeightedGrudge => Balance.AiGrudgeWeight * Grudge;
        private double WeightedFear => Balance.AiFearWeight * Fear;

        /// <summary>How much the ruler wants this war, before weighing its chances.</summary>
        public double Desire => WeightedLand + WeightedGrudge + WeightedFear + Balance.AiDutyWeight * Duty;

        /// <summary>What the war would be for: land → conquest, grudge → humiliation, fear (or no real motive) → containment.</summary>
        public WarAim Aim =>
            Land > 0 && WeightedLand >= WeightedGrudge && WeightedLand >= WeightedFear ? WarAim.Conquest
            : Grudge > 0 && WeightedGrudge >= WeightedFear ? WarAim.Humiliation
            : WarAim.Containment;
    }

    /// <summary>How <paramref name="from"/> regards <paramref name="to"/> (-100..100); the player's side of it is the player-relation score.</summary>
    private static double Regard(GameState state, Nation from, Nation to) =>
        to.IsPlayer ? from.RelationToPlayer : from.IsPlayer ? to.RelationToPlayer : Relation(state, from, to);

    /// <summary>1 for a country next door, falling to 0 at the edge of war range.</summary>
    private static double Proximity(Nation a, Nation b) => Math.Clamp(1 - Distance(a, b) / Balance.AiWarRange, 0, 1);

    /// <summary>The motives of ruler <paramref name="n"/> toward country <paramref name="m"/>. <paramref name="allies"/> are n's AI allies (looked up when omitted).</summary>
    public static WarMotives Motives(GameState state, Nation n, Nation m, IReadOnlyCollection<Nation>? allies = null)
    {
        double reach = Proximity(n, m);
        double regard = Regard(state, n, m);
        double myPower = Math.Max(1, ArmyHelper.ArmyPower(n.Units));
        double theirPower = ArmyHelper.ArmyPower(m.Units);

        double land = reach * Math.Min(1.0, (double)m.Population / Math.Max(1, n.Population));
        double grudge = Math.Max(0, -regard) / 100.0;
        double fear = reach * Math.Min(1.0, theirPower / myPower) * (1 - Math.Max(0, regard) / 100.0);
        allies ??= AlliesOf(state, n);
        double duty = allies.Any(a => m.IsPlayer ? a.AtWarWithPlayer : AtWar(state, a.Id, m.Id)) ? 1 : 0;
        return new WarMotives(land, grudge, fear, duty);
    }

    /// <summary>
    /// How a war would go if the armies met: the force <paramref name="n"/> would march with (its own army, and the share of
    /// its allies' armies that would join), against the garrison <paramref name="m"/> would field (its own army and its allies'),
    /// by the very rule a battle is decided on. Above 1 the attackers would win the field.
    /// </summary>
    public static double Edge(Nation n, IEnumerable<Nation> allies, Nation m, IEnumerable<Nation> targetAllies)
    {
        var helpers = allies.Where(a => a.Id != m.Id).ToList();
        double mine = ArmyHelper.ArmyPower(n.Units)
            + helpers.Sum(a => ArmyHelper.ArmyPower(a.Units)) * Balance.AiAttackerAllyJoinChance;
        double theirs = ArmyHelper.ArmyPower(m.Units)
            + targetAllies.Where(a => a.Id != n.Id).Sum(a => ArmyHelper.ArmyPower(a.Units)) * Balance.AiDefenderAllyJoinChance;
        double attack = mine * Balance.AiInvasionCommitFraction * n.BattleStrengthMult;
        double defence = theirs * Balance.GarrisonFraction * Balance.HomeAdvantageMult;
        double edge = attack / Math.Max(1, defence);

        // Against the player the war is fought differently: an AI country only presses an invasion when its army outnumbers the
        // player's by the invasion ratio, so that is what its chances rest on.
        if (m.IsPlayer)
        {
            double soldiers = n.Soldiers + helpers.Sum(a => a.Soldiers) * Balance.AiAttackerAllyJoinChance;
            edge = Math.Min(edge, soldiers / (Balance.AiInvasionArmyRatio * Math.Max(1, m.Soldiers)));
        }
        return edge;
    }

    /// <summary>
    /// A ruler's confidence in a war, 0..1, from its <see cref="Edge"/>: near 1 when it would crush the enemy, 0.5 at even odds,
    /// small (but never nil) when it would be badly outmatched. A weaker country is held back by this, not barred.
    /// </summary>
    public static double Confidence(double edge)
    {
        double e = Math.Pow(Math.Max(0, edge), Balance.AiOddsSharpness);
        return e / (1 + e);
    }

    /// <summary>How much ruler <paramref name="n"/> wants a war with <paramref name="m"/> right now: its motives times its confidence.</summary>
    public static double Appetite(GameState state, Nation n, Nation m)
    {
        var map = AllianceMap(state);
        var mine = AiAlliesIn(map, n.Id);
        return Appetite(state, n, m, mine, AiAlliesIn(map, m.Id));
    }

    private static double Appetite(GameState state, Nation n, Nation m, List<Nation> mine, List<Nation> theirs) =>
        Motives(state, n, m, mine).Desire * Confidence(Edge(n, mine, m, theirs));

    /// <summary>What a war fought by <paramref name="winner"/> against <paramref name="loser"/> is for: the aim fixed when it began, else the winner's motives now.</summary>
    public static WarAim AimOf(GameState state, Nation winner, Nation loser, WarRecord? war) =>
        war is not null && war.AggressorId == winner.Id && war.Aim is { } aim ? aim : Motives(state, winner, loser).Aim;

    private static string Reason(WarAim aim) => aim switch
    {
        WarAim.Conquest => "it covets its lands",
        WarAim.Humiliation => "to humble a hated rival",
        _ => "to break a menacing neighbour",
    };

    // ---------------- Wars ----------------

    /// <summary>
    /// A ruler weighs every country within reach that it may legally attack — whatever its strength — and is drawn to the one it
    /// wants most (motives × confidence). Whether it acts this time is a matter of pace. No target is ruled out for being the
    /// stronger side or for being liked; only treaties (an alliance, a pact, a guarantee) and a war already on forbid it.
    /// </summary>
    private static void TryWar(GameState state, Nation n, Random rng)
    {
        if (OwnWars(state, n) >= Balance.AiMaxWars) return;

        var map = AllianceMap(state);
        var mine = AiAlliesIn(map, n.Id);
        Nation? best = null;
        double bestAppetite = 0;
        foreach (var m in state.AllNations())
        {
            if (m.Id == n.Id || m.IsEliminated) continue;
            if (m.IsPlayer ? n.AtWarWithPlayer : AtWar(state, n.Id, m.Id)) continue;
            if (Distance(n, m) > Balance.AiWarRange) continue;
            if (TreatyService.ForbidsAttack(state, n.Id, m.Id) is not null) continue;

            double appetite = Appetite(state, n, m, mine, AiAlliesIn(map, m.Id));
            if (appetite > bestAppetite) { best = m; bestAppetite = appetite; }
        }
        if (best is null || rng.NextDouble() >= Balance.AiWarPressure * bestAppetite) return;
        DeclareWar(state, n, best, rng);
    }

    /// <summary>
    /// An AI country goes to war. Allies of the attacked country usually join it; allies of the attacker sometimes do.
    /// A war on the player uses the player-war rules (relations, warnings, the player's allies marching).
    /// </summary>
    public static void DeclareWar(GameState state, Nation aggressor, Nation target, Random rng)
    {
        // What the war is for is judged before the war itself sours anything.
        var aim = Motives(state, aggressor, target).Aim;

        if (target.IsPlayer)
        {
            DeclareWarOnPlayer(state, aggressor, Reason(aim));
            foreach (var ally in AlliesOf(state, aggressor))
            {
                if (ally.AtWarWithPlayer || TreatyService.ForbidsAttack(state, ally.Id, target.Id) is not null) continue;
                if (rng.NextDouble() >= Balance.AiAttackerAllyJoinChance) continue;
                state.Log($"{ally.Name} joins {aggressor.Name} against {target.Name}.");
                DeclareWarOnPlayer(state, ally, $"to stand by {aggressor.Name}");
            }
            return;
        }

        state.Wars.Add(new WarRecord { AggressorId = aggressor.Id, DefenderId = target.Id, StartDate = state.CurrentDate, Aim = aim });
        AddRelation(state, aggressor, target, Balance.AiWarRelationHit);
        state.LogMovement(MovementKind.War, MovementStatus.Completed, aggressor, target,
            $"⚔ {aggressor.Name} declared war on {target.Name} — {Reason(aim)}!");
        if (Allied(state, state.PlayerNation.Id, target.Id))
            state.ActiveWarnings.Add($"⚠ {aggressor.Name} has declared war on your ally {target.Name}!");

        foreach (var ally in AlliesOf(state, target))
            if (ally.Id != aggressor.Id) TryJoin(state, ally, target, aggressor, defending: true, rng);
        foreach (var ally in AlliesOf(state, aggressor))
            if (ally.Id != target.Id) TryJoin(state, ally, aggressor, target, defending: false, rng);
    }

    /// <summary>An ally joins its friend's war against <paramref name="enemy"/> — if it can, and if it honours the alliance.</summary>
    private static void TryJoin(GameState state, Nation ally, Nation friend, Nation enemy, bool defending, Random rng)
    {
        if (ally.IsEliminated || AtWar(state, ally.Id, enemy.Id) || Allied(state, ally.Id, enemy.Id)) return;
        if (Relation(state, ally, friend) < Balance.AiAllyHonorMinRelation) return;
        double chance = defending ? Balance.AiDefenderAllyJoinChance : Balance.AiAttackerAllyJoinChance;
        if (rng.NextDouble() >= chance) return;

        // The war record keeps who started it: an attacker's ally attacks (for whatever it wants of the enemy), a defender's ally
        // is attacked by the aggressor (whose aim against the ally is judged when it wins).
        state.Wars.Add(defending
            ? new WarRecord { AggressorId = enemy.Id, DefenderId = ally.Id, StartDate = state.CurrentDate }
            : new WarRecord { AggressorId = ally.Id, DefenderId = enemy.Id, StartDate = state.CurrentDate, Aim = Motives(state, ally, enemy).Aim });
        AddRelation(state, ally, enemy, Balance.AiWarRelationHit / 2);
        state.LogMovement(MovementKind.War, MovementStatus.Completed, ally, enemy,
            $"🛡 {ally.Name} joins the war against {enemy.Name} to stand by {friend.Name}.");
    }

    /// <summary>An AI country declares war on the player: the same steps whether it decided by anger or by opportunity.</summary>
    public static void DeclareWarOnPlayer(GameState state, Nation aggressor, string? reason = null)
    {
        var player = state.PlayerNation;
        aggressor.AtWarWithPlayer = true;
        aggressor.HasTradePactWithPlayer = false;
        aggressor.RelationToPlayer = -100;
        TreatyService.OnWar(state, player, aggressor);
        state.LogMovement(MovementKind.War, MovementStatus.Completed, aggressor, player,
            reason is null ? $"{aggressor.Name} declared war on {player.Name}!" : $"{aggressor.Name} declared war on {player.Name} — {reason}!");
        state.ActiveWarnings.Add($"⚠ {aggressor.Name} has DECLARED WAR on you!");
        TreatyService.AllianceDefence(state, aggressor, player);
    }

    // ---------------- Wars in progress ----------------

    private static void AdvanceWars(GameState state, Random rng)
    {
        int day = state.CurrentDate.DayNumber;
        foreach (var war in state.Wars.ToList())
        {
            var a = Find(state, war.AggressorId);
            var b = Find(state, war.DefenderId);
            if (a is null || b is null || a.IsEliminated || b.IsEliminated) { state.Wars.Remove(war); continue; }

            int offset = StableHash(PairKey(war.AggressorId, war.DefenderId));
            int age = day - war.StartDate.DayNumber;

            // Peace: worn out, too long, or simply tired of it.
            if ((day + offset) % Balance.AiPeaceCheckDays == 0 && age >= Balance.AiPeaceCheckDays)
            {
                bool exhausted = a.Soldiers < Balance.AiMinWarForce || b.Soldiers < Balance.AiMinWarForce;
                if (exhausted || age >= Balance.AiMaxWarDays || rng.NextDouble() < Balance.AiPeaceChance)
                {
                    MakePeace(state, war, a, b);
                    continue;
                }
            }

            // Fighting: each side may send an army against the other.
            if ((day + offset) % Balance.AiInvasionIntervalDays == 0)
            {
                TryMarch(state, a, b, rng);
                TryMarch(state, b, a, rng);
            }
        }
    }

    private static void TryMarch(GameState state, Nation from, Nation to, Random rng)
    {
        if (TreatyService.HasMarchOn(state, from.Id, to.Id)) return;
        if (rng.NextDouble() >= Balance.AiInvasionChance) return;
        int commit = (int)(from.Soldiers * Balance.AiInvasionCommitFraction);
        if (commit < Balance.MinInvasionForce) return;

        var march = TreatyService.LaunchMarch(state, from, to, commit);
        if (march is null) return;
        state.LogMovement(MovementKind.March, MovementStatus.UnderWay, from, to,
            $"⚔ {from.Name} sends {commit:N0} soldiers against {to.Name} ({march.DaysLeft} days).");
    }

    /// <summary>Ends a war between two AI countries (after exhaustion, a long stalemate, or a winner's spoils).</summary>
    public static void MakePeace(GameState state, WarRecord war, Nation a, Nation b)
    {
        state.Wars.Remove(war);
        SetRelation(state, a, b, Balance.AiWarPeaceRelation);
        state.LogMovement(MovementKind.War, MovementStatus.Completed, a, b, $"🕊 {a.Name} and {b.Name} made peace.");
    }

    /// <summary>Ends every war between two AI countries (used when a victory settles the war with spoils or a white peace).</summary>
    public static void EndWarBetween(GameState state, Nation a, Nation b)
    {
        foreach (var war in state.Wars.Where(w => w.Links(a.Id, b.Id)).ToList())
            MakePeace(state, war, a, b);
    }
}
