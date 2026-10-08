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
    public List<Province> FrontierRegions { get; set; } = new();

    /// <summary>Neutral territories: drawn on the map, owned by no crown.</summary>
    public List<Province> NeutralRegions { get; set; } = new();

    /// <summary>The colony expedition currently at sea (one at a time).</summary>
    public ColonyExpedition? ActiveExpedition { get; set; }

    /// <summary>Armies currently marching to invade.</summary>
    public List<MarchingArmy> MarchingArmies { get; set; } = new();

    public bool VictoryAchieved { get; set; }
    public bool Defeated { get; set; }

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

    private static Province P(string name, long pop, int farms, int mines,
        double lx, double ly, params (double x, double y)[] pts) => new()
    {
        Name = name,
        Population = pop,
        Farms = farms,
        Mines = mines,
        LabelX = lx,
        LabelY = ly,
        Polygon = pts.Select(p => new MapPoint(p.x, p.y)).ToList(),
    };

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
                Provinces = new List<Province>
                {
                    P("Constantinople", 40_000, 40, 2, 1335, 568,
                        (1290,540),(1360,530),(1380,570),(1340,600),(1290,590)),
                    P("Rumelia", 25_000, 30, 2, 1240, 548,
                        (1180,534),(1290,524),(1300,545),(1240,575),(1180,560)),
                    P("Anatolia", 30_000, 35, 3, 1402, 590,
                        (1330,560),(1470,550),(1480,610),(1380,625),(1320,600)),
                    P("Egypt", 35_000, 50, 1, 1380, 712,
                        (1330,660),(1430,655),(1440,730),(1360,770),(1320,720)),
                }
            },
            // ---------------- Spain (Iberian Union) ----------------
            new()
            {
                Id = "spain", Name = "Iberian Union", ColorHex = "#E74C3C",
                Silver = 6000, Gold = 15, Food = 4500, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 30,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Castile", 35_000, 35, 3, 880, 542,
                        (830,500),(920,490),(940,570),(860,590),(820,550)),
                    P("Aragon", 15_000, 20, 2, 938, 530,
                        (915,495),(960,505),(950,565),(915,560)),
                    P("Naples", 25_000, 30, 2, 1130, 612,
                        (1100,596),(1150,592),(1165,610),(1120,630),(1095,614)),
                }
            },
            // ---------------- France ----------------
            new()
            {
                Id = "france", Name = "France", ColorHex = "#2E86C1",
                Silver = 5500, Gold = 12, Food = 5000, Wood = 20, Iron = 10,
                Units = UnitCatalog.SeedArmy(9_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Paris", 40_000, 40, 2, 986, 460,
                        (950,430),(1020,425),(1030,480),(970,495),(945,465)),
                    P("Aquitaine", 20_000, 30, 2, 945, 525,
                        (920,495),(970,495),(975,545),(930,555),(915,525)),
                    P("Languedoc", 20_000, 30, 2, 998, 518,
                        (975,495),(1015,482),(1020,520),(1010,550),(975,545)),
                }
            },
            // ---------------- England ----------------
            new()
            {
                Id = "england", Name = "England", ColorHex = "#7D3C98",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("London", 35_000, 30, 2, 890, 358,
                        (860,330),(920,325),(930,375),(880,390),(855,360)),
                    P("York", 15_000, 20, 3, 883, 298,
                        (850,270),(910,265),(920,325),(860,330),(848,300)),
                }
            },
            // ---------------- Dutch Republic ----------------
            new()
            {
                Id = "dutch", Name = "Netherlands", ColorHex = "#E67E22",
                Silver = 5000, Gold = 15, Food = 2500, Wood = 30, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Holland", 30_000, 25, 1, 1012, 362,
                        (992,340),(1030,335),(1035,375),(1000,385),(990,360)),
                    P("Gelderland", 12_000, 18, 1, 1048, 360,
                        (1030,335),(1062,340),(1060,380),(1035,375)),
                }
            },
            // ---------------- Austria (Habsburgs) ----------------
            new()
            {
                Id = "austria", Name = "Habsburg Monarchy", ColorHex = "#5D6D7E",
                Silver = 4500, Gold = 10, Food = 4000, Wood = 20, Iron = 12,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Vienna", 30_000, 30, 3, 1142, 428,
                        (1152,400),(1170,398),(1172,445),(1112,455),(1150,432)),
                    P("Bohemia", 18_000, 25, 4, 1130, 374,
                        (1100,355),(1165,350),(1160,392),(1100,396),(1095,375)),
                    P("Hungary", 20_000, 28, 2, 1205, 458,
                        (1170,445),(1235,440),(1240,470),(1180,474),(1170,470)),
                }
            },
            // ---------------- Poland-Lithuania ----------------
            new()
            {
                Id = "poland", Name = "Polish-Lithuanian Commonwealth", ColorHex = "#F1948A",
                Silver = 3500, Gold = 8, Food = 4500, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(6_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Krakow", 28_000, 32, 3, 1268, 405,
                        (1235,380),(1300,375),(1305,425),(1240,430),(1230,405)),
                    P("Lithuania", 15_000, 20, 2, 1342, 352,
                        (1300,330),(1380,325),(1385,375),(1300,375)),
                    P("Ukraine", 18_000, 35, 2, 1350, 450,
                        (1305,425),(1390,420),(1400,475),(1320,480),(1300,450)),
                }
            },
            // ---------------- Russia ----------------
            new()
            {
                Id = "russia", Name = "Russia", ColorHex = "#229954",
                Silver = 3500, Gold = 8, Food = 4500, Wood = 30, Iron = 10,
                Units = UnitCatalog.SeedArmy(7_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Moscow", 30_000, 30, 3, 1462, 255,
                        (1420,220),(1500,215),(1510,280),(1430,290),(1415,255)),
                    P("Novgorod", 15_000, 20, 2, 1380, 185,
                        (1340,150),(1430,145),(1420,220),(1345,215),(1330,180)),
                    P("Kazan", 15_000, 22, 3, 1555, 312,
                        (1510,280),(1600,275),(1610,340),(1520,350),(1505,315)),
                }
            },
            // ---------------- Sweden ----------------
            new()
            {
                Id = "sweden", Name = "Sweden", ColorHex = "#5DADE2",
                Silver = 3000, Gold = 8, Food = 2500, Wood = 30, Iron = 14,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Stockholm", 20_000, 18, 4, 1155, 215,
                        (1120,180),(1190,175),(1195,240),(1130,250),(1115,215)),
                    P("Finland", 10_000, 12, 3, 1228, 270,
                        (1195,240),(1260,235),(1265,300),(1200,305),(1190,270)),
                }
            },
            // ---------------- Venice ----------------
            new()
            {
                Id = "venice", Name = "Venice", ColorHex = "#17A589",
                Silver = 4500, Gold = 15, Food = 2000, Wood = 15, Iron = 5,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 25,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Venice", 25_000, 20, 1, 1088, 508,
                        (1065,490),(1110,485),(1115,525),(1070,530),(1060,510)),
                    P("Crete", 8_000, 15, 1, 1362, 650,
                        (1330,640),(1390,635),(1400,660),(1340,665),(1325,652)),
                }
            },
            // ---------------- Safavid Persia ----------------
            new()
            {
                Id = "persia", Name = "Iran", ColorHex = "#1F6F3A",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 12, Iron = 8,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Isfahan", 30_000, 30, 3, 1605, 655,
                        (1570,630),(1640,625),(1650,675),(1580,685),(1565,655)),
                    P("Tabriz", 18_000, 25, 3, 1552, 608,
                        (1520,590),(1590,585),(1585,630),(1525,625),(1515,605)),
                    P("Khorasan", 15_000, 20, 2, 1685, 625,
                        (1650,600),(1720,595),(1725,650),(1650,655),(1645,625)),
                }
            },
            // ---------------- Mughal Empire ----------------
            new()
            {
                Id = "mughal", Name = "Mughal Empire", ColorHex = "#B8860B",
                Silver = 6000, Gold = 15, Food = 6000, Wood = 15, Iron = 8,
                Units = UnitCatalog.SeedArmy(8_000), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Agra", 40_000, 45, 2, 1738, 728,
                        (1700,700),(1770,695),(1780,750),(1710,760),(1695,730)),
                    P("Bengal", 25_000, 40, 2, 1800, 780,
                        (1780,750),(1840,745),(1830,810),(1770,815),(1765,780)),
                    P("Deccan", 20_000, 30, 3, 1705, 822,
                        (1695,760),(1765,780),(1750,870),(1680,880),(1660,810)),
                }
            },
            // ---------------- Ming China ----------------
            new()
            {
                Id = "ming", Name = "Ming China", ColorHex = "#F1C40F",
                Silver = 7000, Gold = 20, Food = 8000, Wood = 25, Iron = 15,
                Units = UnitCatalog.SeedArmy(12_000), Warships = 20,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Beijing", 45_000, 40, 3, 1915, 460,
                        (1880,436),(1950,432),(1955,480),(1890,490),(1875,462)),
                    P("Nanjing", 35_000, 45, 2, 1938, 545,
                        (1900,520),(1970,515),(1975,570),(1905,575)),
                    P("Canton", 25_000, 35, 2, 1905, 628,
                        (1870,605),(1940,600),(1945,646),(1875,651),(1865,630)),
                    P("Sichuan", 25_000, 40, 4, 1835, 530,
                        (1800,500),(1870,495),(1875,560),(1805,565),(1795,530)),
                }
            },
            // ---------------- Japan ----------------
            new()
            {
                Id = "japan", Name = "Japan", ColorHex = "#D5D8DC",
                Silver = 3500, Gold = 10, Food = 3000, Wood = 25, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 15,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Edo", 30_000, 30, 3, 2098, 472,
                        (2070,450),(2120,445),(2125,495),(2075,500)),
                    P("Kyushu", 12_000, 22, 2, 2078, 528,
                        (2055,505),(2100,500),(2105,550),(2060,555),(2050,530)),
                }
            },
            // ---------------- Kazakh Khanate ----------------
            new()
            {
                Id = "kazakh", Name = "Kazakh Khanate", ColorHex = "#2F4F6F",
                Silver = 2500, Gold = 5, Food = 2200, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Turkestan", 15_000, 15, 1, 1628, 448,
                        (1590,420),(1660,415),(1665,470),(1595,475)),
                    P("Kazakh Steppe", 10_000, 12, 2, 1700, 445,
                        (1660,415),(1740,410),(1745,470),(1665,475),(1658,445)),
                }
            },
            // ---------------- Morocco ----------------
            new()
            {
                Id = "morocco", Name = "Morocco", ColorHex = "#7D6608",
                Silver = 3000, Gold = 8, Food = 2500, Wood = 10, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Marrakesh", 20_000, 25, 2, 832, 670,
                        (800,650),(860,645),(865,690),(805,695)),
                    P("Fez", 15_000, 22, 2, 892, 632,
                        (865,620),(915,615),(920,650),(865,645)),
                }
            },
            // ---------------- Denmark-Norway ----------------
            new()
            {
                Id = "denmark", Name = "Denmark-Norway", ColorHex = "#AD1457",
                Silver = 3000, Gold = 8, Food = 2200, Wood = 25, Iron = 8,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Denmark", 18_000, 20, 2, 1036, 316,
                        (1000,298),(1068,294),(1072,332),(1008,338),(998,318)),
                    P("Norway", 12_000, 12, 4, 1038, 210,
                        (1000,120),(1078,115),(1080,295),(1000,298),(995,200)),
                }
            },
            // ---------------- Crimean Khanate ----------------
            new()
            {
                Id = "crimea", Name = "Crimean Khanate", ColorHex = "#00ACC1",
                Silver = 2000, Gold = 5, Food = 2000, Wood = 8, Iron = 3,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Crimea", 12_000, 15, 1, 1436, 502,
                        (1402,480),(1460,475),(1470,520),(1410,530),(1397,505)),
                    P("Azov Steppe", 8_000, 10, 1, 1508, 498,
                        (1470,475),(1540,470),(1545,520),(1475,525)),
                }
            },
            // ---------------- Ethiopia ----------------
            new()
            {
                Id = "ethiopia", Name = "Ethiopia", ColorHex = "#6D4C41",
                Silver = 2500, Gold = 6, Food = 2500, Wood = 12, Iron = 5,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Abyssinia", 16_000, 22, 2, 1438, 830,
                        (1400,800),(1470,795),(1475,860),(1405,865)),
                    P("Tigray", 12_000, 18, 3, 1505, 828,
                        (1475,795),(1530,790),(1535,860),(1475,860)),
                }
            },
            // ---------------- Siam ----------------
            new()
            {
                Id = "siam", Name = "Thailand", ColorHex = "#7CB342",
                Silver = 3000, Gold = 8, Food = 3000, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(3_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Ayutthaya", 18_000, 30, 1, 1850, 719,
                        (1832,700),(1872,697),(1874,740),(1834,743)),
                    P("Tenasserim", 12_000, 22, 2, 1874, 756,
                        (1844,738),(1902,736),(1905,772),(1846,775)),
                }
            },
            // ---------------- Korea ----------------
            new()
            {
                Id = "korea", Name = "Korea", ColorHex = "#BA68C8",
                Silver = 2500, Gold = 6, Food = 2500, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Hanseong", 15_000, 25, 2, 2015, 496,
                        (2001,481),(2026,478),(2028,513),(2003,516)),
                    P("Gyeongsang", 10_000, 20, 2, 2028, 528,
                        (2003,516),(2028,513),(2049,539),(2007,544)),
                }
            },
            // ---------------- Bukhara (Uzbeks) ----------------
            new()
            {
                Id = "bukhara", Name = "Bukhara", ColorHex = "#A1887F",
                Silver = 2500, Gold = 6, Food = 2000, Wood = 8, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Bukhara", 12_000, 15, 2, 1692, 504,
                        (1665,475),(1745,470),(1748,530),(1670,535),(1662,505)),
                    P("Samarkand", 10_000, 14, 3, 1768, 556,
                        (1748,530),(1792,528),(1796,580),(1750,585),(1745,555)),
                }
            },
            // ---------------- Holy Roman Empire ----------------
            new()
            {
                Id = "hre", Name = "Holy Roman Empire", ColorHex = "#212121",
                Silver = 4000, Gold = 10, Food = 3500, Wood = 22, Iron = 12,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Bavaria", 20_000, 28, 3, 1078, 416,
                        (1055,398),(1100,396),(1102,432),(1057,434)),
                    P("Saxony", 15_000, 24, 4, 1126, 413,
                        (1102,396),(1148,394),(1150,430),(1104,432)),
                }
            },
            // ---------------- Scotland ----------------
            new()
            {
                Id = "scotland", Name = "Scotland", ColorHex = "#1B4F72",
                Silver = 2000, Gold = 5, Food = 1800, Wood = 18, Iron = 6,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Edinburgh", 12_000, 15, 2, 882, 240,
                        (848,215),(910,210),(915,265),(850,270)),
                    P("Highlands", 8_000, 8, 4, 877, 180,
                        (845,150),(905,145),(910,210),(848,215)),
                }
            },
            // ---------------- Genoa ----------------
            new()
            {
                Id = "genoa", Name = "Genoa", ColorHex = "#A93226",
                Silver = 4000, Gold = 12, Food = 1500, Wood = 12, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 18,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Genoa", 12_000, 12, 1, 1042, 515,
                        (1020,500),(1062,498),(1064,530),(1022,532)),
                    P("Corsica", 6_000, 10, 1, 1046, 580,
                        (1030,560),(1060,558),(1062,600),(1032,602)),
                }
            },
            // ---------------- Papal States ----------------
            new()
            {
                Id = "papal", Name = "Papal States", ColorHex = "#F9E79F",
                Silver = 3500, Gold = 10, Food = 1800, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Rome", 12_000, 15, 1, 1139, 576,
                        (1118,558),(1156,554),(1158,592),(1120,596)),
                    P("Ancona", 8_000, 12, 1, 1176, 572,
                        (1158,554),(1192,552),(1194,590),(1160,592)),
                }
            },
            // ---------------- Italy ----------------
            new()
            {
                Id = "italy", Name = "Italy", ColorHex = "#7E5109",
                Silver = 3500, Gold = 10, Food = 2200, Wood = 12, Iron = 6,
                Units = UnitCatalog.SeedArmy(3_000), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Piedmont", 14_000, 20, 2, 1077, 472,
                        (1055,455),(1098,453),(1100,490),(1057,492)),
                    P("Tuscany", 11_000, 18, 2, 1134, 522,
                        (1116,505),(1150,503),(1152,540),(1118,542)),
                }
            },
            // ---------------- Croatia ----------------
            new()
            {
                Id = "croatia", Name = "Croatia", ColorHex = "#E59866",
                Silver = 2000, Gold = 5, Food = 1800, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Croatia", 10_000, 14, 2, 1176, 503,
                        (1140,478),(1210,472),(1214,520),(1180,524),(1138,510)),
                    P("Slavonia", 8_000, 12, 1, 1243, 496,
                        (1215,472),(1268,468),(1272,522),(1218,526)),
                }
            },
            // ---------------- Vietnam ----------------
            new()
            {
                Id = "vietnam", Name = "Vietnam", ColorHex = "#148F77",
                Silver = 3000, Gold = 8, Food = 3500, Wood = 18, Iron = 5,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 10,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Tonkin", 20_000, 32, 2, 1936, 680,
                        (1906,650),(1960,647),(1963,710),(1909,713)),
                    P("Annam", 15_000, 28, 2, 1940, 748,
                        (1909,713),(1963,710),(1968,780),(1912,783)),
                }
            },
            // ---------------- Burma ----------------
            new()
            {
                Id = "burma", Name = "Burma", ColorHex = "#6C3483",
                Silver = 3000, Gold = 8, Food = 3500, Wood = 20, Iron = 4,
                Units = UnitCatalog.SeedArmy(4_000), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Ava", 18_000, 28, 2, 1808, 722,
                        (1785,700),(1830,697),(1832,745),(1788,748)),
                    P("Pegu", 14_000, 30, 1, 1812, 778,
                        (1788,748),(1832,745),(1840,810),(1790,812)),
                }
            },
            // ---------------- Ahom Kingdom ----------------
            new()
            {
                Id = "ahom", Name = "Ahom Kingdom", ColorHex = "#1A5276",
                Silver = 2000, Gold = 5, Food = 2200, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Ahom", 12_000, 20, 2, 1827, 676,
                        (1800,655),(1855,652),(1858,695),(1803,698)),
                    P("Kamarupa", 8_000, 16, 2, 1867, 673,
                        (1858,652),(1874,650),(1876,695),(1861,698)),
                }
            },
            // ---------------- Northern Yuan ----------------
            new()
            {
                Id = "yuan", Name = "Northern Yuan", ColorHex = "#935116",
                Silver = 2000, Gold = 5, Food = 2000, Wood = 10, Iron = 3,
                Units = UnitCatalog.SeedArmy(5_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Khalkha", 14_000, 12, 2, 1852, 405,
                        (1800,380),(1900,375),(1905,430),(1805,435)),
                    P("Chahar", 11_000, 10, 2, 1931, 403,
                        (1905,375),(1953,372),(1956,430),(1908,433)),
                }
            },
            // ---------------- Nepal ----------------
            new()
            {
                Id = "nepal", Name = "Nepal", ColorHex = "#CA6F1E",
                Silver = 1800, Gold = 5, Food = 1800, Wood = 14, Iron = 4,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Kathmandu", 9_000, 14, 3, 1726, 656,
                        (1700,640),(1750,638),(1752,672),(1702,674)),
                    P("Pokhara", 6_000, 10, 3, 1774, 655,
                        (1752,638),(1795,636),(1797,672),(1754,674)),
                }
            },
            // ---------------- Kongo ----------------
            new()
            {
                Id = "kongo", Name = "Kongo", ColorHex = "#117A65",
                Silver = 2000, Gold = 5, Food = 2200, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Mbanza Kongo", 12_000, 18, 1, 1036, 857,
                        (1000,830),(1070,828),(1072,885),(1002,888)),
                    P("Loango", 10_000, 15, 1, 1101, 856,
                        (1072,828),(1130,826),(1132,885),(1074,888)),
                }
            },
            // ---------------- Jianzhou Jurchens ----------------
            new()
            {
                Id = "jurchens", Name = "Jianzhou Jurchens", ColorHex = "#4A235A",
                Silver = 2200, Gold = 6, Food = 2000, Wood = 18, Iron = 8,
                Units = UnitCatalog.SeedArmy(4_500), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Hetu Ala", 11_000, 12, 3, 1984, 393,
                        (1960,378),(2005,376),(2008,408),(1962,410)),
                    P("Jianzhou", 9_000, 10, 3, 2030, 392,
                        (2008,376),(2050,374),(2052,408),(2010,410)),
                }
            },
            // ---------------- Cambodia ----------------
            new()
            {
                Id = "cambodia", Name = "Cambodia", ColorHex = "#B7950B",
                Silver = 2500, Gold = 6, Food = 2800, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Angkor", 14_000, 26, 1, 1931, 804,
                        (1905,786),(1955,783),(1958,823),(1908,826)),
                    P("Lovek", 11_000, 22, 1, 1980, 803,
                        (1958,783),(2000,780),(2002,822),(1960,824)),
                }
            },
            // ---------------- Laos ----------------
            new()
            {
                Id = "laos", Name = "Laos", ColorHex = "#884EA0",
                Silver = 1800, Gold = 5, Food = 2000, Wood = 16, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_000), Warships = 0,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Luang Prabang", 9_000, 14, 2, 1890, 680,
                        (1874,660),(1904,657),(1906,700),(1877,703)),
                    P("Vientiane", 7_000, 12, 1, 1892, 720,
                        (1877,703),(1906,700),(1908,734),(1880,737)),
                }
            },
            // ---------------- Malaysia ----------------
            new()
            {
                Id = "malaysia", Name = "Malaysia", ColorHex = "#2874A6",
                Silver = 3000, Gold = 8, Food = 2200, Wood = 18, Iron = 3,
                Units = UnitCatalog.SeedArmy(2_500), Warships = 12,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Malacca", 11_000, 20, 1, 1864, 803,
                        (1846,775),(1880,773),(1882,830),(1848,832)),
                    P("Johor", 9_000, 18, 1, 1895, 802,
                        (1882,773),(1905,772),(1908,830),(1884,832)),
                }
            },
            // ---------------- United Arab Emirates ----------------
            new()
            {
                Id = "uae", Name = "United Arab Emirates", ColorHex = "#D4AC0D",
                Silver = 2500, Gold = 8, Food = 1200, Wood = 8, Iron = 2,
                Units = UnitCatalog.SeedArmy(1_500), Warships = 8,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Julfar", 6_000, 8, 1, 1581, 776,
                        (1560,752),(1600,750),(1602,800),(1562,802)),
                    P("Dibba", 4_000, 6, 1, 1614, 776,
                        (1602,750),(1624,748),(1626,800),(1604,802)),
                }
            },
            // ---------------- Micronesia ----------------
            new()
            {
                Id = "micronesia", Name = "Micronesia", ColorHex = "#85C1E9",
                Silver = 1000, Gold = 3, Food = 800, Wood = 10, Iron = 0,
                Units = UnitCatalog.SeedArmy(500), Warships = 5,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Guam", 3_000, 6, 0, 2169, 603,
                        (2150,588),(2186,586),(2188,618),(2152,620)),
                    P("Palau", 2_000, 5, 0, 2171, 636,
                        (2152,620),(2188,618),(2190,650),(2154,652)),
                }
            },
            // ---------------- Easter Island ----------------
            new()
            {
                Id = "easter", Name = "Easter Island", ColorHex = "#F5CBA7",
                Silver = 800, Gold = 2, Food = 500, Wood = 6, Iron = 0,
                Units = UnitCatalog.SeedArmy(300), Warships = 2,
                NextPayday = new DateOnly(1600, 7, 1),
                Provinces = new List<Province>
                {
                    P("Rapa Nui", 3_000, 5, 0, 2171, 967,
                        (2150,948),(2190,946),(2192,986),(2152,988)),
                }
            },
        };

        foreach (var n in nations)
            n.Population = n.Provinces.Sum(p => p.Population);

        ApplyReligions(nations);

        var player = nations.FirstOrDefault(n => n.Id == playerNationId) ?? nations[0];
        player.IsPlayer = true;
        state.PlayerNation = player;
        state.OtherNations = nations.Where(n => n != player).ToList();

        // Neutral territories: drawn on the map, owned by no crown.
        state.NeutralRegions = new List<Province>
        {
            P("Siberia", 0, 0, 0, 1850, 230,
                (1610,80),(2100,80),(2100,380),(1700,380),(1610,340)),
            P("Northern Scandinavia", 0, 0, 0, 1150, 130,
                (1080,90),(1240,90),(1240,180),(1120,180),(1080,140)),
            P("Barbary Coast", 0, 0, 0, 1119, 670,
                (920,640),(1318,640),(1318,700),(920,700)),
            P("Sahara", 0, 0, 0, 1120, 750,
                (920,700),(1320,700),(1320,800),(920,800)),
            P("West Africa", 0, 0, 0, 860, 830,
                (800,760),(920,760),(920,900),(800,900)),
            P("Central Africa", 0, 0, 0, 1125, 944,
                (920,888),(1330,888),(1330,1000),(920,1000)),
            P("East Africa", 0, 0, 0, 1415, 932,
                (1330,865),(1500,865),(1500,1000),(1330,1000)),
            P("South Africa", 0, 0, 0, 1125, 1050,
                (920,1000),(1330,1000),(1250,1100),(1000,1100)),
            P("Arabia", 0, 0, 0, 1500, 800,
                (1440,730),(1560,726),(1562,835),(1480,870),(1440,800)),
            P("Tibet", 0, 0, 0, 1815, 604,
                (1785,585),(1840,582),(1845,622),(1790,626)),
        };

        // Uncharted frontier regions, open to colonisation.
        state.FrontierRegions = new List<Province>
        {
            P("Western Isles", 0, 0, 0, 640, 560,
                (560,500),(720,490),(740,600),(620,650),(550,590)),
            P("Southern Reaches", 0, 0, 0, 700, 1010,
                (600,950),(800,940),(830,1050),(680,1080),(590,1020)),
            P("Far Eastern Isles", 0, 0, 0, 2120, 708,
                (2075,660),(2150,655),(2175,740),(2110,770),(2072,712)),
        };

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
}
