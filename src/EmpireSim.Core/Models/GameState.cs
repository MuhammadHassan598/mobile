using EmpireSim.Core.Services;

namespace EmpireSim.Core.Models;

/// <summary>
/// The full serialisable game state: date, nations, warnings and a
/// rolling event log. This is what gets saved to / loaded from disk.
/// </summary>
public sealed class GameState
{
    public int SaveVersion { get; set; } = 1;

    /// <summary>
    /// Which edition of the world map (<see cref="WorldGeometry.Version"/>) this state's polygons come from. A save made before the map was
    /// redrawn has 0 and is brought up to date when it is loaded (<see cref="WorldGeometry.Upgrade"/>).
    /// </summary>
    public int MapVersion { get; set; }

    /// <summary>The game always starts on 1 January 1600 (design decision).</summary>
    public DateOnly CurrentDate { get; set; } = new DateOnly(1600, 1, 1);

    public Nation PlayerNation { get; set; } = new();
    public List<Nation> OtherNations { get; set; } = new();

    /// <summary>The player's spy networks abroad.</summary>
    public List<SpyNetwork> SpyNetworks { get; set; } = new();

    /// <summary>Uncharted regions that can be colonised.</summary>
    public List<FrontierRegion> FrontierRegions { get; set; } = new();

    /// <summary>Neutral territories: drawn on the map, owned by no crown.</summary>
    public List<NeutralTerritory> NeutralRegions { get; set; } = new();

    /// <summary>The colony expedition currently at sea (one at a time).</summary>
    public ColonyExpedition? ActiveExpedition { get; set; }

    /// <summary>Armies currently marching to invade.</summary>
    public List<MarchingArmy> MarchingArmies { get; set; } = new();

    public bool Defeated { get; set; }

    /// <summary>Whole countries annexed by the player (hegemony victory).</summary>
    public int NationsAnnexedByPlayer { get; set; }

    public IEnumerable<Nation> AllNations()
    {
        yield return PlayerNation;
        foreach (var n in OtherNations) yield return n;
    }

    /// <summary>The other countries that still exist: an annexed country is part of the one that annexed it, not a state of its own.</summary>
    public IEnumerable<Nation> LiveOtherNations() => OtherNations.Where(n => !n.IsEliminated);

    /// <summary>Everyone under a country's rule, counted once: its own people and those of the territories it has annexed.</summary>
    public long TotalPopulation(Nation n) => n.Population + AnnexedBy(n.Id).Sum(t => t.Population);

    /// <summary>A country's Assembly voting power, counting the people of its territories (an annexed territory itself has none).</summary>
    public int VotingPowerOf(Nation n) => AssemblyService.VotingPower(n, AllNations().ToList());

    /// <summary>The countries that were annexed by <paramref name="nationId"/> (and now belong to it), oldest first.</summary>
    public List<Nation> AnnexedBy(string nationId) =>
        AllNations().Where(n => n.AnnexedById == nationId).OrderBy(n => n.AnnexedDate).ThenBy(n => n.Name).ToList();

    /// <summary>
    /// Player-facing warnings, rebuilt every tick. Only critical warnings
    /// (army maintenance, starvation, bankruptcy, war, revolt) may pop up;
    /// everything else is shown silently in the dashboard.
    /// </summary>
    public List<string> ActiveWarnings { get; set; } = new();

    /// <summary>Rolling log, newest entries appended at the end.</summary>
    public List<string> EventLog { get; set; } = new();

    /// <summary>All trade contracts (buy and sell).</summary>
    public List<TradeContract> TradeContracts { get; set; } = new();

    /// <summary>Assembly proposals.</summary>
    public List<AssemblyProposal> AssemblyProposals { get; set; } = new();

    /// <summary>Active assembly policy effects.</summary>
    public List<ActiveAssemblyPolicy> ActiveAssemblyPolicies { get; set; } = new();

    /// <summary>Embassies, pacts, alliances and trade agreements between nations.</summary>
    public List<Treaty> Treaties { get; set; } = new();

    /// <summary>Soldiers currently lent from one nation's army to another's.</summary>
    public List<TroopLoan> TroopLoans { get; set; } = new();

    /// <summary>Colonies founded so far and who owns them now.</summary>
    public List<Colony> Colonies { get; set; } = new();

    /// <summary>Player missionary influence per target nation id (0 to the conversion threshold).</summary>
    public Dictionary<string, double> MissionaryInfluence { get; set; } = new();

    /// <summary>Everything that moved between states, oldest first (the Movement Report). Capped at <see cref="MaxMovements"/>.</summary>
    public List<MovementRecord> Movements { get; set; } = new();

    public const int MaxMovements = 1500;   // (the AI world adds a lot of news, so the report keeps more)

    /// <summary>Countries the player has beaten and not yet dealt with: annex, take resources, or let go.</summary>
    public List<VictoryDecision> PendingVictories { get; set; } = new();

    /// <summary>
    /// Peace negotiations in the player's wars: every proposal and counteroffer, pending or settled, until the war they belong to ends
    /// (see <see cref="PeaceService"/>). Saved with the game.
    /// </summary>
    public List<PeaceOffer> PeaceOffers { get; set; } = new();

    /// <summary>How each of the player's wars has gone (battles, losses, age), one per enemy at war with the player. Saved with the game.</summary>
    public List<WarFront> WarFronts { get; set; } = new();

    /// <summary>The player's inbox: what happened, filed by the type of event (see <see cref="InboxService"/>). Saved with the game.</summary>
    public List<InboxMessage> Inbox { get; set; } = new();

