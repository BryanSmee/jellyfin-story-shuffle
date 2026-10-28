using System.Text.Json.Nodes;

namespace StoryShuffle.Tests;

/// <summary>A fake Jellyfin library with realistic episode names.</summary>
public static class Library
{
    public const string DoctorWhoId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a001";
    public const string SouthParkId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a002";
    public const string TestShowId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a003";
    public const string ClassicId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a004";
    public const string DoctorWhoFrId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a005";
    public const string NewWhoId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a006";
    public const string OldLayoutId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a007";

    /// <summary>
    /// TheTVDB episode ids of the Doctor Who (2005) episodes above, by code (TheTVDB numbers the specials differently).
    /// The French copy of the series carries them, the English one has none (built-in rules then match by title).
    /// </summary>
    public static readonly Dictionary<string, string> DoctorWhoTvdbIds = new()
    {
        ["S00E01"] = "311603", // The Christmas Invasion
        ["S00E02"] = "306978", // The Runaway Bride
        ["S00E03"] = "334734", // Voyage of the Damned
        ["S00E04"] = "373854", // The Next Doctor
        ["S00E05"] = "450701", // Planet of the Dead
        ["S00E06"] = "533011", // The Waters of Mars
        ["S00E07"] = "6101698", // The End of Time (1)
        ["S00E08"] = "1247711", // The End of Time (2)
        ["S01E01"] = "295294", // Rose
        ["S01E02"] = "295295", // The End of the World
        ["S01E03"] = "295296", // The Unquiet Dead
        ["S01E04"] = "295297", // Aliens of London
        ["S01E05"] = "295298", // World War Three
        ["S01E06"] = "295299", // Dalek
        ["S01E07"] = "295300", // The Long Game
        ["S01E08"] = "295301", // Father's Day
        ["S01E09"] = "295305", // The Empty Child
        ["S01E10"] = "295306", // The Doctor Dances
        ["S01E11"] = "295307", // Boom Town
        ["S01E12"] = "295309", // Bad Wolf
        ["S01E13"] = "295311", // The Parting of the Ways
        ["S02E01"] = "302429", // New Earth
        ["S02E02"] = "302430", // Tooth and Claw
        ["S02E03"] = "302431", // School Reunion
        ["S02E04"] = "302432", // The Girl in the Fireplace
        ["S02E05"] = "302785", // Rise of the Cybermen
        ["S02E06"] = "308600", // The Age of Steel
        ["S02E07"] = "302433", // The Idiot's Lantern
        ["S02E08"] = "302434", // The Impossible Planet
        ["S02E09"] = "302435", // The Satan Pit
        ["S02E10"] = "308601", // Love & Monsters
        ["S02E11"] = "303141", // Fear Her
        ["S02E12"] = "302436", // Army of Ghosts
        ["S02E13"] = "302437", // Doomsday
        ["S03E01"] = "315679", // Smith and Jones
        ["S03E02"] = "327371", // The Shakespeare Code
        ["S03E03"] = "327372", // Gridlock
        ["S03E04"] = "327373", // Daleks in Manhattan
        ["S03E05"] = "327374", // Evolution of the Daleks
        ["S03E06"] = "327375", // The Lazarus Experiment
        ["S03E07"] = "327376", // 42
        ["S03E08"] = "327377", // Human Nature
        ["S03E09"] = "327378", // The Family of Blood
        ["S03E10"] = "327379", // Blink
        ["S03E11"] = "327380", // Utopia
        ["S03E12"] = "327381", // The Sound of Drums
        ["S03E13"] = "327382", // Last of the Time Lords
        ["S04E01"] = "357056", // Partners in Crime
        ["S04E02"] = "357054", // The Fires of Pompeii
        ["S04E03"] = "357057", // Planet of the Ood
        ["S04E04"] = "362988", // The Sontaran Stratagem
        ["S04E05"] = "363699", // The Poison Sky
        ["S04E06"] = "363700", // The Doctor's Daughter
        ["S04E07"] = "365136", // The Unicorn and the Wasp
        ["S04E08"] = "365139", // Silence in the Library
        ["S04E09"] = "368694", // Forest of the Dead
        ["S04E10"] = "368695", // Midnight
        ["S04E11"] = "371447", // Turn Left
        ["S04E12"] = "371448", // The Stolen Earth
        ["S04E13"] = "371449", // Journey's End
        ["S09E01"] = "5076329", // The Magician's Apprentice
        ["S09E02"] = "5134440", // The Witch's Familiar
        ["S09E03"] = "5276473", // Under the Lake
        ["S09E04"] = "5276474", // Before the Flood
        ["S09E05"] = "5276475", // The Girl Who Died
        ["S09E06"] = "5276476", // The Woman Who Lived
        ["S09E07"] = "5312609", // The Zygon Invasion
        ["S09E08"] = "5312610", // The Zygon Inversion
        ["S09E09"] = "5330527", // Sleep No More
        ["S09E10"] = "5330528", // Face the Raven
        ["S09E11"] = "5330529", // Heaven Sent
        ["S09E12"] = "5330530", // Hell Bent
        ["S12E01"] = "7461765", // Spyfall, Part 1
        ["S12E02"] = "7461767", // Spyfall, Part 2
        ["S12E03"] = "7477552", // Orphan 55
        ["S12E04"] = "7538135", // Nikola Tesla's Night of Terror
        ["S12E05"] = "7538137", // Fugitive of the Judoon
        ["S12E06"] = "7538138", // Praxeus
        ["S12E07"] = "7558526", // Can You Hear Me?
        ["S12E08"] = "7558528", // The Haunting of Villa Diodati
        ["S12E09"] = "7579416", // Ascension of the Cybermen
        ["S12E10"] = "7579417", // The Timeless Children
        ["S13E01"] = "8766120", // Chapter One: The Halloween Apocalypse
        ["S13E02"] = "8766125", // Chapter Two: War of the Sontarans
        ["S13E03"] = "8766126", // Chapter Three: Once, Upon Time
        ["S13E04"] = "8766127", // Chapter Four: Village of the Angels
        ["S13E05"] = "8766128", // Chapter Five: Survivors of the Flux
        ["S13E06"] = "8766129", // Chapter Six: The Vanquishers
    };

