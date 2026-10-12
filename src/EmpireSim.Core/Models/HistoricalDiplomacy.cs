using EmpireSim.Core.Services;

namespace EmpireSim.Core.Models;

/// <summary>
/// The diplomatic web of the year 1600, used to start a campaign: whichever country the player chooses begins with the
/// embassies, friendships and quarrels that country really had — the Ottomans with an embassy in friendly France and Venice and
/// bad blood with the Habsburgs, England with the Dutch and Morocco against Spain, Korea under Ming protection and at odds with
/// Japan. Pairs history says nothing about start neutral (a little warmer between countries of one faith).
///
/// Ratings are on the 0-100 scale the Diplomacy screen shows: 80+ very close, 60s friendly, 50s warm, 45-50 neutral,
/// 40s cool, 30s tense, 20s hostile, under 20 bitter enemies.
/// </summary>
public static class HistoricalDiplomacy
{
    /// <summary>One known relationship between two countries (the same from either side).</summary>
    /// <param name="Rating">How well the two regard each other, 0-100.</param>
    /// <param name="Embassy">Each keeps a resident ambassador at the other's court.</param>
    /// <param name="Why">The historical reason, for maintainers (and one day the screen).</param>
    public sealed record Tie(string A, string B, int Rating, bool Embassy, string Why)
    {
        public bool Involves(string id) => A == id || B == id;
        public string Other(string id) => A == id ? B : A;
    }

    public const int NeutralRating = 50;
    public const int SameFaithBonus = 6;
    public const int OtherFaithPenalty = 2;

    /// <summary>A country with fewer embassies than this (from the table) is given its nearest acceptable neighbours' as well.</summary>
    public const int MinEmbassies = 2;