    /// <summary>Standing conditions already reported to the inbox (condition id → what was reported), so a shortage that goes on is not reported every day.</summary>
    public Dictionary<string, string> InboxConditions { get; set; } = new();

    // ---- The AI world: AI countries live their own lives (see AiWorldService) ----

    /// <summary>Wars between AI countries. (A war with the player is <see cref="Nation.AtWarWithPlayer"/>.)</summary>
    public List<WarRecord> Wars { get; set; } = new();

    /// <summary>
    /// How the AI countries' regard for each other has drifted from its starting value, per pair
    /// (key "idA|idB", ids in alphabetical order). See AiWorldService.Relation.
    /// </summary>
    public Dictionary<string, double> AiRelations { get; set; } = new();

    /// <summary>The AI world wakes up on this date: before it, AI countries only farm and keep their armies.</summary>
    public DateOnly AiWorldStartDate { get; set; } = new DateOnly(1601, 1, 1);

    /// <summary>
    /// Records a movement between states for the Movement Report and writes the same line to the event log,
    /// so a movement is never in one and missing from the other.
    /// </summary>
    public MovementRecord LogMovement(MovementKind kind, MovementStatus status,
        string fromId, string fromName, string toId, string toName, string text, bool playerLinked = false, bool military = false,
        InboxTopic? inbox = null)
    {
        var record = new MovementRecord
        {
            Date = CurrentDate,
            Kind = kind,
            Status = status,
            FromId = fromId,
            FromName = fromName,
            ToId = toId,
            ToName = toName,
            Text = text,
            PlayerLinked = playerLinked || LinkedToPlayer(kind, fromId, toId),
            Military = military,
        };
        Movements.Add(record);

        // Over the cap, world news goes first: records that never concerned the player are dropped before any that did.
        int excess = Movements.Count - MaxMovements;
        for (int i = 0; i < Movements.Count && excess > 0; )
        {
            if (ConcernsPlayer(Movements[i])) { i++; continue; }
            Movements.RemoveAt(i);
            excess--;
        }
        if (excess > 0) Movements.RemoveRange(0, excess);

        Log(text);

        // An event of this type that concerns the player is also reported to the inbox (once: this is the one place it is recorded).
        if (inbox is { } topic && ConcernsPlayer(record))
        {
            bool fromPlayer = fromId == PlayerNation.Id;
            string otherId = fromPlayer ? toId : fromId;
            string otherName = fromPlayer ? toName : fromName;
            InboxService.Notify(this, topic, text, otherId,
                title: $"{InboxCatalog.TopicTitle(topic)} — {otherName}",
                details: $"{text}\n{MovementReport.StatusName(status)}: {fromName} ➜ {toName}");
        }
        return record;
    }

    public MovementRecord LogMovement(MovementKind kind, MovementStatus status, Nation from, Nation to, string text, bool playerLinked = false, bool military = false,
        InboxTopic? inbox = null) =>
        LogMovement(kind, status, from.Id, from.Name, to.Id, to.Name, text, playerLinked, military, inbox);

    /// <summary>Whether a country is in an active defensive alliance with the player.</summary>
    public bool IsPlayerAlly(string nationId) =>
        Treaties.Any(t => t.Type == TreatyType.DefensiveAlliance && t.IsActiveOn(CurrentDate)
                          && t.Involves(PlayerNation.Id) && t.Involves(nationId));

    /// <summary>
    /// Whether a movement concerns the player's country: the player is on one side of it, or it is a war or march that touches
    /// one of the player's allies. Anything else between other countries is world news.
    /// </summary>
    public bool LinkedToPlayer(MovementKind kind, string fromId, string toId)
    {
        string player = PlayerNation.Id;
        if (fromId == player || toId == player) return true;
        return (kind is MovementKind.War or MovementKind.March) && (IsPlayerAlly(fromId) || IsPlayerAlly(toId));
    }

    /// <summary>Whether a saved record belongs in the player's Movement Report.</summary>
    public bool ConcernsPlayer(MovementRecord m) => m.PlayerLinked || m.Involves(PlayerNation.Id);

    public void Log(string message)
    {
        EventLog.Add($"{CurrentDate:dd-MM-yyyy}: {message}");
        if (EventLog.Count > 300)
            EventLog.RemoveRange(0, EventLog.Count - 300);
    }

    /// <summary>
    /// Creates a fresh 1600 campaign. The starter map is a stylised
    /// 1000x700 region with 10 provinces across 4 nations — real
    /// geography arrives with the full map milestone.
    /// </summary>
    /// <summary>
    /// Creates a fresh 1600 campaign. The map is a stylised 2200x1150
    /// Eastern Hemisphere with 55 provinces across 22 nations of 1600 —
    /// the 16 great powers plus 6 minor states — plus neutral territories
    /// and uncharted frontier regions.
    /// </summary>
    /// <summary>
    /// How many separate land patches (polygons) each country had on the first stylised map. The starting stone endowment is 4 per patch, and redrawing
    /// the map along the real coasts changed the polygon counts, so the endowment keeps reading these numbers: the economy is not touched by the map.
    /// </summary>
    private static readonly Dictionary<string, int> OriginalLandPatches = new() { ["ottoman"] = 19, ["spain"] = 13, ["france"] = 2, ["england"] = 2, ["dutch"] = 2, ["austria"] = 3, ["poland"] = 6, ["russia"] = 4, ["sweden"] = 2, ["venice"] = 1, ["persia"] = 5, ["mughal"] = 5, ["ming"] = 2, ["japan"] = 3, ["kazakh"] = 1, ["morocco"] = 2, ["denmark"] = 3, ["crimea"] = 1, ["ethiopia"] = 1, ["siam"] = 1, ["korea"] = 2, ["bukhara"] = 4, ["hre"] = 1, ["scotland"] = 1, ["genoa"] = 1, ["papal"] = 1, ["italy"] = 1, ["croatia"] = 1, ["vietnam"] = 1, ["burma"] = 1, ["ahom"] = 1, ["yuan"] = 1, ["nepal"] = 1, ["kongo"] = 4, ["jurchens"] = 3, ["cambodia"] = 1, ["laos"] = 1, ["malaysia"] = 1, ["uae"] = 1, ["micronesia"] = 1, ["easter"] = 1 };

