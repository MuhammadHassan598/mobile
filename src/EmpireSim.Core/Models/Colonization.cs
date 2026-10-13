namespace EmpireSim.Core.Models;

/// <summary>Where a colonisable region stands.</summary>
public enum ColonyStatus
{
    /// <summary>Nobody has claimed it: any country that meets the requirements can send a mission.</summary>
    Unclaimed,
    /// <summary>A colonisation mission is on its way. The region is claimed by that country until the mission lands or fails.</summary>
    MissionInProgress,
    /// <summary>A colony was founded here and belongs to a country (see <see cref="FrontierRegion.ColonyId"/>).</summary>
    Colonized,
}

/// <summary>What kind of land a colony region is; it decides how a mission reaches it and what the settlement is called.</summary>
public enum ColonyTerrain
{
    /// <summary>A coastal strip of a continent, reached by sea.</summary>
    Coast,
    /// <summary>An island, reached by sea.</summary>
    Island,
    /// <summary>A river basin inland of an existing realm: reached overland when it touches the colonising country's land.</summary>
    River,
    /// <summary>A group of small islands in open sea (the original frontier regions).</summary>
    Archipelago,
    /// <summary>Inland plateau or uplands with no shore of its own: reached overland from land the country holds, otherwise by sea to the nearest coast and on foot.</summary>
    Highland,
}

/// <summary>How big a region is: bigger land costs more to settle (see <see cref="Services.ColonizationService"/>).</summary>
public enum ColonySize { Small, Medium, Large, Vast }

public enum MissionStatus { InProgress, Completed, Failed }

/// <summary>
/// A colonisation mission: a country's settlers and supplies on their way to a colony region. It is created when the country commits the
/// resources (paid exactly once, recorded here) and ends, on its completion date, as a colony or as a failure. The completion date
/// decides, so a mission that is saved and loaded lands on the same day; it never restarts and never finishes twice.
/// </summary>
public sealed class ColonizationMission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RegionId { get; set; } = "";
    public string RegionName { get; set; } = "";

    /// <summary>The country that sent the mission (and will own the colony).</summary>
    public string OwnerId { get; set; } = "";
    public string OwnerName { get; set; } = "";

    public DateOnly StartDate { get; set; }
    public DateOnly CompletionDate { get; set; }
    public int TotalDays { get; set; }

    // ---- what was committed at the start (and so what the colony is founded with, or what is lost) ----
    public double Gold { get; set; }
    public double Wheat { get; set; }
    public double Wood { get; set; }
    public long Settlers { get; set; }

    /// <summary>Warships the country must still hold when the mission lands (0 for a mission overland).</summary>
    public int WarshipsRequired { get; set; }
    public bool Overland { get; set; }

    public MissionStatus Status { get; set; } = MissionStatus.InProgress;
    public DateOnly? EndDate { get; set; }

    /// <summary>How it ended, in words (empty while it is under way).</summary>
    public string Outcome { get; set; } = "";

    /// <summary>The colony it founded (only for a completed mission).</summary>
    public string? ColonyId { get; set; }

    public int DaysLeft(DateOnly today) => Math.Max(0, CompletionDate.DayNumber - today.DayNumber);

    public double Progress(DateOnly today) =>
        Status != MissionStatus.InProgress ? (Status == MissionStatus.Completed ? 1 : 0)
        : TotalDays <= 0 ? 1 : Math.Clamp(1 - (double)DaysLeft(today) / TotalDays, 0, 1);
}

/// <summary>What is special about one colony region: its land, its settlement and the workings the first settlers find or build.</summary>
public sealed record ColonySite(string Name, string Landmass, ColonyTerrain Terrain, string Blurb, params (string Building, int Count)[] Endowment)
{
    /// <summary>Stable id of the region: lower-case, hyphenated name ("Gold Coast" becomes "gold-coast").</summary>
    public string Id => ColonyCatalog.IdOf(Name);

    public string Icon => Terrain switch
    {
        ColonyTerrain.Coast => "⛵",
        ColonyTerrain.Island => "🏝️",
        ColonyTerrain.River => "🏕️",
        ColonyTerrain.Highland => "⛰️",
        _ => "🌴",
    };

    /// <summary>The settlement the colony is governed from.</summary>
    public string Settlement => Terrain switch
    {
        ColonyTerrain.River => $"{Name} Stockade",
        ColonyTerrain.Highland => $"{Name} Fort",
        ColonyTerrain.Archipelago => $"{Name} Anchorage",
        ColonyTerrain.Island => $"{Name} Harbour",
        _ => $"{Name} Landing",
    };
}

/// <summary>A neutral land that cannot be settled, and why.</summary>
public sealed record UnsettleableLand(string Name, string Kind, string Reason);

