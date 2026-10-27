using System.Text.Json.Nodes;

namespace StoryShuffle.Tests;

/// <summary>A fake Jellyfin library with realistic episode names.</summary>
public static class Library
{
    public const string DoctorWhoId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a001";
    public const string SouthParkId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a002";
    public const string TestShowId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a003";
    public const string ClassicId = "0a6b52bd1c4f4f43b0c3a1d5f0e1a004";

    public sealed record Ep(string SeriesId, string SeriesName, int Season, int Episode, string Name, string? Overview = null, DateTime? LastPlayed = null)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
    }

    public static readonly List<Ep> All = Build();

    public static IEnumerable<Ep> Series(string id) => All.Where(e => e.SeriesId == id);

    public static JsonObject ToJson(Ep e, bool withOverview) => new()
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
        ["UserData"] = new JsonObject
        {
            ["Played"] = e.LastPlayed is not null,
            ["LastPlayedDate"] = e.LastPlayed?.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"),
        },
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
        return list;
    }
}