    /// <summary>The series items themselves: name, first year and provider ids, as Jellyfin returns them.</summary>
    public static readonly Dictionary<string, (string Name, int Year, Dictionary<string, string> ProviderIds)> SeriesItems = new()
    {
        [DoctorWhoId] = ("Doctor Who", 2005, new() { ["Tvdb"] = "78804", ["Tmdb"] = "57243", ["Imdb"] = "tt0436992" }),
        [DoctorWhoFrId] = ("Doctor Who", 2005, new() { ["Tvdb"] = "78804", ["Tmdb"] = "57243" }),
        [SouthParkId] = ("South Park", 1997, new() { ["Tvdb"] = "75897", ["Tmdb"] = "2190" }),
        [TestShowId] = ("Test Show (1999)", 1999, new() { ["Tvdb"] = "1" }),
        [ClassicId] = ("Doctor Who (1963)", 1963, new() { ["Tvdb"] = "76107", ["Tmdb"] = "121" }),
        [NewWhoId] = ("Doctor Who (2023)", 2024, new() { ["Tvdb"] = "449991", ["Tmdb"] = "239770" }),
        // Scraped before TVDB split off the 2023 series: the 2024-25 seasons are seasons 14 and 15 of the 2005 series.
        [OldLayoutId] = ("Doctor Who (2005)", 2005, new() { ["Tvdb"] = "78804" }),
    };

    public sealed record Ep(string SeriesId, string SeriesName, int Season, int Episode, string Name, string? Overview = null, DateTime? LastPlayed = null)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public Dictionary<string, string> ProviderIds { get; init; } = new();