    public static readonly IReadOnlyList<Tie> Ties = new List<Tie>
    {
        // ---- The Ottoman Empire ----
        new("ottoman", "crimea", 78, true, "Crimea is an Ottoman vassal khanate"),
        new("ottoman", "france", 72, true, "Franco-Ottoman friendship since 1536; capitulations renewed in 1597"),
        new("ottoman", "england", 62, true, "Levant Company trade; Elizabeth's envoy at the Porte"),
        new("ottoman", "venice", 55, true, "the Venetian bailo at Constantinople; the Porte's main western trade partner"),
        new("ottoman", "dutch", 54, false, "a common enemy in the Habsburgs"),
        new("ottoman", "mughal", 54, false, "courtesies between the two great Muslim courts"),
        new("ottoman", "austria", 20, false, "the Long Turkish War (1593-1606)"),
        new("ottoman", "hre", 24, false, "the Long Turkish War: the Empire pays for the frontier"),
        new("ottoman", "croatia", 22, false, "the Habsburg military frontier"),
        new("ottoman", "spain", 26, false, "Mediterranean rivalry after Lepanto"),
        new("ottoman", "papal", 24, false, "the Pope preaches crusade against the Turk"),
        new("ottoman", "persia", 26, false, "Safavid rivalry; war is coming"),
        new("ottoman", "poland", 40, false, "Tatar raids and border disputes"),
        new("ottoman", "russia", 36, false, "the clash over Astrakhan and the steppe"),
        new("ottoman", "ethiopia", 30, false, "the Red Sea coast: the Ottoman province of Habesh"),
        new("ottoman", "morocco", 42, false, "the Saadi sultans refuse Ottoman overlordship"),
        new("ottoman", "italy", 34, false, "Spanish Italy is Habsburg ground"),

        // ---- The Iberian Union ----
        new("spain", "austria", 78, true, "Habsburg cousins"),
        new("spain", "hre", 72, true, "Habsburg cousins"),
        new("spain", "papal", 72, true, "the Catholic King and the Pope"),
        new("spain", "genoa", 76, true, "Genoese bankers finance the Crown"),
        new("spain", "italy", 66, true, "Spanish Naples and Milan"),
        new("spain", "kongo", 62, true, "Portuguese ties with the Christian kingdom of Kongo"),
        new("spain", "poland", 60, true, "Habsburg marriages with the Vasa kings"),
        new("spain", "croatia", 62, false, "the Catholic frontier against the Turk"),
        new("spain", "england", 18, false, "the Anglo-Spanish War (1585-1604)"),
        new("spain", "dutch", 15, false, "the Eighty Years' War against the Dutch revolt"),
        new("spain", "france", 34, false, "rivalry; the Peace of Vervins is only two years old"),
        new("spain", "scotland", 42, false, "Catholic sympathies against a Protestant crown"),
        new("spain", "persia", 52, false, "Shah Abbas seeks allies against the Turk"),
        new("spain", "morocco", 30, false, "Barbary raids and Portuguese forts on the Moroccan coast"),
        new("spain", "venice", 46, false, "Venice stays out of Habsburg quarrels"),
        new("spain", "malaysia", 26, false, "Johor fights Portuguese Malacca"),
        new("spain", "uae", 28, false, "Portuguese Hormuz holds the Gulf"),
        new("spain", "ethiopia", 58, false, "Portuguese soldiers and Jesuits aid the Negus"),
        new("spain", "cambodia", 55, false, "Spanish adventurers at the Khmer court"),

        // ---- France ----
        new("france", "scotland", 74, true, "the Auld Alliance"),
        new("france", "dutch", 66, true, "Henry IV backs the Dutch revolt"),
        new("france", "venice", 60, true, "old friends against Habsburg power"),
        new("france", "england", 55, true, "the Treaty of Greenwich (1596) against Spain"),
        new("france", "papal", 55, true, "Henry IV's absolution (1595) healed the breach"),
        new("france", "sweden", 54, false, "Protestant friends of the crown"),
        new("france", "austria", 32, false, "Habsburg encirclement"),
        new("france", "hre", 32, false, "Habsburg encirclement"),
        new("france", "genoa", 42, false, "Genoa is Spain's banker"),

        // ---- England, the Dutch, Scotland and the north ----
        new("england", "dutch", 68, true, "the Treaty of Nonsuch (1585): allies against Spain"),
        new("england", "morocco", 70, true, "Elizabeth and al-Mansur against Spain"),
        new("england", "russia", 62, true, "the Muscovy Company's trade"),
        new("england", "venice", 54, true, "a Venetian ambassador in London"),
        new("england", "sweden", 56, false, "Protestant courtesy"),
        new("england", "persia", 54, false, "the Sherley brothers' mission"),
        new("england", "scotland", 46, false, "wary: James VI is Elizabeth's heir"),
        new("england", "papal", 20, false, "Elizabeth is excommunicated"),
        new("dutch", "sweden", 62, false, "Protestant trade partners"),
        new("dutch", "morocco", 56, false, "Atlantic trade"),
        new("dutch", "scotland", 55, false, "Protestant kinship"),
        new("dutch", "papal", 20, false, "heretic rebels in Catholic eyes"),
        new("dutch", "austria", 24, false, "Habsburg rule is what they fight"),
        new("dutch", "hre", 40, false, "Calvinists on the Empire's border"),
        new("denmark", "scotland", 66, true, "James VI married Anne of Denmark"),
        new("denmark", "sweden", 28, false, "Baltic rivalry"),
        new("sweden", "poland", 16, false, "the war over Livonia (1600)"),
        new("sweden", "russia", 32, false, "Livonian and Ingrian rivalry"),
        new("scotland", "papal", 32, false, "Catholic recusants and a Protestant crown"),

        // ---- Central Europe, Italy and the Papacy ----
        new("hre", "austria", 85, true, "one Emperor: Rudolf II of Habsburg"),
        new("croatia", "austria", 82, true, "the Habsburg military border"),
        new("croatia", "hre", 70, false, "the Empire's frontier"),
        new("croatia", "papal", 68, false, "the crusade frontier"),
        new("papal", "hre", 62, true, "the Emperor and the Pope"),
        new("papal", "poland", 70, true, "a Catholic Vasa king"),
        new("poland", "austria", 66, true, "Habsburg marriage ties"),
        new("poland", "hre", 58, false, "Catholic neighbours"),
        new("papal", "italy", 60, false, "neighbours on the peninsula"),
        new("papal", "genoa", 60, false, "Catholic neighbours"),
        new("papal", "kongo", 60, false, "Catholic missionaries"),
        new("papal", "venice", 42, false, "quarrels over church rights"),
        new("venice", "genoa", 34, false, "old maritime rivals"),
        new("venice", "austria", 48, false, "Uskok pirates on the Adriatic"),
        new("venice", "croatia", 40, false, "Dalmatian coast disputes"),
        new("venice", "italy", 40, false, "jealousy of Milan"),
        new("venice", "dutch", 56, false, "trade and Protestants"),
        new("italy", "austria", 55, false, "Habsburg neighbours in Lombardy"),

        // ---- Eastern Europe and the steppe ----
        new("poland", "russia", 34, false, "Livonia and Smolensk disputes"),
        new("poland", "crimea", 28, false, "Tatar raids"),
        new("russia", "persia", 62, true, "Caspian trade; Boris Godunov courts the Shah"),
        new("russia", "crimea", 18, false, "Tatar raids on Moscow's southern border"),
        new("russia", "kazakh", 40, false, "the conquest of Siberia"),
        new("russia", "bukhara", 52, false, "steppe caravans"),
        new("russia", "denmark", 54, false, "Godunov seeks a Danish prince for his daughter"),
        new("crimea", "persia", 24, false, "an Ottoman vassal against the Shah"),
        new("crimea", "bukhara", 56, false, "Sunni Turkic kin"),
        new("kazakh", "bukhara", 34, false, "steppe wars with the Uzbeks"),
        new("kazakh", "yuan", 52, false, "Mongol kin"),
        new("kazakh", "persia", 30, false, "Uzbek-steppe raiders in Khorasan"),
        new("bukhara", "persia", 22, false, "the Uzbek wars for Khorasan"),
        new("bukhara", "mughal", 42, false, "the Hindu Kush frontier"),

        // ---- Persia, India and Arabia ----
        new("persia", "mughal", 54, false, "Safavid-Mughal courtesies"),
        new("persia", "austria", 60, false, "Rudolf II's envoys seek an ally against the Turk"),
        new("persia", "uae", 40, false, "rivalry for the Gulf"),
        new("mughal", "ahom", 24, false, "Assam frontier wars"),
        new("uae", "malaysia", 52, false, "Muslim traders of the Indian Ocean"),
        new("uae", "ethiopia", 34, false, "Red Sea trade rivals"),

        // ---- China, Japan, Korea and the steppe frontier ----
        new("ming", "korea", 80, true, "a tributary state saved from the Japanese invasion"),
        new("ming", "vietnam", 62, true, "a tributary state"),
        new("ming", "jurchens", 50, true, "tributary chiefs of Jianzhou"),
        new("ming", "siam", 60, false, "a tributary state"),
        new("ming", "laos", 56, false, "a tributary state"),
        new("ming", "japan", 20, false, "the Imjin War has only just ended"),
        new("ming", "yuan", 22, false, "Mongol raids on the northern border"),
        new("japan", "korea", 18, false, "the Imjin War (1592-98)"),
        new("japan", "siam", 62, false, "red-seal trade"),
        new("japan", "vietnam", 56, false, "red-seal trade"),
        new("japan", "cambodia", 56, false, "Japanese traders in the Khmer ports"),
        new("korea", "jurchens", 36, false, "raids across the northern border"),

        // ---- South-East Asia ----
        new("siam", "burma", 16, false, "Naresuan's war against Burma"),
        new("siam", "cambodia", 24, false, "Lovek was sacked in 1594"),
        new("burma", "laos", 36, false, "the Shan frontier"),
        new("vietnam", "cambodia", 36, false, "Vietnamese expansion to the south"),
        new("cambodia", "laos", 52, false, "Khmer-Lao ties"),

        // ---- Africa ----
        new("kongo", "ethiopia", 54, false, "the two Christian kingdoms of Africa"),
    };