/// <summary>The colonisable regions of the map and what they offer. The polygons are the map's (<see cref="WorldGeometry"/>); this is their character.</summary>
public static class ColonyCatalog
{
    public static string IdOf(string regionName) => regionName.ToLowerInvariant().Replace(' ', '-');

    public static readonly IReadOnlyList<ColonySite> Sites = new List<ColonySite>
    {
        // the original open-sea frontier regions
        new("Western Isles", "", ColonyTerrain.Archipelago, "Windswept Atlantic islands beyond the known sea lanes.", ("farm", 2), ("saltmine", 1)),
        new("Southern Reaches", "", ColonyTerrain.Archipelago, "Cold southern islands, rich in seals and salt.", ("farm", 1), ("saltmine", 1)),
        new("Far Eastern Isles", "", ColonyTerrain.Archipelago, "Forested islands of the far Pacific.", ("farm", 2), ("sawmill", 1)),

        // West Africa
        new("Senegambia", "West Africa", ColonyTerrain.Coast, "River mouths and salt pans on Africa's western bulge.", ("farm", 2), ("saltmine", 1)),
        new("Guinea Coast", "West Africa", ColonyTerrain.Coast, "Forested shore with deep harbours.", ("farm", 2), ("sawmill", 1)),
        new("Gold Coast", "West Africa", ColonyTerrain.Coast, "The shore the gold caravans end at.", ("farm", 1), ("goldmine", 1)),
        new("Niger Delta", "West Africa", ColonyTerrain.Coast, "A maze of channels in fertile lowland.", ("farm", 3), ("sawmill", 1)),
        new("Upper Niger", "West Africa", ColonyTerrain.River, "The great bend of the Niger: Sahel markets, rice fields and salt caravans behind the coasts.", ("farm", 2), ("cattlefarm", 1), ("saltmine", 1)),
        // East and South Africa
        new("Horn of Africa", "East Africa", ColonyTerrain.Coast, "Dry coast of herders and incense traders.", ("sheepfarm", 2), ("saltmine", 1)),
        new("Swahili Coast", "East Africa", ColonyTerrain.Coast, "Old trading towns along the monsoon route.", ("farm", 2), ("sawmill", 1), ("stonequarry", 1)),
        new("Mozambique Coast", "East Africa", ColonyTerrain.Coast, "Gold from the interior reaches the sea here.", ("farm", 1), ("goldmine", 1)),
        new("Natal", "South Africa", ColonyTerrain.Coast, "Green hills and grassland above a surf-beaten shore.", ("farm", 2), ("cattlefarm", 1)),
        new("Cape of Good Hope", "South Africa", ColonyTerrain.Coast, "The southern tip: a refreshment station for every fleet.", ("farm", 2), ("cattlefarm", 1), ("sheepfarm", 1)),
        new("Namib Coast", "South Africa", ColonyTerrain.Coast, "Fog-bound desert shore with salt and copper.", ("saltmine", 1), ("copperminer", 1)),
        new("Madagascar North", "South Africa", ColonyTerrain.Island, "The northern half of the great island.", ("farm", 2), ("sawmill", 1)),
        new("Madagascar South", "South Africa", ColonyTerrain.Island, "The drier southern half of the great island.", ("farm", 1), ("cattlefarm", 1)),
        new("Darfur", "East Africa", ColonyTerrain.Highland, "Dry savanna around the Jebel Marra massif: cattle country on the caravan roads to the Nile.", ("cattlefarm", 2), ("farm", 1), ("copperminer", 1)),
        new("Nubia", "East Africa", ColonyTerrain.River, "The Nile above Egypt: irrigated strips between desert and river, with gold in the hills.", ("farm", 2), ("goldmine", 1)),
        new("Great Lakes", "East Africa", ColonyTerrain.Highland, "Fertile lake country and volcanic uplands behind the Swahili coast.", ("farm", 3), ("cattlefarm", 1), ("ironmine", 1)),
        new("Zambezi Plateau", "South Africa", ColonyTerrain.Highland, "The plateau of the Zambezi and the old gold-working kingdoms.", ("farm", 1), ("goldmine", 1), ("copperminer", 1)),
        new("Highveld", "South Africa", ColonyTerrain.Highland, "Open grassland high above the coast: cattle, maize country and iron.", ("cattlefarm", 2), ("farm", 1), ("ironmine", 1)),
        // Arabia
        new("Aden Coast", "Arabia", ColonyTerrain.Coast, "The gate of the Red Sea.", ("farm", 1), ("saltmine", 1)),
        new("Oman Coast", "Arabia", ColonyTerrain.Coast, "Harbours on the road to India.", ("farm", 1), ("copperminer", 1)),
        // East Indies
        new("North Sumatra", "East Indies", ColonyTerrain.Island, "Pepper country and river gold.", ("farm", 2), ("goldmine", 1)),
        new("South Sumatra", "East Indies", ColonyTerrain.Island, "Jungle lowlands behind swamp coasts.", ("farm", 2), ("sawmill", 1)),
        new("Java", "East Indies", ColonyTerrain.Island, "Rich volcanic soil and busy ports.", ("farm", 3)),
        new("Borneo", "East Indies", ColonyTerrain.Island, "Endless rainforest along great rivers.", ("farm", 1), ("sawmill", 2)),
        new("Celebes", "East Indies", ColonyTerrain.Island, "A spidery island of bays and iron hills.", ("farm", 2), ("ironmine", 1)),
        new("Moluccas", "East Indies", ColonyTerrain.Island, "The Spice Islands.", ("farm", 2), ("sawmill", 1)),
        new("Timor", "East Indies", ColonyTerrain.Island, "A chain of dry islands known for sandalwood.", ("farm", 1), ("sawmill", 1)),
        new("West New Guinea", "East Indies", ColonyTerrain.Island, "Mountains and swamps; copper in the hills.", ("farm", 1), ("copperminer", 1)),
        new("East New Guinea", "East Indies", ColonyTerrain.Island, "Highlands, rivers and gold.", ("farm", 1), ("goldmine", 1)),
        // Siberia
        new("Yenisei Valley", "Siberia", ColonyTerrain.River, "Fur country along a great river.", ("furfarm", 2), ("farm", 1), ("ironmine", 1)),
        new("Lena Valley", "Siberia", ColonyTerrain.River, "A frozen basin of trappers and traders.", ("furfarm", 2), ("farm", 1)),
        new("Amur Basin", "Siberia", ColonyTerrain.River, "The mildest valley of the east.", ("farm", 2), ("furfarm", 1)),
        new("Okhotsk Coast", "Siberia", ColonyTerrain.Coast, "Fishing shore on a cold sea.", ("furfarm", 2), ("saltmine", 1)),
        new("Kamchatka", "Siberia", ColonyTerrain.Island, "A volcanic peninsula at the end of the continent.", ("furfarm", 2), ("farm", 1)),
        new("Kolyma Uplands", "Siberia", ColonyTerrain.Highland, "Cold uplands and tundra of the far north-east: furs, and gold in the streams.", ("furfarm", 2), ("goldmine", 1)),
        // Northern islands
        new("Ireland", "", ColonyTerrain.Island, "Green island across the Irish Sea.", ("farm", 3), ("sheepfarm", 1)),
        new("Iceland", "", ColonyTerrain.Island, "Volcanic island of sheep and sagas.", ("sheepfarm", 2), ("saltmine", 1)),
        new("Greenland", "", ColonyTerrain.Island, "A vast ice-bound island; only its coast can be settled.", ("furfarm", 1), ("saltmine", 1)),
    };