        public string Code => $"S{Season:00}E{Episode:00}";
    }

    public static readonly List<Ep> All = Build();

    public static IEnumerable<Ep> Series(string id) => All.Where(e => e.SeriesId == id);

    public static JsonObject ToJson(Ep e, bool withOverview, bool withProviderIds) => new()
    {
        ["Name"] = e.Name,
        ["ServerId"] = "abc",
        ["Id"] = e.Id,
        ["Type"] = "Episode",
        ["IndexNumber"] = e.Episode,
        ["ParentIndexNumber"] = e.Season,
        ["SeriesName"] = e.SeriesName,
        ["SeriesId"] = e.SeriesId,
        ["Overview"] = withOverview ? e.Overview : null,
        ["ProviderIds"] = withProviderIds ? new JsonObject(e.ProviderIds.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))) : null,
        ["UserData"] = new JsonObject
        {
            ["Played"] = e.LastPlayed is not null,
            ["LastPlayedDate"] = e.LastPlayed?.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
        },
    };

    public static JsonObject SeriesToJson(string id) => new()
    {
        ["Name"] = SeriesItems[id].Name,
        ["ServerId"] = "abc",
        ["Id"] = id,
        ["Type"] = "Series",
        ["ProductionYear"] = SeriesItems[id].Year,
        ["ProviderIds"] = new JsonObject(SeriesItems[id].ProviderIds.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
    };

    private static List<Ep> Build()
    {
        var list = new List<Ep>();
        void Season(string id, string name, int s, params string[] titles)
        {
            for (var i = 0; i < titles.Length; i++)
            {
                list.Add(new Ep(id, name, s, i + 1, titles[i]));
            }
        }

        const string dw = "Doctor Who";
        Season(DoctorWhoId, dw, 0, "The Christmas Invasion", "The Runaway Bride", "Voyage of the Damned", "The Next Doctor",
            "Planet of the Dead", "The Waters of Mars", "The End of Time (1)", "The End of Time (2)");
        Season(DoctorWhoId, dw, 1, "Rose", "The End of the World", "The Unquiet Dead", "Aliens of London", "World War Three",
            "Dalek", "The Long Game", "Father's Day", "The Empty Child", "The Doctor Dances", "Boom Town", "Bad Wolf", "The Parting of the Ways");
        Season(DoctorWhoId, dw, 2, "New Earth", "Tooth and Claw", "School Reunion", "The Girl in the Fireplace", "Rise of the Cybermen",
            "The Age of Steel", "The Idiot's Lantern", "The Impossible Planet", "The Satan Pit", "Love & Monsters", "Fear Her", "Army of Ghosts", "Doomsday");
        Season(DoctorWhoId, dw, 3, "Smith and Jones", "The Shakespeare Code", "Gridlock", "Daleks in Manhattan", "Evolution of the Daleks",
            "The Lazarus Experiment", "42", "Human Nature", "The Family of Blood", "Blink", "Utopia", "The Sound of Drums", "Last of the Time Lords");
        Season(DoctorWhoId, dw, 4, "Partners in Crime", "The Fires of Pompeii", "Planet of the Ood", "The Sontaran Stratagem", "The Poison Sky",
            "The Doctor's Daughter", "The Unicorn and the Wasp", "Silence in the Library", "Forest of the Dead", "Midnight", "Turn Left",
            "The Stolen Earth", "Journey's End");
        Season(DoctorWhoId, dw, 9, "The Magician's Apprentice", "The Witch's Familiar", "Under the Lake", "Before the Flood", "The Girl Who Died",
            "The Woman Who Lived", "The Zygon Invasion", "The Zygon Inversion", "Sleep No More", "Face the Raven", "Heaven Sent", "Hell Bent");
        Season(DoctorWhoId, dw, 12, "Spyfall, Part 1", "Spyfall, Part 2", "Orphan 55", "Nikola Tesla's Night of Terror", "Fugitive of the Judoon",
            "Praxeus", "Can You Hear Me?", "The Haunting of Villa Diodati", "Ascension of the Cybermen", "The Timeless Children");
        Season(DoctorWhoId, dw, 13, "Chapter One: The Halloween Apocalypse", "Chapter Two: War of the Sontarans", "Chapter Three: Once, Upon Time",
            "Chapter Four: Village of the Angels", "Chapter Five: Survivors of the Flux", "Chapter Six: The Vanquishers");

        // The same series as a server with French metadata sees it: every title translated, provider ids present.
        foreach (var e in list.Where(e => e.SeriesId == DoctorWhoId).ToList())
        {
            var ids = new Dictionary<string, string> { ["Tvdb"] = DoctorWhoTvdbIds[e.Code] };
            list.Add(new Ep(DoctorWhoFrId, dw, e.Season, e.Episode, FrenchTitle(e)) { ProviderIds = ids });
        }

        const string sp = "South Park";
        Season(SouthParkId, sp, 1, "Cartman Gets an Anal Probe", "Weight Gain 4000", "Volcano", "Big Gay Al's Big Gay Boat Ride",
            "An Elephant Makes Love to a Pig", "Death", "Pinkeye", "Starvin' Marvin", "Mr. Hankey, the Christmas Poo", "Damien",
            "Tom's Rhinoplasty", "Mecha-Streisand", "Cartman's Mom Is a Dirty Slut");
        Season(SouthParkId, sp, 2, "Terrance and Phillip in Not Without My Anus", "Cartman's Mom Is Still a Dirty Slut", "Ike's Wee Wee");
        Season(SouthParkId, sp, 10, "The Return of Chef", "Smug Alert!", "Cartoon Wars Part I", "Cartoon Wars Part II", "A Million Little Fibers",
            "ManBearPig", "Tsst", "Make Love, Not Warcraft", "Mystery of the Urinal Deuce", "Miss Teacher Bangs a Boy", "Hell on Earth 2006",
            "Go God Go", "Go God Go XII", "Stanley's Cup");
        Season(SouthParkId, sp, 11, "With Apologies to Jesse Jackson", "Cartman Sucks", "Lice Capades", "The Snuke", "Fantastic Easter Special",
            "D-Yikes!", "Night of the Living Homeless", "Le Petit Tourette", "More Crap", "Imaginationland, Episode I", "Imaginationland, Episode II",
            "Imaginationland, Episode III", "Guitar Queer-O", "The List");
        Season(SouthParkId, sp, 12, "Tonsil Trouble", "Britney's New Look", "Major Boobage", "Canada on Strike", "Eek, a Penis!", "Over Logging",
            "Super Fun Time", "The China Probrem", "Breast Cancer Show Ever", "Pandemic", "Pandemic 2: The Startling", "About Last Night...",
            "Elementary School Musical", "The Ungroundable");
        Season(SouthParkId, sp, 24, "The Pandemic Special", "South ParQ Vaccination Special");

        const string ts = "Test Show (1999)";
        list.Add(new Ep(TestShowId, ts, 1, 1, "Pilot"));
        list.Add(new Ep(TestShowId, ts, 1, 2, "The Heist", "The crew plans a heist. Part one of two."));
        list.Add(new Ep(TestShowId, ts, 1, 3, "The Getaway", "Part two of two. The crew escapes."));
        list.Add(new Ep(TestShowId, ts, 1, 4, "Standalone", "Stan concludes that the rumours are false."));
        list.Add(new Ep(TestShowId, ts, 1, 5, "Storm Front", "A storm rolls in. To be continued..."));
        list.Add(new Ep(TestShowId, ts, 1, 6, "Eye of the Storm", "The storm hits town."));
        list.Add(new Ep(TestShowId, ts, 1, 7, "Downtime", "A quiet day.", DateTime.UtcNow.AddDays(-2)));
        list.Add(new Ep(TestShowId, ts, 1, 8, "Best of Both Worlds (1)"));
        list.Add(new Ep(TestShowId, ts, 2, 1, "Best of Both Worlds (2)"));
        list.Add(new Ep(TestShowId, ts, 2, 2, "Reunion"));
        list.Add(new Ep(TestShowId, ts, 2, 3, "Old Friends"));
        list.Add(new Ep(TestShowId, ts, 2, 4, "New Enemies"));

        const string cl = "Doctor Who (1963)";
        Season(ClassicId, cl, 7, "Spearhead from Space (1)", "Spearhead from Space (2)", "Spearhead from Space (3)", "Spearhead from Space (4)",
            "Doctor Who and the Silurians (1)", "Doctor Who and the Silurians (2)", "Doctor Who and the Silurians (3)",
            "Doctor Who and the Silurians (4)", "Doctor Who and the Silurians (5)", "Doctor Who and the Silurians (6)",
            "Doctor Who and the Silurians (7)", "The Ambassadors of Death (1)", "The Ambassadors of Death (2)");

        const string nw = "Doctor Who (2023)";
        Season(NewWhoId, nw, 1, "Space Babies", "The Devil's Chord", "Boom", "73 Yards", "Dot and Bubble", "Rogue",
            "The Legend of Ruby Sunday", "Empire of Death");
        Season(NewWhoId, nw, 2, "The Robot Revolution", "Lux", "The Well", "Lucky Day", "The Story & the Engine", "The Interstellar Song Contest",
            "Wish World", "The Reality War");

        const string ol = "Doctor Who (2005)";
        Season(OldLayoutId, ol, 14, "Space Babies", "The Devil's Chord", "Boom", "73 Yards", "Dot and Bubble", "Rogue",
            "The Legend of Ruby Sunday", "Empire of Death");
        Season(OldLayoutId, ol, 15, "The Robot Revolution", "Lux", "The Well", "Lucky Day", "The Story & the Engine", "The Interstellar Song Contest",
            "Wish World", "The Reality War");
        return list;
    }

    /// <summary>A title that shares nothing with the English one, so only provider ids can link it.</summary>
    public static string FrenchTitle(Ep english) => $"Épisode {english.Episode} de la saison {english.Season}";
}