    // ---------------- Reading the table ----------------

    /// <summary>The table's tie between two countries, or null if history says nothing.</summary>
    public static Tie? TieBetween(string a, string b) =>
        Ties.FirstOrDefault(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));

    /// <summary>
    /// How two countries regard each other at the start of 1600, 0-100: the table's rating, else neutral — a little warmer between
    /// countries of one faith, a little cooler between different ones.
    /// </summary>
    public static int Rating(Nation a, Nation b)
    {
        if (TieBetween(a.Id, b.Id) is { } tie) return tie.Rating;
        bool sameFaith = !string.IsNullOrEmpty(a.Religion) && a.Religion.Equals(b.Religion, StringComparison.OrdinalIgnoreCase);
        return NeutralRating + (sameFaith ? SameFaithBonus : -OtherFaithPenalty);
    }

    // ---------------- Starting a campaign ----------------

    /// <summary>
    /// Gives the player's country its 1600 diplomacy: every country's regard for the player (and the baseline that regard settles
    /// back to), and the embassies the player's country keeps. A country with fewer than <see cref="MinEmbassies"/> from the table
    /// also gets one in its warmest neighbours (nearest among equals) that regard it well enough (so a lesser power is not cut off from trade), but never
    /// across the whole world: an isolated island starts without any.
    /// </summary>
    public static void Apply(GameState state)
    {
        var player = state.PlayerNation;
        var embassies = new List<Nation>();

        foreach (var other in state.OtherNations)
        {
            int rating = Rating(player, other);
            other.RelationToPlayer = DiplomacyService.FromDisplayRating(rating);
            other.BaselineRelation = other.RelationToPlayer;
            if (TieBetween(player.Id, other.Id) is { Embassy: true }) embassies.Add(other);
        }

        if (embassies.Count < MinEmbassies)
        {
            var neighbours = state.OtherNations
                .Where(n => !embassies.Contains(n)
                            && DiplomacyService.ToDisplayRating(n.RelationToPlayer) >= Balance.EmbassyMinRating
                            && AiWorldService.Distance(player, n) <= Balance.AiWarRange)
                .OrderByDescending(n => DiplomacyService.ToDisplayRating(n.RelationToPlayer))   // the warmest first...
                .ThenBy(n => AiWorldService.Distance(player, n));                               // ...the nearest among equals
            foreach (var n in neighbours)
            {
                if (embassies.Count >= MinEmbassies) break;
                embassies.Add(n);
            }
        }

        foreach (var n in embassies)
            TreatyService.Add(state, TreatyType.Embassy, player, n);

        if (embassies.Count > 0)
            state.Log($"{player.Name} keeps embassies in: {string.Join(", ", embassies.Select(n => n.Name))}.");
    }
}
