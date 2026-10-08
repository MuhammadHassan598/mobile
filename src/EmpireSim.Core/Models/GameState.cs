namespace EmpireSim.Core.Models;

/// <summary>
/// The full serialisable game state: date, nations, warnings and a
/// rolling event log. This is what gets saved to / loaded from disk.
/// </summary>
public sealed class GameState
{
    public int SaveVersion { get; set; } = 1;

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

    /// <summary>
    /// Player-facing warnings, rebuilt every tick. Only critical warnings
    /// (army maintenance, starvation, bankruptcy, war, revolt) may pop up;
    /// everything else is shown silently in the dashboard.
    /// </summary>
    public List<string> ActiveWarnings { get; set; } = new();

    /// <summary>Rolling log, newest entries appended at the end.</summary>
    public List<string> EventLog { get; set; } = new();

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
    public static GameState NewGame(string playerNationId = "ottoman")
    {
        var state = new GameState();
        var nations = new List<Nation>
        {
            // ---------------- Ottoman Empire ----------------
            new()
            {
                Id = "ottoman", Name = "Ottoman Empire", ColorHex = "#8B0000",
                Silver = 5000, Gold = 12, Food = 5000, Wood = 20, Iron = 10,
                Units = UnitCatalog.SeedArmy(8_000), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Constantinople", MapX = 1335, MapY = 568,
                Farms = 155, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1290, 540), new(1360, 530), new(1380, 570), new(1340, 600), new(1290, 590) },
                    new() { new(1180, 534), new(1290, 524), new(1300, 545), new(1240, 575), new(1180, 560) },
                    new() { new(1330, 560), new(1470, 550), new(1480, 610), new(1380, 625), new(1320, 600) },
                    new() { new(1330, 660), new(1430, 655), new(1440, 730), new(1360, 770), new(1320, 720) },
                }
            },
            // ---------------- Spain (Iberian Union) ----------------
            new()
            {
                Id = "spain", Name = "Iberian Union", ColorHex = "#E74C3C",
                Silver = 6000, Gold = 15, Food = 4500, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 30,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Castile", MapX = 880, MapY = 542,
                Farms = 85, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(830, 500), new(920, 490), new(940, 570), new(860, 590), new(820, 550) },
                    new() { new(915, 495), new(960, 505), new(950, 565), new(915, 560) },
                    new() { new(1100, 596), new(1150, 592), new(1165, 610), new(1120, 630), new(1095, 614) },
                }
            },
            // ---------------- France ----------------
            new()
            {
                Id = "france", Name = "France", ColorHex = "#2E86C1",
                Silver = 5500, Gold = 12, Food = 5000, Wood = 20, Iron = 10,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Paris", MapX = 986, MapY = 460,
                Farms = 100, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(950, 430), new(1020, 425), new(1030, 480), new(970, 495), new(945, 465) },
                    new() { new(920, 495), new(970, 495), new(975, 545), new(930, 555), new(915, 525) },
                    new() { new(975, 495), new(1015, 482), new(1020, 520), new(1010, 550), new(975, 545) },
                }
            },
            // ---------------- England ----------------
            new()
            {
                Id = "england", Name = "England", ColorHex = "#7D3C98",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "London", MapX = 890, MapY = 358,
                Farms = 50, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(860, 330), new(920, 325), new(930, 375), new(880, 390), new(855, 360) },
                    new() { new(850, 270), new(910, 265), new(920, 325), new(860, 330), new(848, 300) },
                }
            },
            // ---------------- Dutch Republic ----------------
            new()
            {
                Id = "dutch", Name = "Netherlands", ColorHex = "#E67E22",
                Silver = 5000, Gold = 15, Food = 2500, Wood = 30, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Holland", MapX = 1012, MapY = 362,
                Farms = 43, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(992, 340), new(1030, 335), new(1035, 375), new(1000, 385), new(990, 360) },
                    new() { new(1030, 335), new(1062, 340), new(1060, 380), new(1035, 375) },
                }
            },
            // ---------------- Austria (Habsburgs) ----------------
            new()
            {
                Id = "austria", Name = "Habsburg Monarchy", ColorHex = "#5D6D7E",
                Silver = 4500, Gold = 10, Food = 4000, Wood = 20, Iron = 12,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Vienna", MapX = 1142, MapY = 428,
                Farms = 83, Mines = 9, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1152, 400), new(1170, 398), new(1172, 445), new(1112, 455), new(1150, 432) },
                    new() { new(1100, 355), new(1165, 350), new(1160, 392), new(1100, 396), new(1095, 375) },
                    new() { new(1170, 445), new(1235, 440), new(1240, 470), new(1180, 474), new(1170, 470) },
                }
            },
            // ---------------- Poland-Lithuania ----------------
            new()
            {
                Id = "poland", Name = "Polish-Lithuanian Commonwealth", ColorHex = "#F1948A",
                Silver = 3500, Gold = 8, Food = 4500, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(6_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Krakow", MapX = 1268, MapY = 405,
                Farms = 87, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1235, 380), new(1300, 375), new(1305, 425), new(1240, 430), new(1230, 405) },
                    new() { new(1300, 330), new(1380, 325), new(1385, 375), new(1300, 375) },
                    new() { new(1305, 425), new(1390, 420), new(1400, 475), new(1320, 480), new(1300, 450) },
                }
            },
            // ---------------- Russia ----------------
            new()
            {
                Id = "russia", Name = "Russia", ColorHex = "#229954",
                Silver = 3500, Gold = 8, Food = 4500, Wood = 30, Iron = 10,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Moscow", MapX = 1462, MapY = 255,
                Farms = 72, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1420, 220), new(1500, 215), new(1510, 280), new(1430, 290), new(1415, 255) },
                    new() { new(1340, 150), new(1430, 145), new(1420, 220), new(1345, 215), new(1330, 180) },
                    new() { new(1510, 280), new(1600, 275), new(1610, 340), new(1520, 350), new(1505, 315) },
                }
            },
            // ---------------- Sweden ----------------
            new()
            {
                Id = "sweden", Name = "Sweden", ColorHex = "#5DADE2",
                Silver = 3000, Gold = 8, Food = 2500, Wood = 30, Iron = 14,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Stockholm", MapX = 1155, MapY = 215,
                Farms = 30, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1120, 180), new(1190, 175), new(1195, 240), new(1130, 250), new(1115, 215) },
                    new() { new(1195, 240), new(1260, 235), new(1265, 300), new(1200, 305), new(1190, 270) },
                }
            },
            // ---------------- Venice ----------------
            new()
            {
                Id = "venice", Name = "Venice", ColorHex = "#17A589",
                Silver = 4500, Gold = 15, Food = 2000, Wood = 15, Iron = 5,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Venice", MapX = 1088, MapY = 508,
                Farms = 35, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1065, 490), new(1110, 485), new(1115, 525), new(1070, 530), new(1060, 510) },
                    new() { new(1330, 640), new(1390, 635), new(1400, 660), new(1340, 665), new(1325, 652) },
                }
            },
            // ---------------- Safavid Persia ----------------
            new()
            {
                Id = "persia", Name = "Iran", ColorHex = "#1F6F3A",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 12, Iron = 8,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Isfahan", MapX = 1605, MapY = 655,
                Farms = 75, Mines = 8, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1570, 630), new(1640, 625), new(1650, 675), new(1580, 685), new(1565, 655) },
                    new() { new(1520, 590), new(1590, 585), new(1585, 630), new(1525, 625), new(1515, 605) },
                    new() { new(1650, 600), new(1720, 595), new(1725, 650), new(1650, 655), new(1645, 625) },
                }
            },
            // ---------------- Mughal Empire ----------------
            new()
            {
                Id = "mughal", Name = "Mughal Empire", ColorHex = "#B8860B",
                Silver = 6000, Gold = 15, Food = 6000, Wood = 15, Iron = 8,
                Units = UnitCatalog.SeedArmy(8_000), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Agra", MapX = 1738, MapY = 728,
                Farms = 115, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1700, 700), new(1770, 695), new(1780, 750), new(1710, 760), new(1695, 730) },
                    new() { new(1780, 750), new(1840, 745), new(1830, 810), new(1770, 815), new(1765, 780) },
                    new() { new(1695, 760), new(1765, 780), new(1750, 870), new(1680, 880), new(1660, 810) },
                }
            },
            // ---------------- Ming Dynasty ----------------
            new()
            {
                Id = "ming", Name = "Ming Dynasty", ColorHex = "#F1C40F",
                Silver = 7000, Gold = 20, Food = 8000, Wood = 25, Iron = 15,
                Units = UnitCatalog.SeedArmy(12_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Beijing", MapX = 1915, MapY = 460,
                Farms = 160, Mines = 11, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1880, 436), new(1950, 432), new(1955, 480), new(1890, 490), new(1875, 462) },
                    new() { new(1900, 520), new(1970, 515), new(1975, 570), new(1905, 575) },
                    new() { new(1870, 605), new(1940, 600), new(1945, 646), new(1875, 651), new(1865, 630) },
                    new() { new(1800, 500), new(1870, 495), new(1875, 560), new(1805, 565), new(1795, 530) },
                }
            },
            // ---------------- Japan ----------------
            new()
            {
                Id = "japan", Name = "Japan", ColorHex = "#D5D8DC",
                Silver = 3500, Gold = 10, Food = 3000, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Edo", MapX = 2098, MapY = 472,
                Farms = 52, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(2070, 450), new(2120, 445), new(2125, 495), new(2075, 500) },
                    new() { new(2055, 505), new(2100, 500), new(2105, 550), new(2060, 555), new(2050, 530) },
                }
            },
            // ---------------- Kazakh Khanate ----------------
            new()
            {
                Id = "kazakh", Name = "Kazakh Khanate", ColorHex = "#2F4F6F",
                Silver = 2500, Gold = 5, Food = 2200, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Turkestan", MapX = 1628, MapY = 448,
                Farms = 27, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1590, 420), new(1660, 415), new(1665, 470), new(1595, 475) },
                    new() { new(1660, 415), new(1740, 410), new(1745, 470), new(1665, 475), new(1658, 445) },
                }
            },
            // ---------------- Morocco ----------------
            new()
            {
                Id = "morocco", Name = "Morocco", ColorHex = "#7D6608",
                Silver = 3000, Gold = 8, Food = 2500, Wood = 10, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Marrakesh", MapX = 832, MapY = 670,
                Farms = 47, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(800, 650), new(860, 645), new(865, 690), new(805, 695) },
                    new() { new(865, 620), new(915, 615), new(920, 650), new(865, 645) },
                }
            },
            // ---------------- Denmark-Norway ----------------
            new()
            {
                Id = "denmark", Name = "Denmark-Norway", ColorHex = "#AD1457",
                Silver = 3000, Gold = 8, Food = 2200, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Denmark", MapX = 1036, MapY = 316,
                Farms = 32, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1000, 298), new(1068, 294), new(1072, 332), new(1008, 338), new(998, 318) },
                    new() { new(1000, 120), new(1078, 115), new(1080, 295), new(1000, 298), new(995, 200) },
                }
            },
            // ---------------- Crimean Khanate ----------------
            new()
            {
                Id = "crimea", Name = "Crimean Khanate", ColorHex = "#00ACC1",
                Silver = 2000, Gold = 5, Food = 2000, Wood = 8, Iron = 3,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Crimea", MapX = 1436, MapY = 502,
                Farms = 25, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1402, 480), new(1460, 475), new(1470, 520), new(1410, 530), new(1397, 505) },
                    new() { new(1470, 475), new(1540, 470), new(1545, 520), new(1475, 525) },
                }
            },
            // ---------------- Ethiopia ----------------
            new()
            {
                Id = "ethiopia", Name = "Ethiopia", ColorHex = "#6D4C41",
                Silver = 2500, Gold = 6, Food = 2500, Wood = 12, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Abyssinia", MapX = 1438, MapY = 830,
                Farms = 40, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1400, 800), new(1470, 795), new(1475, 860), new(1405, 865) },
                    new() { new(1475, 795), new(1530, 790), new(1535, 860), new(1475, 860) },
                }
            },
            // ---------------- Siam ----------------
            new()
            {
                Id = "siam", Name = "Thailand", ColorHex = "#7CB342",
                Silver = 3000, Gold = 8, Food = 3000, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ayutthaya", MapX = 1850, MapY = 719,
                Farms = 52, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1832, 700), new(1872, 697), new(1874, 740), new(1834, 743) },
                    new() { new(1844, 738), new(1902, 736), new(1905, 772), new(1846, 775) },
                }
            },
            // ---------------- Korea ----------------
            new()
            {
                Id = "korea", Name = "Korea", ColorHex = "#BA68C8",
                Silver = 2500, Gold = 6, Food = 2500, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Hanseong", MapX = 2015, MapY = 496,
                Farms = 45, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(2001, 481), new(2026, 478), new(2028, 513), new(2003, 516) },
                    new() { new(2003, 516), new(2028, 513), new(2049, 539), new(2007, 544) },
                }
            },
            // ---------------- Bukhara (Uzbeks) ----------------
            new()
            {
                Id = "bukhara", Name = "Bukhara", ColorHex = "#A1887F",
                Silver = 2500, Gold = 6, Food = 2000, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Bukhara", MapX = 1692, MapY = 504,
                Farms = 29, Mines = 5, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1665, 475), new(1745, 470), new(1748, 530), new(1670, 535), new(1662, 505) },
                    new() { new(1748, 530), new(1792, 528), new(1796, 580), new(1750, 585), new(1745, 555) },
                }
            },
            // ---------------- Holy Roman Empire ----------------
            new()
            {
                Id = "hre", Name = "Holy Roman Empire", ColorHex = "#212121",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 22, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Bavaria", MapX = 1078, MapY = 416,
                Farms = 52, Mines = 7, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1055, 398), new(1100, 396), new(1102, 432), new(1057, 434) },
                    new() { new(1102, 396), new(1148, 394), new(1150, 430), new(1104, 432) },
                }
            },
            // ---------------- Scotland ----------------
            new()
            {
                Id = "scotland", Name = "Scotland", ColorHex = "#1B4F72",
                Silver = 2000, Gold = 5, Food = 1800, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Edinburgh", MapX = 882, MapY = 240,
                Farms = 23, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(848, 215), new(910, 210), new(915, 265), new(850, 270) },
                    new() { new(845, 150), new(905, 145), new(910, 210), new(848, 215) },
                }
            },
            // ---------------- Genoa ----------------
            new()
            {
                Id = "genoa", Name = "Genoa", ColorHex = "#A93226",
                Silver = 4000, Gold = 12, Food = 1500, Wood = 12, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Genoa", MapX = 1042, MapY = 515,
                Farms = 22, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1020, 500), new(1062, 498), new(1064, 530), new(1022, 532) },
                    new() { new(1030, 560), new(1060, 558), new(1062, 600), new(1032, 602) },
                }
            },
            // ---------------- Papal States ----------------
            new()
            {
                Id = "papal", Name = "Papal States", ColorHex = "#F9E79F",
                Silver = 3500, Gold = 10, Food = 1800, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Rome", MapX = 1139, MapY = 576,
                Farms = 27, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1118, 558), new(1156, 554), new(1158, 592), new(1120, 596) },
                    new() { new(1158, 554), new(1192, 552), new(1194, 590), new(1160, 592) },
                }
            },
            // ---------------- Italy ----------------
            new()
            {
                Id = "italy", Name = "Italy", ColorHex = "#7E5109",
                Silver = 3500, Gold = 10, Food = 2200, Wood = 12, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Piedmont", MapX = 1077, MapY = 472,
                Farms = 38, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1055, 455), new(1098, 453), new(1100, 490), new(1057, 492) },
                    new() { new(1116, 505), new(1150, 503), new(1152, 540), new(1118, 542) },
                }
            },
            // ---------------- Croatia ----------------
            new()
            {
                Id = "croatia", Name = "Croatia", ColorHex = "#E59866",
                Silver = 2000, Gold = 5, Food = 1800, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Croatia", MapX = 1176, MapY = 503,
                Farms = 26, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1140, 478), new(1210, 472), new(1214, 520), new(1180, 524), new(1138, 510) },
                    new() { new(1215, 472), new(1268, 468), new(1272, 522), new(1218, 526) },
                }
            },
            // ---------------- Vietnam ----------------
            new()
            {
                Id = "vietnam", Name = "Vietnam", ColorHex = "#148F77",
                Silver = 3000, Gold = 8, Food = 3500, Wood = 18, Iron = 5,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Tonkin", MapX = 1936, MapY = 680,
                Farms = 60, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1906, 650), new(1960, 647), new(1963, 710), new(1909, 713) },
                    new() { new(1909, 713), new(1963, 710), new(1968, 780), new(1912, 783) },
                }
            },
            // ---------------- Burma ----------------
            new()
            {
                Id = "burma", Name = "Burma", ColorHex = "#6C3483",
                Silver = 3000, Gold = 8, Food = 3500, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ava", MapX = 1808, MapY = 722,
                Farms = 58, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1785, 700), new(1830, 697), new(1832, 745), new(1788, 748) },
                    new() { new(1788, 748), new(1832, 745), new(1840, 810), new(1790, 812) },
                }
            },
            // ---------------- Ahom Kingdom ----------------
            new()
            {
                Id = "ahom", Name = "Ahom Kingdom", ColorHex = "#1A5276",
                Silver = 2000, Gold = 5, Food = 2200, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Ahom", MapX = 1827, MapY = 676,
                Farms = 36, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1800, 655), new(1855, 652), new(1858, 695), new(1803, 698) },
                    new() { new(1858, 652), new(1874, 650), new(1876, 695), new(1861, 698) },
                }
            },
            // ---------------- Northern Yuan ----------------
            new()
            {
                Id = "yuan", Name = "Northern Yuan", ColorHex = "#935116",
                Silver = 2000, Gold = 5, Food = 2000, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Khalkha", MapX = 1852, MapY = 405,
                Farms = 22, Mines = 4, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1800, 380), new(1900, 375), new(1905, 430), new(1805, 435) },
                    new() { new(1905, 375), new(1953, 372), new(1956, 430), new(1908, 433) },
                }
            },
            // ---------------- Nepal ----------------
            new()
            {
                Id = "nepal", Name = "Nepal", ColorHex = "#CA6F1E",
                Silver = 1800, Gold = 5, Food = 1800, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Kathmandu", MapX = 1726, MapY = 656,
                Farms = 24, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1700, 640), new(1750, 638), new(1752, 672), new(1702, 674) },
                    new() { new(1752, 638), new(1795, 636), new(1797, 672), new(1754, 674) },
                }
            },
            // ---------------- Kongo ----------------
            new()
            {
                Id = "kongo", Name = "Kongo", ColorHex = "#117A65",
                Silver = 2000, Gold = 5, Food = 2200, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Mbanza Kongo", MapX = 1036, MapY = 857,
                Farms = 33, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1000, 830), new(1070, 828), new(1072, 885), new(1002, 888) },
                    new() { new(1072, 828), new(1130, 826), new(1132, 885), new(1074, 888) },
                }
            },
            // ---------------- Jianzhou Jurchens ----------------
            new()
            {
                Id = "jurchens", Name = "Jianzhou Jurchens", ColorHex = "#4A235A",
                Silver = 2200, Gold = 6, Food = 2000, Wood = 18, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Hetu Ala", MapX = 1984, MapY = 393,
                Farms = 22, Mines = 6, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1960, 378), new(2005, 376), new(2008, 408), new(1962, 410) },
                    new() { new(2008, 376), new(2050, 374), new(2052, 408), new(2010, 410) },
                }
            },
            // ---------------- Cambodia ----------------
            new()
            {
                Id = "cambodia", Name = "Cambodia", ColorHex = "#B7950B",
                Silver = 2500, Gold = 6, Food = 2800, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Angkor", MapX = 1931, MapY = 804,
                Farms = 48, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1905, 786), new(1955, 783), new(1958, 823), new(1908, 826) },
                    new() { new(1958, 783), new(2000, 780), new(2002, 822), new(1960, 824) },
                }
            },
            // ---------------- Laos ----------------
            new()
            {
                Id = "laos", Name = "Laos", ColorHex = "#884EA0",
                Silver = 1800, Gold = 5, Food = 2000, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Luang Prabang", MapX = 1890, MapY = 680,
                Farms = 26, Mines = 3, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1874, 660), new(1904, 657), new(1906, 700), new(1877, 703) },
                    new() { new(1877, 703), new(1906, 700), new(1908, 734), new(1880, 737) },
                }
            },
            // ---------------- Malaysia ----------------
            new()
            {
                Id = "malaysia", Name = "Malaysia", ColorHex = "#2874A6",
                Silver = 3000, Gold = 8, Food = 2200, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Malacca", MapX = 1864, MapY = 803,
                Farms = 38, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1846, 775), new(1880, 773), new(1882, 830), new(1848, 832) },
                    new() { new(1882, 773), new(1905, 772), new(1908, 830), new(1884, 832) },
                }
            },
            // ---------------- United Arab Emirates ----------------
            new()
            {
                Id = "uae", Name = "United Arab Emirates", ColorHex = "#D4AC0D",
                Silver = 2500, Gold = 8, Food = 1200, Wood = 8, Iron = 2,
                Units = UnitCatalog.SeedArmy(1_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Julfar", MapX = 1581, MapY = 776,
                Farms = 14, Mines = 2, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(1560, 752), new(1600, 750), new(1602, 800), new(1562, 802) },
                    new() { new(1602, 750), new(1624, 748), new(1626, 800), new(1604, 802) },
                }
            },
            // ---------------- Micronesia ----------------
            new()
            {
                Id = "micronesia", Name = "Micronesia", ColorHex = "#85C1E9",
                Silver = 1000, Gold = 3, Food = 800, Wood = 10, Iron = 0,
                Units = UnitCatalog.SeedArmy(500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Guam", MapX = 2169, MapY = 603,
                Farms = 11, Mines = 0, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(2150, 588), new(2186, 586), new(2188, 618), new(2152, 620) },
                    new() { new(2152, 620), new(2188, 618), new(2190, 650), new(2154, 652) },
                }
            },
            // ---------------- Easter Island ----------------
            new()
            {
                Id = "easter", Name = "Easter Island", ColorHex = "#F5CBA7",
                Silver = 800, Gold = 2, Food = 500, Wood = 6, Iron = 0,
                Units = UnitCatalog.SeedArmy(300), Warships = 2,
                NextPayday = new DateOnly(1600, 7, 1),
                CapitalName = "Rapa Nui", MapX = 2171, MapY = 967,
                Farms = 5, Mines = 0, Sawmills = 0, Workshops = 0,
                Territory = new List<List<MapPoint>>
                {
                    new() { new(2150, 948), new(2190, 946), new(2192, 986), new(2152, 988) },
                }
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
            // Mineral endowment: deterministic, from the nation's lands.
            n.Stone = 4 * n.Territory.Count;
            n.Lead = 2 * n.Mines;
            n.Copper = 2 * n.Mines;
        }

        var player = nations.FirstOrDefault(n => n.Id == playerNationId) ?? nations[0];
        player.IsPlayer = true;
        state.PlayerNation = player;
        state.OtherNations = nations.Where(n => n != player).ToList();

        // Neutral territories: drawn on the map, owned by no crown.
                state.NeutralRegions = new List<NeutralTerritory>
        {
            new() { Name = "Siberia", LabelX = 1850, LabelY = 230,
                Polygon = new() { new(1610, 80), new(2100, 80), new(2100, 380), new(1700, 380), new(1610, 340) } },
            new() { Name = "Northern Scandinavia", LabelX = 1150, LabelY = 130,
                Polygon = new() { new(1080, 90), new(1240, 90), new(1240, 180), new(1120, 180), new(1080, 140) } },
            new() { Name = "Barbary Coast", LabelX = 1119, LabelY = 670,
                Polygon = new() { new(920, 640), new(1318, 640), new(1318, 700), new(920, 700) } },
            new() { Name = "Sahara", LabelX = 1120, LabelY = 750,
                Polygon = new() { new(920, 700), new(1320, 700), new(1320, 800), new(920, 800) } },
            new() { Name = "West Africa", LabelX = 860, LabelY = 830,
                Polygon = new() { new(800, 760), new(920, 760), new(920, 900), new(800, 900) } },
            new() { Name = "Central Africa", LabelX = 1125, LabelY = 944,
                Polygon = new() { new(920, 888), new(1330, 888), new(1330, 1000), new(920, 1000) } },
            new() { Name = "East Africa", LabelX = 1415, LabelY = 932,
                Polygon = new() { new(1330, 865), new(1500, 865), new(1500, 1000), new(1330, 1000) } },
            new() { Name = "South Africa", LabelX = 1125, LabelY = 1050,
                Polygon = new() { new(920, 1000), new(1330, 1000), new(1250, 1100), new(1000, 1100) } },
            new() { Name = "Arabia", LabelX = 1500, LabelY = 800,
                Polygon = new() { new(1440, 730), new(1560, 726), new(1562, 835), new(1480, 870), new(1440, 800) } },
            new() { Name = "Tibet", LabelX = 1815, LabelY = 604,
                Polygon = new() { new(1785, 585), new(1840, 582), new(1845, 622), new(1790, 626) } },
        };;

        // Uncharted frontier regions, open to colonisation.
                state.FrontierRegions = new List<FrontierRegion>
        {
            new() { Name = "Western Isles", LabelX = 640, LabelY = 560,
                Polygon = new() { new(560, 500), new(720, 490), new(740, 600), new(620, 650), new(550, 590) } },
            new() { Name = "Southern Reaches", LabelX = 700, LabelY = 1010,
                Polygon = new() { new(600, 950), new(800, 940), new(830, 1050), new(680, 1080), new(590, 1020) } },
            new() { Name = "Far Eastern Isles", LabelX = 2120, LabelY = 708,
                Polygon = new() { new(2075, 660), new(2150, 655), new(2175, 740), new(2110, 770), new(2072, 712) } },
        };;

        state.Log($"The campaign begins. Long live {player.Name}!");
        return state;
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