    private static readonly Dictionary<string, ColonySite> ByName = Sites.ToDictionary(s => s.Name);

    public static ColonySite? Get(string regionName) => ByName.GetValueOrDefault(regionName);

    /// <summary>
    /// The neutral lands that stay neutral, each with the reason nobody can settle it (shown when the player taps it). Every other piece of
    /// ordinary land is a colony region (<see cref="Sites"/>); a test keeps the two lists in step with the map.
    /// </summary>
    public static readonly IReadOnlyList<UnsettleableLand> Unsettleable = new List<UnsettleableLand>
    {
        new("Sahara", "Desert", "The Sahara is a waterless sea of sand and bare rock. There is no river, well or rain to feed settlers, so no colony here could support itself: only the caravans that cross it, and the oases they use, live off it. Its southern shores (Senegambia, the Niger) and the Nile can be settled."),
        new("Arabia", "Desert", "The interior of Arabia is stony desert and the Empty Quarter: no permanent water, and only camel-herding nomads cross it. Settlers could not feed themselves there. The coasts of Aden and Oman can be settled."),
        new("Tibet", "Plateau", "Tibet is a plateau 4,500 metres high, ringed by the highest mountains in the world. The passes are held by its own monasteries and its harvests cannot feed strangers: there is no road a colony could be supplied by."),
        new("Switzerland", "Protected", "The Swiss cantons are a protected neutral land: a confederacy of valleys that every neighbouring crown has agreed to leave alone. No country may settle it."),
        new("Siberia", "Arctic", "What is left of northern Siberia is Arctic tundra, ice-locked coast and islands, frozen the year round, with no growing season: nothing a settler could plant. The river valleys and coasts to the south are colony regions."),
        new("Svalbard", "Ice", "Svalbard is an ice-covered Arctic archipelago, dark half the year and locked by pack ice, with no soil to farm and no timber: nobody can live off it."),
    };

    /// <summary>Why a neutral land cannot be settled (null for a name that is not a neutral land).</summary>
    public static UnsettleableLand? WhyNot(string neutralName) => Unsettleable.FirstOrDefault(u => u.Name == neutralName);
}