    public static GameState NewGame(string playerNationId = "ottoman", bool historicalDiplomacy = true)
    {
        var state = new GameState { MapVersion = WorldGeometry.Version };
        var nations = new List<Nation>
        {
            // ---------------- Ottoman Empire ----------------
            new()
            {
                Id = "ottoman", Name = "Ottoman Empire", ColorHex = "#8B0000",
                Gold = 62, Wood = 20, Iron = 10,
                Units = UnitCatalog.SeedArmy(8_000), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Constantinople", MapX = 604, MapY = 382,
                Farms = 155, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("ottoman")
            },
            // ---------------- Spain (Iberian Union) ----------------
            new()
            {
                Id = "spain", Name = "Iberian Union", ColorHex = "#E74C3C",
                Gold = 75, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 30,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Castile", MapX = 206, MapY = 368,
                Farms = 85, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("spain")
            },
            // ---------------- France ----------------
            new()
            {
                Id = "france", Name = "France", ColorHex = "#2E86C1",
                Gold = 67, Wood = 20, Iron = 10,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Paris", MapX = 281, MapY = 308,
                Farms = 100, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("france")
            },
            // ---------------- England ----------------
            new()
            {
                Id = "england", Name = "England", ColorHex = "#7D3C98",
                Gold = 50, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "London", MapX = 221, MapY = 247,
                Farms = 50, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("england")
            },
            // ---------------- Dutch Republic ----------------
            new()
            {
                Id = "dutch", Name = "Netherlands", ColorHex = "#E67E22",
                Gold = 65, Wood = 30, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Holland", MapX = 307, MapY = 267,
                Farms = 43, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("dutch")
            },
            // ---------------- Austria (Habsburgs) ----------------
            new()
            {
                Id = "austria", Name = "Habsburg Monarchy", ColorHex = "#5D6D7E",
                Gold = 55, Wood = 20, Iron = 12,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Vienna", MapX = 389, MapY = 297,
                Farms = 83, Mines = 9, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("austria")
            },
            // ---------------- Poland-Lithuania ----------------
            new()
            {
                Id = "poland", Name = "Polish-Lithuanian Commonwealth", ColorHex = "#F1948A",
                Gold = 43, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(6_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Krakow", MapX = 555, MapY = 287,
                Farms = 87, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("poland")
            },
            // ---------------- Russia ----------------
            new()
            {
                Id = "russia", Name = "Russia", ColorHex = "#229954",
                Gold = 43, Wood = 30, Iron = 10,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Moscow", MapX = 814, MapY = 179,
                Farms = 72, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("russia")
            },
            // ---------------- Sweden ----------------
            new()
            {
                Id = "sweden", Name = "Sweden", ColorHex = "#5DADE2",
                Gold = 38, Wood = 30, Iron = 14,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Stockholm", MapX = 418, MapY = 156,
                Farms = 30, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("sweden")
            },
            // ---------------- Venice ----------------
            new()
            {
                Id = "venice", Name = "Venice", ColorHex = "#17A589",
                Gold = 60, Wood = 15, Iron = 5,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Venice", MapX = 375, MapY = 313,
                Farms = 35, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("venice")
            },
            // ---------------- Safavid Persia ----------------
            new()
            {
                Id = "persia", Name = "Iran", ColorHex = "#1F6F3A",
                Gold = 50, Wood = 12, Iron = 8,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Isfahan", MapX = 783, MapY = 433,
                Farms = 75, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("persia")
            },
            // ---------------- Mughal Empire ----------------
            new()
            {
                Id = "mughal", Name = "Mughal Empire", ColorHex = "#B8860B",
                Gold = 75, Wood = 15, Iron = 8,
                Units = UnitCatalog.SeedArmy(8_000), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Agra", MapX = 1040, MapY = 515,
                Farms = 115, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("mughal")
            },
            // ---------------- Ming Dynasty ----------------
            new()
            {
                Id = "ming", Name = "Ming Dynasty", ColorHex = "#F1C40F",
                Gold = 90, Wood = 25, Iron = 15,
                Units = UnitCatalog.SeedArmy(12_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Beijing", MapX = 1299, MapY = 393,
                Farms = 160, Mines = 11, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("ming")
            },
            // ---------------- Japan ----------------
            new()
            {
                Id = "japan", Name = "Japan", ColorHex = "#D5D8DC",
                Gold = 45, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Edo", MapX = 1611, MapY = 407,
                Farms = 52, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("japan")
            },
            // ---------------- Kazakh Khanate ----------------
            new()
            {
                Id = "kazakh", Name = "Kazakh Khanate", ColorHex = "#2F4F6F",
                Gold = 30, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Turkestan", MapX = 909, MapY = 298,
                Farms = 27, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("kazakh")
            },
            // ---------------- Morocco ----------------
            new()
            {
                Id = "morocco", Name = "Morocco", ColorHex = "#7D6608",
                Gold = 38, Wood = 10, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Marrakesh", MapX = 165, MapY = 465,
                Farms = 47, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("morocco")
            },
            // ---------------- Denmark-Norway ----------------
            new()
            {
                Id = "denmark", Name = "Denmark-Norway", ColorHex = "#AD1457",
                Gold = 38, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Copenhagen", MapX = 350, MapY = 222,
                Farms = 32, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("denmark")
            },
            // ---------------- Crimean Khanate ----------------
            new()
            {
                Id = "crimea", Name = "Crimean Khanate", ColorHex = "#00ACC1",
                Gold = 25, Wood = 8, Iron = 3,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Crimea", MapX = 593, MapY = 309,
                Farms = 25, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("crimea")
            },
            // ---------------- Ethiopia ----------------
            new()
            {
                Id = "ethiopia", Name = "Ethiopia", ColorHex = "#6D4C41",
                Gold = 31, Wood = 12, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Abyssinia", MapX = 646, MapY = 649,
                Farms = 40, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("ethiopia")
            },
            // ---------------- Siam ----------------
            new()
            {
                Id = "siam", Name = "Thailand", ColorHex = "#7CB342",
                Gold = 38, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ayutthaya", MapX = 1262, MapY = 611,
                Farms = 52, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("siam")
            },
            // ---------------- Korea ----------------
            new()
            {
                Id = "korea", Name = "Korea", ColorHex = "#BA68C8",
                Gold = 31, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Hanseong", MapX = 1528, MapY = 363,
                Farms = 45, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("korea")
            },
            // ---------------- Bukhara (Uzbeks) ----------------
            new()
            {
                Id = "bukhara", Name = "Bukhara", ColorHex = "#A1887F",
                Gold = 31, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Bukhara", MapX = 899, MapY = 358,
                Farms = 29, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("bukhara")
            },
            // ---------------- Holy Roman Empire ----------------
            new()
            {
                Id = "hre", Name = "Holy Roman Empire", ColorHex = "#212121",
                Gold = 50, Wood = 22, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Bavaria", MapX = 356, MapY = 258,
                Farms = 52, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("hre")
            },
            // ---------------- Scotland ----------------
            new()
            {
                Id = "scotland", Name = "Scotland", ColorHex = "#1B4F72",
                Gold = 25, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Edinburgh", MapX = 209, MapY = 209,
                Farms = 23, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("scotland")
            },
            // ---------------- Genoa ----------------
            new()
            {
                Id = "genoa", Name = "Genoa", ColorHex = "#A93226",
                Gold = 52, Wood = 12, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Genoa", MapX = 334, MapY = 329,
                Farms = 22, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("genoa")
            },
            // ---------------- Papal States ----------------
            new()
            {
                Id = "papal", Name = "Papal States", ColorHex = "#F9E79F",
                Gold = 45, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Rome", MapX = 386, MapY = 349,
                Farms = 27, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("papal")
            },
            // ---------------- Italy ----------------
            new()
            {
                Id = "italy", Name = "Italy", ColorHex = "#7E5109",
                Gold = 45, Wood = 12, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Piedmont", MapX = 350, MapY = 320,
                Farms = 38, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("italy")
            },
            // ---------------- Croatia ----------------
            new()
            {
                Id = "croatia", Name = "Croatia", ColorHex = "#E59866",
                Gold = 25, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Croatia", MapX = 412, MapY = 321,
                Farms = 26, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("croatia")
            },
            // ---------------- Vietnam ----------------
            new()
            {
                Id = "vietnam", Name = "Vietnam", ColorHex = "#148F77",
                Gold = 38, Wood = 18, Iron = 5,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Tonkin", MapX = 1309, MapY = 582,
                Farms = 60, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("vietnam")
            },
            // ---------------- Burma ----------------
            new()
            {
                Id = "burma", Name = "Burma", ColorHex = "#6C3483",
                Gold = 38, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ava", MapX = 1219, MapY = 544,
                Farms = 58, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("burma")
            },
            // ---------------- Ahom Kingdom ----------------
            new()
            {
                Id = "ahom", Name = "Ahom Kingdom", ColorHex = "#1A5276",
                Gold = 25, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ahom", MapX = 1199, MapY = 498,
                Farms = 36, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("ahom")
            },
            // ---------------- Northern Yuan ----------------
            new()
            {
                Id = "yuan", Name = "Northern Yuan", ColorHex = "#935116",
                Gold = 25, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Khalkha", MapX = 1296, MapY = 299,
                Farms = 22, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("yuan")
            },
            // ---------------- Nepal ----------------
            new()
            {
                Id = "nepal", Name = "Nepal", ColorHex = "#CA6F1E",
                Gold = 23, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Kathmandu", MapX = 1097, MapY = 477,
                Farms = 24, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("nepal")
            },
            // ---------------- Kongo ----------------
            new()
            {
                Id = "kongo", Name = "Kongo", ColorHex = "#117A65",
                Gold = 25, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Mbanza Kongo", MapX = 485, MapY = 778,
                Farms = 33, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("kongo")
            },
            // ---------------- Jianzhou Jurchens ----------------
            new()
            {
                Id = "jurchens", Name = "Jianzhou Jurchens", ColorHex = "#4A235A",
                Gold = 28, Wood = 18, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Hetu Ala", MapX = 1461, MapY = 356,
                Farms = 22, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("jurchens")
            },
            // ---------------- Cambodia ----------------
            new()
            {
                Id = "cambodia", Name = "Cambodia", ColorHex = "#B7950B",
                Gold = 31, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Angkor", MapX = 1300, MapY = 617,
                Farms = 48, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("cambodia")
            },
            // ---------------- Laos ----------------
            new()
            {
                Id = "laos", Name = "Laos", ColorHex = "#884EA0",
                Gold = 23, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Luang Prabang", MapX = 1288, MapY = 568,
                Farms = 26, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("laos")
            },
            // ---------------- Malaysia ----------------
            new()
            {
                Id = "malaysia", Name = "Malaysia", ColorHex = "#2874A6",
                Gold = 38, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Malacca", MapX = 1267, MapY = 693,
                Farms = 38, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("malaysia")
            },
            // ---------------- United Arab Emirates ----------------
            new()
            {
                Id = "uae", Name = "United Arab Emirates", ColorHex = "#D4AC0D",
                Gold = 33, Wood = 8, Iron = 2,
                Units = UnitCatalog.SeedArmy(1_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Julfar", MapX = 800, MapY = 513,
                Farms = 14, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("uae")
            },
            // ---------------- Micronesia ----------------
            new()
            {
                Id = "micronesia", Name = "Micronesia", ColorHex = "#85C1E9",
                Gold = 13, Wood = 10, Iron = 0,
                Units = UnitCatalog.SeedArmy(500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Guam", MapX = 1750, MapY = 690,
                Farms = 11, Mines = 0, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("micronesia")
            },
            // ---------------- Easter Island ----------------
            new()
            {
                Id = "easter", Name = "Easter Island", ColorHex = "#F5CBA7",
                Gold = 10, Wood = 6, Iron = 0,
                Units = UnitCatalog.SeedArmy(300), Warships = 2,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Rapa Nui", MapX = 2120, MapY = 900,
                Farms = 5, Mines = 0, Sawmills = 0, Workshops = 0,
                Territory = WorldGeometry.Nation("easter")
            },
        };

        ApplyReligions(nations);
        ApplyHistoricalPopulations(nations);
        ApplyEmblems(nations);
        ApplySelectPins(nations);

        foreach (var n in nations)
        {
            // The simulation runs on the historical 1600 population.
            n.Population = n.HistoricalPopulation;
            n.Gold = Services.Balance.StartingGold;
            // Mineral endowment: deterministic, from the nation's lands.
            n.Stone = 4 * OriginalLandPatches.GetValueOrDefault(n.Id, n.Territory.Count);
            n.Lead = 2 * n.Mines;
            n.Copper = 2 * n.Mines;
            // Every item (food and minerals) starts at one week of the population's need.
            ConsumptionService.SeedStock(n, Services.Balance.StartingStockDays);
        }

        var player = nations.FirstOrDefault(n => n.Id == playerNationId) ?? nations[0];
        player.IsPlayer = true;
        state.PlayerNation = player;
        state.OtherNations = nations.Where(n => n != player).ToList();

        // Neutral territories: drawn on the map, owned by no crown.
                state.NeutralRegions = new List<NeutralTerritory>
        {
            new() { Name = "Ireland", LabelX = 168, LabelY = 248, Polygons = WorldGeometry.Neutral("Ireland") },
            new() { Name = "Iceland", LabelX = 63, LabelY = 140, Polygons = WorldGeometry.Neutral("Iceland") },
            new() { Name = "Switzerland", LabelX = 322, LabelY = 305, Polygons = WorldGeometry.Neutral("Switzerland") },
            new() { Name = "Sahara", LabelX = 341, LabelY = 533, Polygons = WorldGeometry.Neutral("Sahara") },
            new() { Name = "West Africa", LabelX = 351, LabelY = 656, Polygons = WorldGeometry.Neutral("West Africa") },
            new() { Name = "Arabia", LabelX = 736, LabelY = 551, Polygons = WorldGeometry.Neutral("Arabia") },
            new() { Name = "East Africa", LabelX = 542, LabelY = 610, Polygons = WorldGeometry.Neutral("East Africa") },
            new() { Name = "South Africa", LabelX = 492, LabelY = 971, Polygons = WorldGeometry.Neutral("South Africa") },
            new() { Name = "Tibet", LabelX = 1165, LabelY = 434, Polygons = WorldGeometry.Neutral("Tibet") },
            new() { Name = "Siberia", LabelX = 1359, LabelY = 172, Polygons = WorldGeometry.Neutral("Siberia") },
            new() { Name = "East Indies", LabelX = 1669, LabelY = 786, Polygons = WorldGeometry.Neutral("East Indies") },
            new() { Name = "Greenland", LabelX = 25, LabelY = 40, Polygons = WorldGeometry.Neutral("Greenland") },
            new() { Name = "Svalbard", LabelX = 414, LabelY = 17, Polygons = WorldGeometry.Neutral("Svalbard") },
        };;

        // Uncharted frontier regions, open to colonisation.
                        state.FrontierRegions = new List<FrontierRegion>
        {
            new() { Name = "Western Isles", LabelX = 69, LabelY = 245, Polygons = WorldGeometry.Frontier("Western Isles") },
            new() { Name = "Southern Reaches", LabelX = 1233, LabelY = 1021, Polygons = WorldGeometry.Frontier("Southern Reaches") },
            new() { Name = "Far Eastern Isles", LabelX = 1846, LabelY = 815, Polygons = WorldGeometry.Frontier("Far Eastern Isles") },
        };;

        // Seed historically-accurate starting production buildings (1600 economies)
        foreach (var nation in nations)
        {
            SeedProductionBuildings(nation);
            // The historical list above keeps only the gold mines: every consumed item's mills are
            // re-sized so the nation starts 30% (big) / 15% (mid) / 10% (small) short of its need.
            ConsumptionService.SeedMills(nation);
            // ...but that is the PLAYER's starting challenge. Every other country feeds itself (and keeps doing so
            // as it grows: see AiWorldService), so only the country the player picks begins in shortage.
            if (!nation.IsPlayer) ConsumptionService.EnsureSupply(nation, Services.Balance.AiSupplyCoverage);
            nation.BaseSoldiers = nation.Soldiers;
            nation.BasePopulation = nation.Population;
        }

        // The diplomatic web of 1600: embassies, friendships and quarrels of the chosen country.
        if (historicalDiplomacy) HistoricalDiplomacy.Apply(state);

        state.Log($"The campaign begins. Long live {player.Name}!");
        return state;
    }

    /// <summary>
    /// Seeds starting production buildings based on each nation's 1600 economy.
    /// Larger/richer economies get more buildings; specialties reflect historical strengths.
    /// </summary>
    private static void SeedProductionBuildings(Nation nation)
    {
        var b = new Dictionary<string, int>();
        // Base scale from population (per million)
        double popM = nation.Population / 1_000_000.0;
        int baseFood = (int)(popM * 2);
        int baseMills = (int)(popM * 1);

        // Default: farms and basic food production scaled by population
        if (baseFood > 0)
        {
            b["farm"] = baseFood;
            b["bakery"] = baseFood / 3;
            b["flourmill"] = baseFood / 4;
            b["cattlefarm"] = baseFood / 5;
        }

        // Nation-specific historical specialties (1600)
        switch (nation.Id)
        {
            case "ming": // China: world's largest economy, silk, porcelain, tea, massive agriculture
                b["farm"] = 800; b["bakery"] = 200; b["flourmill"] = 150;
                b["tailoring"] = 120; b["saltmine"] = 60; b["perfume"] = 20;
                b["ironmine"] = 40; b["copperminer"] = 30; b["goldmine"] = 25;
                b["sawmill"] = 80; b["stonequarry"] = 60;
                break;

            case "mughal": // India: world's textile leader, spices, agriculture
                b["farm"] = 500; b["bakery"] = 120; b["tailoring"] = 150;
                b["saltmine"] = 40; b["perfume"] = 30; b["sheepfarm"] = 60;
                b["ironmine"] = 25; b["goldmine"] = 20; b["stonequarry"] = 40;
                break;

            case "ottoman": // Diverse empire: textiles, food, military
                b["farm"] = 155; b["bakery"] = 50; b["tailoring"] = 40;
                b["saltmine"] = 25; b["sheepfarm"] = 30; b["cattlefarm"] = 40;
                b["ironmine"] = 20; b["copperminer"] = 15; b["leadminer"] = 10;
                b["sawmill"] = 25; b["stonequarry"] = 20;
                break;

            case "iran": // Safavid: silk, carpets
                b["farm"] = 80; b["tailoring"] = 60; b["sheepfarm"] = 40;
                b["saltmine"] = 15; b["perfume"] = 15;
                b["ironmine"] = 12; b["copperminer"] = 10; b["stonequarry"] = 15;
                break;

            case "iberian": // Spain/Portugal: agriculture, shipbuilding (American silver not modeled)
                b["farm"] = 90; b["bakery"] = 30; b["sheepfarm"] = 35;
                b["saltmine"] = 20; b["ironmine"] = 18; b["copperminer"] = 12;
                b["sawmill"] = 20;
                break;

            case "france": // Breadbasket, wine, textiles
                b["farm"] = 180; b["bakery"] = 70; b["flourmill"] = 50;
                b["tailoring"] = 45; b["sheepfarm"] = 40; b["cattlefarm"] = 50;
                b["saltmine"] = 20; b["ironmine"] = 15; b["sawmill"] = 30;
                break;

            case "england": // Wool leader, growing textiles
                b["sheepfarm"] = 80; b["tailoring"] = 50; b["farm"] = 60;
                b["bakery"] = 25; b["ironmine"] = 12; b["copperminer"] = 8;
                b["sawmill"] = 15;
                break;

            case "netherlands": // Trade, shipbuilding, textiles b["tailoring"] = 35; b["sawmill"] = 25;
                b["farm"] = 25; b["bakery"] = 15; b["saltmine"] = 10;
                break;

            case "hre": // Germany: mining (silver, iron), agriculture
                b["ironmine"] = 35; b["copperminer"] = 20; b["leadminer"] = 15;
                b["saltmine"] = 25; b["farm"] = 120; b["bakery"] = 40;
                b["sawmill"] = 35; b["stonequarry"] = 25;
                break;

            case "plc": // Poland-Lithuania: grain exporter
                b["farm"] = 150; b["flourmill"] = 60; b["bakery"] = 40;
                b["cattlefarm"] = 35; b["sawmill"] = 25; b["saltmine"] = 15;
                break;

            case "russia": // Furs, timber, agriculture
                b["furfarm"] = 60; b["sawmill"] = 50; b["farm"] = 100;
                b["bakery"] = 30; b["ironmine"] = 15; b["saltmine"] = 20;
                break;

            case "sweden": // Iron and copper (world's largest copper)
                b["ironmine"] = 30; b["copperminer"] = 35; b["sawmill"] = 30;
                b["farm"] = 30; b["furfarm"] = 15;
                break;

            case "japan": // Rice agriculture, silver mining
                b["farm"] = 120; b["bakery"] = 30; b["saltmine"] = 20;
                b["ironmine"] = 15; b["copperminer"] = 12; b["goldmine"] = 15;
                b["sawmill"] = 25; b["stonequarry"] = 20;
                break;

            case "morocco": // Agriculture, leather, textiles
                b["farm"] = 50; b["tailoring"] = 20; b["sheepfarm"] = 25;
                b["saltmine"] = 15; b["bakery"] = 15;
                break;

            case "ethiopia": // Agriculture, coffee (not modeled), livestock
                b["farm"] = 40; b["cattlefarm"] = 25; b["sheepfarm"] = 20;
                b["bakery"] = 10;
                break;

            case "kongo": // Agriculture, copper, textiles
                b["farm"] = 30; b["copperminer"] = 10; b["tailoring"] = 10;
                break;

            case "thailand": case "siam": // Rice, spices
                b["farm"] = 60; b["bakery"] = 15; b["saltmine"] = 10;
                b["perfume"] = 10;
                break;

            case "burma": case "myanmar": // Rice, teak
                b["farm"] = 50; b["sawmill"] = 20; b["bakery"] = 12;
                break;

            case "vietnam": // Rice
                b["farm"] = 55; b["bakery"] = 12; b["saltmine"] = 8;
                break;

            case "korea": // Rice, agriculture
                b["farm"] = 70; b["bakery"] = 18; b["saltmine"] = 12;
                b["ironmine"] = 8;
                break;

            case "nepal": // Mountain agriculture
                b["farm"] = 25; b["sheepfarm"] = 15;
                break;

            case "ahom": // Assam: rice, silk
                b["farm"] = 35; b["tailoring"] = 12;
                break;

            case "kazakh": // Nomadic: livestock
                b["sheepfarm"] = 40; b["cattlefarm"] = 30; b["furfarm"] = 20;
                break;

            case "bukhara": case "uzbek": // Silk Road trade, agriculture
                b["farm"] = 40; b["tailoring"] = 20; b["sheepfarm"] = 20;
                b["saltmine"] = 10;
                break;

            case "crimean": // Livestock, grain
                b["sheepfarm"] = 25; b["cattlefarm"] = 20; b["farm"] = 25;
                break;

            case "yuan": // Northern Yuan: nomadic livestock
                b["sheepfarm"] = 35; b["cattlefarm"] = 25; b["furfarm"] = 15;
                break;

            case "jurchens": // Manchuria: furs, ginseng (not modeled)
                b["furfarm"] = 25; b["farm"] = 20; b["sawmill"] = 15;
                break;

            case "denmark": // Agriculture, shipbuilding
                b["farm"] = 35; b["sawmill"] = 15;
                b["bakery"] = 12;
                break;

            case "scotland": // Wool, agriculture
                b["sheepfarm"] = 30; b["farm"] = 25; b["tailoring"] = 12;
                break;

            case "genoa": case "venice": // Trade, shipbuilding b["tailoring"] = 15; b["sawmill"] = 12;
                break;

            case "papal": case "italy": // Agriculture, textiles
                b["farm"] = 45; b["tailoring"] = 20; b["bakery"] = 18;
                b["sheepfarm"] = 15;
                break;

            case "croatia": // Agriculture, timber
                b["farm"] = 25; b["sawmill"] = 12; b["bakery"] = 8;
                break;

            case "scotland2": // (placeholder, not used)
                break;

            case "cambodia": case "laos": // Rice
                b["farm"] = 30; b["bakery"] = 8;
                break;

            case "malaysia": // Spices, tin
                b["farm"] = 25; b["perfume"] = 12; b["copperminer"] = 8;
                break;

            case "uae": // Pearls (not modeled), trade
                b["saltmine"] = 8;
                break;

            case "micronesia": case "easter": // Small islands, minimal
                b["farm"] = 5; b["bakery"] = 2;
                break;

            default:
                // Generic fallback based on population
                break;
        }

        nation.ProductionBuildings = b;
    }

    private static void ApplyReligions(List<Nation> nations)
    {
        var map = new Dictionary<string, string>
        {
            ["ottoman"] = "Islam", ["spain"] = "Christianity", ["france"] = "Christianity",
            ["england"] = "Christianity", ["dutch"] = "Christianity", ["austria"] = "Christianity",
            ["poland"] = "Christianity", ["russia"] = "Christianity", ["sweden"] = "Christianity",
            ["venice"] = "Christianity", ["persia"] = "Islam", ["mughal"] = "Islam",
            ["ming"] = "Confucianism", ["japan"] = "Shinto", ["kazakh"] = "Islam",
            ["morocco"] = "Islam", ["denmark"] = "Christianity", ["crimea"] = "Islam",
            ["ethiopia"] = "Christianity", ["siam"] = "Buddhism", ["korea"] = "Confucianism",
            ["bukhara"] = "Islam", ["hre"] = "Christianity", ["scotland"] = "Christianity",
            ["genoa"] = "Christianity", ["papal"] = "Christianity", ["italy"] = "Christianity",
            ["croatia"] = "Christianity", ["vietnam"] = "Buddhism", ["burma"] = "Buddhism",
            ["ahom"] = "Hinduism", ["yuan"] = "Buddhism", ["nepal"] = "Hinduism",
            ["kongo"] = "Christianity", ["jurchens"] = "Shamanism", ["cambodia"] = "Buddhism",
            ["laos"] = "Buddhism", ["malaysia"] = "Islam", ["uae"] = "Islam",
            ["micronesia"] = "Animism", ["easter"] = "Animism",
        };
        foreach (var n in nations)
            n.Religion = map.TryGetValue(n.Id, out var r) ? r : "";
    }

    /// <summary>Historical population estimates for 1600, shown on the select screen.</summary>
    private static void ApplyHistoricalPopulations(List<Nation> nations)
    {
        var map = new Dictionary<string, long>
        {
            ["ottoman"] = 30000000, ["spain"] = 20000000, ["france"] = 18500000,
            ["england"] = 6100000, ["dutch"] = 1500000, ["austria"] = 8000000,
            ["poland"] = 11000000, ["russia"] = 13000000, ["sweden"] = 1000000,
            ["venice"] = 2500000, ["persia"] = 10000000, ["mughal"] = 100000000,
            ["ming"] = 160000000, ["japan"] = 12000000, ["kazakh"] = 1200000,
            ["morocco"] = 4000000, ["denmark"] = 1000000, ["crimea"] = 500000,
            ["ethiopia"] = 3000000, ["siam"] = 2500000, ["korea"] = 10000000,
            ["bukhara"] = 2000000, ["hre"] = 20000000, ["scotland"] = 800000,
            ["genoa"] = 600000, ["papal"] = 1500000, ["italy"] = 3000000,
            ["croatia"] = 800000, ["vietnam"] = 5000000, ["burma"] = 3000000,
            ["ahom"] = 2000000, ["yuan"] = 1000000, ["nepal"] = 1000000,
            ["kongo"] = 2000000, ["jurchens"] = 400000, ["cambodia"] = 1000000,
            ["laos"] = 800000, ["malaysia"] = 500000, ["uae"] = 100000,
            ["micronesia"] = 100000, ["easter"] = 12000,
        };
        foreach (var n in nations)
            n.HistoricalPopulation = map.TryGetValue(n.Id, out var p) ? p : 0;
    }

    /// <summary>
    /// Capital pin positions on the painted parchment select-screen map, in
    /// image percent. The painted map is decorative, not the game coordinate
    /// space, so every capital is placed by hand.
    /// </summary>
    private static void ApplySelectPins(List<Nation> nations)
    {
        var map = new Dictionary<string, (double x, double y)>
        {
            ["ottoman"] = (35.0, 32.5), ["spain"] = (18.0, 31.0), ["france"] = (23.5, 26.5),
            ["england"] = (20.8, 20.5), ["dutch"] = (26.3, 22.5), ["austria"] = (32.5, 24.5),
            ["poland"] = (38.5, 21.5), ["russia"] = (45.5, 15.5), ["sweden"] = (35.5, 10.5),
            ["venice"] = (29.3, 30.0), ["persia"] = (50.5, 36.0), ["mughal"] = (61.0, 45.5),
            ["ming"] = (80.0, 29.5), ["japan"] = (90.8, 42.5), ["kazakh"] = (58.5, 26.5),
            ["morocco"] = (15.0, 36.5), ["denmark"] = (31.8, 16.8), ["crimea"] = (39.5, 28.5),
            ["ethiopia"] = (47.5, 51.0), ["siam"] = (72.5, 50.5), ["korea"] = (85.8, 40.5),
            ["bukhara"] = (55.5, 30.5), ["hre"] = (31.0, 23.5), ["scotland"] = (19.3, 16.8),
            ["genoa"] = (27.8, 30.5), ["papal"] = (28.8, 32.8), ["italy"] = (27.5, 28.8),
            ["croatia"] = (31.3, 27.3), ["vietnam"] = (75.3, 47.5), ["burma"] = (69.5, 48.5),
            ["ahom"] = (67.3, 44.0), ["yuan"] = (66.5, 26.5), ["nepal"] = (62.8, 42.5),
            ["kongo"] = (37.5, 63.0), ["jurchens"] = (82.5, 27.5), ["cambodia"] = (73.8, 52.0),
            ["laos"] = (72.3, 47.5), ["malaysia"] = (71.3, 57.5), ["uae"] = (51.5, 44.5),
            ["micronesia"] = (95.5, 55.0), ["easter"] = (93.0, 75.0),
        };
        foreach (var n in nations)
            if (map.TryGetValue(n.Id, out var pin)) (n.PinX, n.PinY) = pin;
    }

    /// <summary>Emblem per nation, used for the civilization grid and banner.</summary>
    private static void ApplyEmblems(List<Nation> nations)
    {
        var map = new Dictionary<string, string>
        {
            ["ottoman"] = "☪️", ["spain"] = "🏰", ["france"] = "⚜️",
            ["england"] = "🦁", ["dutch"] = "🌷", ["austria"] = "🦅",
            ["poland"] = "🐎", ["russia"] = "🐻", ["sweden"] = "👑",
            ["venice"] = "⚓", ["persia"] = "☀️", ["mughal"] = "🕌",
            ["ming"] = "🐉", ["japan"] = "⛩️", ["kazakh"] = "🏹",
            ["morocco"] = "🌙", ["denmark"] = "🛡️", ["crimea"] = "🐺",
            ["ethiopia"] = "⛪", ["siam"] = "🐘", ["korea"] = "🏯",
            ["bukhara"] = "🐫", ["hre"] = "🏛️", ["scotland"] = "🦄",
            ["genoa"] = "🧭", ["papal"] = "✝️", ["italy"] = "🍇",
            ["croatia"] = "⛵", ["vietnam"] = "🎋", ["burma"] = "🛕",
            ["ahom"] = "🗡️", ["yuan"] = "🏕️", ["nepal"] = "🏔️",
            ["kongo"] = "🥁", ["jurchens"] = "🐗", ["cambodia"] = "🗿",
            ["laos"] = "🌅", ["malaysia"] = "🌴", ["uae"] = "🏜️",
            ["micronesia"] = "🐚", ["easter"] = "🐢",
        };
        foreach (var n in nations)
            n.Emblem = map.TryGetValue(n.Id, out var e) ? e : "🏳️";
    }
}
