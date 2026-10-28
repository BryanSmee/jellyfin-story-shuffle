using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.StoryShuffle;
using Jellyfin.Plugin.StoryShuffle.Configuration;
using Jellyfin.Plugin.StoryShuffle.Grouping;
using Jellyfin.Plugin.StoryShuffle.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using StoryShuffle.Release;
using StoryShuffle.Tests;
using static StoryShuffle.Tests.Library;

var failures = new List<string>();
var passes = 0;
void Check(bool ok, string what)
{
    if (ok) { passes++; } else { failures.Add(what); Console.WriteLine("FAIL: " + what); }
}

// ---------------------------------------------------------------- plugin loads against real 12.4 types
var tmp = Directory.CreateTempSubdirectory("storyshuffle").FullName;
var paths = DispatchProxyPaths.Create(tmp);
var plugin = new Plugin(paths, new XmlFileSerializer());
Check(plugin.Name == "Story Shuffle", "plugin name");
Check(plugin.Id == Guid.Parse("1f90710b-d628-40f1-8aa9-614cba01e68e"), "plugin id");
Check(plugin.Configuration.Enabled && plugin.Configuration.RecentDays == 14, "default configuration loads through real BasePlugin<T>");
var page = ((IHasWebPages)plugin).GetPages().Single();
using (var s = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath))
{
    Check(s is not null && s.Length > 1000, "config page embedded at " + page.EmbeddedResourcePath);
}

var cfg = plugin.Configuration;
cfg.RulesJson = """
[
  // comments and trailing commas are fine
  { "series": ["Test Show"], "groups": [["S02E02", "S02E04"]], "exclude": ["Old Friends"], },
]
""";
plugin.UpdateConfiguration(cfg); // real Jellyfin save path (XML)
Check(File.Exists(plugin.ConfigurationFilePath) && File.ReadAllText(plugin.ConfigurationFilePath).Contains("Old Friends"), "configuration saved as XML by real BasePlugin");
Check(plugin.RulesError is null && plugin.GetRuleSet() is not null && plugin.RulesError is null, "user rules parse");

// ---------------------------------------------------------------- rule entries by provider id
var builtIn = JsonNode.Parse(Plugin.BuiltInJson)!.AsArray(); // strict JSON: the files' comments are dropped
Check(builtIn.Count == 3 && builtIn.All(r => r!["groups"]!.AsArray().Count > 0), "every file under BuiltInRules/ is loaded, served as strict JSON");
{
    EpisodeInfo E(int n, string name, params (string Provider, string Id)[] ids) => new()
    {
        Index = n, Id = "e" + n, Name = name, Season = 1, Episode = n,
        ProviderIds = ids.ToDictionary(i => i.Provider, i => i.Id, StringComparer.OrdinalIgnoreCase),
    };
    var eps = new List<EpisodeInfo>
    {
        E(1, "L'Enfant vide", ("Tvdb", "101"), ("Tmdb", "901")),
        E(2, "Le Docteur danse", ("TMDB", "902")),
        E(3, "Sans identifiant"),
        E(4, "La Fin", ("Tvdb", "104")),
        E(5, "Encore", ("Tvdb", "105")),
        E(6, "Ailleurs", ("Imdb", "tt106")),
        E(7, "À exclure", ("Tvdb", "107")),
    };
    List<List<string>> Run(ResolvedRule r, List<string> unresolved)
        => StoryGrouper.Group(eps, r, unresolved).Where(s => s.Episodes.Count > 1).Select(s => s.Episodes.Select(e => e.Id).ToList()).ToList();
    var rule = new ResolvedRule();
    rule.Groups.Add(new() { "tvdb:999 | tmdb:901 | The Empty Child", "tvdb:102 | TMDB:902 | The Doctor Dances" }); // tmdb only, any case
    rule.Groups.Add(new() { "tvdb:104", "Sans identifiant" });                                                    // id, then a title
    rule.Groups.Add(new() { "tvdb:105", "imdb:tt106", "tvdb:404 | Not There" });                               // any provider; one missing
    rule.Exclude.Add("tvdb:107");
    var unresolved = new List<string>();
    var got = Run(rule, unresolved);
    Check(got.Count == 3 && got[0].SequenceEqual(new[] { "e1", "e2" }), "entries match on whichever provider id the episode has: " + string.Join(" ; ", got.Select(g => string.Join("/", g))));
    Check(got.Count == 3 && got[1].SequenceEqual(new[] { "e4", "e3" }) && got[2].SequenceEqual(new[] { "e5", "e6" }), "provider ids and titles mix in one story, in rule order");
    Check(unresolved.SequenceEqual(new[] { "tvdb:404 | Not There" }), "an entry whose ids and title are all missing is unresolved: " + string.Join(", ", unresolved));
    Check(StoryGrouper.Group(eps, rule).All(s => s.Episodes.All(e => e.Id != "e7")), "exclude by provider id");

    var parts = new List<EpisodeInfo> { E(1, "Le Casse (1)", ("Tvdb", "201")), E(2, "Le Casse (2)", ("Tvdb", "202")) };
    var heuristic = new ResolvedRule { TitleHeuristic = true };
    Check(StoryGrouper.Group(parts, heuristic).Count == 1, "\"(1) / (2)\" titles link by default");
    heuristic.NeverGroup.Add("tvdb:202");
    Check(StoryGrouper.Group(parts, heuristic).Count == 2, "neverGroup by provider id keeps the episode alone");

    // Part markers and overview phrases in other languages.
    bool Linked(params string[] titles)
        => StoryGrouper.Group(titles.Select((t, i) => E(i + 1, t)).ToList(), new ResolvedRule { TitleHeuristic = true }).Count == 1;
    foreach (var (language, a, b) in new[]
    {
        ("French", "Le Jour du Docteur (Partie 1)", "Le Jour du Docteur (Partie 2)"),
        ("French", "L'Invasion – Épisode un", "L'Invasion – Épisode deux"),
        ("German", "Die Flut – Teil eins", "Die Flut – Teil zwei"),
        ("German", "Der Plan, Folge 4", "Der Plan, Folge fünf"),
        ("Spanish", "El final, parte 1", "El final, parte 2"),
        ("Spanish", "La boda (Capítulo uno)", "La boda (Capítulo dos)"),
        ("Italian", "La fuga - parte uno", "La fuga - parte due"),
        ("Dutch", "De storm: deel een", "De storm: deel twee"),
        ("Dutch", "Het eind, aflevering één", "Het eind, aflevering twee"),
        ("Portuguese", "O fim (Parte um de dois)", "O fim (Parte dois de dois)"),
        ("Portuguese", "A queda – capítulo 1", "A queda – capítulo 2"),
    })
    {
        Check(Linked(a, b), $"{language} titles \"{a}\" / \"{b}\" link");
    }

    Check(Linked("Le Plan : Partie 1 - Le début", "Le Plan : Partie 2 - La fin"), "French \"Partie N - subtitle\" in the middle of a title links");
    Check(!Linked("Uno", "Dos") && !Linked("Un jour", "Deux jours") && !Linked("Le Casse (Un)", "Le Casse (Deux)"),
        "number words in other languages alone link nothing: \"Uno\", \"Un jour\", \"(Un)\"");
    Check(!Linked("Partie de chasse 1", "Partie de chasse 2") && !Linked("Parti 1", "Parti 2"), "\"part\" only counts as a whole word");
    Check(TitleTools.StripAndNormalize("Chapitre un : La Fin") == TitleTools.Normalize("La Fin")
        && TitleTools.StripAndNormalize("Kapitel 3: Das Ende") == TitleTools.Normalize("Das Ende"), "a leading \"Chapitre un :\" is stripped for matching");
    Check(TitleTools.Languages.All(l => l.Numbers.Select((words, i) => words.Split('|').All(w => TitleTools.ParseNumber(w) == i + 1)).All(x => x)),
        "every number word of every language parses to its own number");

    Check(StoryGrouper.OverviewLinks("Le Docteur découvre la vérité. À suivre…", "Rien à voir."), "\"À suivre…\" links to the next episode");
    Check(StoryGrouper.OverviewLinks("Die Daleks greifen an. Fortsetzung folgt.", null), "\"Fortsetzung folgt\" links to the next episode");
    Check(StoryGrouper.OverviewLinks(null, "Suite et fin de l'aventure commencée la semaine dernière."), "\"Suite et fin de…\" links to the previous episode");
    Check(StoryGrouper.OverviewLinks("¿Escapará el Doctor? Continuará...", null) && StoryGrouper.OverviewLinks("De Doctor zit vast. Wordt vervolgd.", null)
        && StoryGrouper.OverviewLinks("Il Dottore è in trappola. Continua...", null) && StoryGrouper.OverviewLinks("O Doutor está preso. (Continua)", null),
        "Spanish, Dutch, Italian and Portuguese \"to be continued\" link forward");
    Check(StoryGrouper.OverviewLinks(null, "Seconda parte: il Dottore torna.") && StoryGrouper.OverviewLinks(null, "Zweiter Teil des Abenteuers.")
        && StoryGrouper.OverviewLinks(null, "Der zweite Teil des Abenteuers.") && StoryGrouper.OverviewLinks(null, "Het tweede deel van het verhaal.")
        && StoryGrouper.OverviewLinks(null, "Segunda parte de la historia."), "\"second part\" openings link back");
    Check(StoryGrouper.OverviewLinks(null, "Teil 2 von 2: Das Ende.") && StoryGrouper.OverviewLinks("Partie 1 sur 2 : le début.", null)
        && !StoryGrouper.OverviewLinks(null, "En la parte 2 de una serie de misterios."), "\"Teil 2 von 2\" links, \"parte 2 de una serie\" doesn't");
    Check(!StoryGrouper.OverviewLinks("Le Docteur commence à suivre un homme dans Londres.", "Une nouvelle aventure.")
        && !StoryGrouper.OverviewLinks("La storia continua in un nuovo pianeta.", null), "\"à suivre\" and \"continua\" inside a sentence link nothing");

    var byTitle = new ResolvedRule();
    byTitle.Groups.Add(new() { "tvdb:555 | L'Enfant vide", "S01E02" });
    Check(Run(byTitle, new()).SingleOrDefault()?.SequenceEqual(new[] { "e1", "e2" }) == true, "an unknown id falls back to the entry's title, codes still work");

    Check(builtIn.All(r => r!["groups"]!.AsArray().SelectMany(g => g!.AsArray()).All(x =>
        System.Text.RegularExpressions.Regex.IsMatch(x!.GetValue<string>(), @"^tvdb:\d+ \| \S"))),
        "every built-in entry is a TVDB id followed by its English title");
    Check(builtIn.All(r => r!["series"]!.AsArray().All(x =>
        System.Text.RegularExpressions.Regex.IsMatch(x!.GetValue<string>(), @"^(tvdb|tmdb|imdb):\S+$"))),
        "every built-in list names its series by provider id only, never by name");
}

// ---------------------------------------------------------------- fake Jellyfin server with the plugin's middleware
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddResponseCompression();
new ServiceRegistrator().RegisterServices(builder.Services, null!);
var app = builder.Build();
app.UseResponseCompression();
string? lastQuery = null;
var seriesLookups = 0;
var failSeriesLookup = false;
var lookupPrefix = ""; // the base URL the plugin's series lookup must keep
string? lastLookupUserId = null;
app.MapGet("/{**rest}", (HttpContext ctx, string? rest) =>
{
    if (!ctx.Request.Query.ContainsKey("ids")) { lastQuery = ctx.Request.QueryString.Value; }
    var q = ctx.Request.Query;
    if (!ctx.Request.Headers.ContainsKey("Authorization")) { return Results.Unauthorized(); }
    var path = "/" + rest;
    List<Library.Ep> eps;
    var m = System.Text.RegularExpressions.Regex.Match(path, @"Shows/([0-9a-f]+)/Episodes$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success)
    {
        eps = Series(m.Groups[1].Value).ToList();
    }
    else if (path.EndsWith("/Items", StringComparison.OrdinalIgnoreCase) && q.ContainsKey("ids"))
    {
        // The plugin's series lookup: series items with their provider ids. Doesn't count as the "last query".
        seriesLookups++;
        lastLookupUserId = q["userId"].FirstOrDefault();
        if (failSeriesLookup) { return Results.StatusCode(500); }
        if (!string.Equals(path, lookupPrefix + "/Items", StringComparison.OrdinalIgnoreCase)) { return Results.NotFound(); }
        var found = q["ids"].ToString().Split(',').Where(SeriesItems.ContainsKey).Select(id => (JsonNode?)SeriesToJson(id)).ToArray();
        ctx.Response.Headers["X-Series-Lookup"] = "1";
        return Results.Content(new JsonObject { ["Items"] = new JsonArray(found), ["TotalRecordCount"] = found.Length }.ToJsonString(), "application/json; charset=utf-8");
    }
    else if (path.EndsWith("/Items", StringComparison.OrdinalIgnoreCase))
    {
        var types = q["includeItemTypes"].ToString();
        eps = types.Contains("Episode", StringComparison.OrdinalIgnoreCase) ? Series(q["parentId"].ToString()).ToList() : new();
    }
    else
    {
        return Results.NotFound();
    }

    var sort = q["sortBy"].ToString();
    if (sort.Contains("Random", StringComparison.OrdinalIgnoreCase)) { eps = eps.OrderBy(_ => Random.Shared.Next()).ToList(); }
    else { eps = eps.OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList(); }
    var total = eps.Count;
    if (int.TryParse(q["startIndex"], out var si)) { eps = eps.Skip(si).ToList(); }
    if (int.TryParse(q["limit"], out var lim)) { eps = eps.Take(lim).ToList(); }
    var withOverview = q["fields"].ToString().Contains("Overview", StringComparison.OrdinalIgnoreCase);
    var withProviderIds = q["fields"].ToString().Contains("ProviderIds", StringComparison.OrdinalIgnoreCase);
    var body = new JsonObject
    {
        ["Items"] = new JsonArray(eps.Select(e => (JsonNode?)Library.ToJson(e, withOverview, withProviderIds)).ToArray()),
        ["TotalRecordCount"] = total,
        ["StartIndex"] = si,
    };
    return Results.Content(body.ToJsonString(), "application/json; charset=utf-8");
});
await app.StartAsync();
var baseUrl = app.Urls.First();

var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { BaseAddress = new Uri(baseUrl) };
http.DefaultRequestHeaders.Add("Authorization", "MediaBrowser Token=\"x\"");
http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br");

async Task<List<string>> Names(string url)
{
    var json = await http.GetFromJsonAsync<JsonObject>(url);
    return json!["Items"]!.AsArray().Select(i => i!["Name"]!.GetValue<string>()).ToList();
}

// Expected stories: every part present must appear complete, consecutive, in order.
var dwExpected = new List<List<string>>
{
    new() { "Aliens of London", "World War Three" }, new() { "The Empty Child", "The Doctor Dances" }, new() { "Bad Wolf", "The Parting of the Ways" },
    new() { "Rise of the Cybermen", "The Age of Steel" }, new() { "The Impossible Planet", "The Satan Pit" }, new() { "Army of Ghosts", "Doomsday" },
    new() { "Daleks in Manhattan", "Evolution of the Daleks" }, new() { "Human Nature", "The Family of Blood" },
    new() { "Utopia", "The Sound of Drums", "Last of the Time Lords" },
    new() { "The Sontaran Stratagem", "The Poison Sky" }, new() { "Silence in the Library", "Forest of the Dead" }, new() { "The Stolen Earth", "Journey's End" },
    new() { "The End of Time (1)", "The End of Time (2)" },
    new() { "The Magician's Apprentice", "The Witch's Familiar" }, new() { "Under the Lake", "Before the Flood" },
    new() { "The Zygon Invasion", "The Zygon Inversion" }, new() { "Face the Raven", "Heaven Sent", "Hell Bent" },
    new() { "Spyfall, Part 1", "Spyfall, Part 2" }, new() { "Ascension of the Cybermen", "The Timeless Children" },
    new()
    {
        "Chapter One: The Halloween Apocalypse", "Chapter Two: War of the Sontarans", "Chapter Three: Once, Upon Time",
        "Chapter Four: Village of the Angels", "Chapter Five: Survivors of the Flux", "Chapter Six: The Vanquishers",
    },
};
var spExpected = new List<List<string>>
{
    new() { "Cartman's Mom Is a Dirty Slut", "Cartman's Mom Is Still a Dirty Slut" },
    new() { "Cartoon Wars Part I", "Cartoon Wars Part II" }, new() { "Go God Go", "Go God Go XII" },
    new() { "Imaginationland, Episode I", "Imaginationland, Episode II", "Imaginationland, Episode III" },
    new() { "Pandemic", "Pandemic 2: The Startling" },
};
var newWhoExpected = new List<List<string>>
{
    new() { "The Legend of Ruby Sunday", "Empire of Death" }, new() { "Wish World", "The Reality War" },
};
var tsExpected = new List<List<string>>
{
    new() { "The Heist", "The Getaway" }, new() { "Storm Front", "Eye of the Storm" },
    new() { "Best of Both Worlds (1)", "Best of Both Worlds (2)" }, new() { "Reunion", "New Enemies" },
};
var classicExpected = new List<List<string>>
{
    new() { "Spearhead from Space (1)", "Spearhead from Space (2)", "Spearhead from Space (3)", "Spearhead from Space (4)" },
    Enumerable.Range(1, 7).Select(i => $"Doctor Who and the Silurians ({i})").ToList(),
    new() { "The Ambassadors of Death (1)", "The Ambassadors of Death (2)" },
};

// Builds every story the queue should contain: expected groups plus everything else as singles.
string StoriesBroken(List<string> queue, List<List<string>> expected)
{
    var linked = expected.SelectMany(g => g).ToHashSet();
    foreach (var g in expected)
    {
        var first = queue.IndexOf(g[0]);
        var anyPresent = g.Any(queue.Contains);
        if (!anyPresent) { continue; }
        if (first < 0 || first + g.Count > queue.Count || !queue.Skip(first).Take(g.Count).SequenceEqual(g))
        {
            return "broken story: " + string.Join(" / ", g);
        }
    }

    // Nothing else should have been glued on: count stories by walking the queue.
    if (queue.Distinct().Count() != queue.Count) { return "duplicate episodes"; }
    return string.Empty;
}

async Task ShuffleSeries(string label, string seriesId, List<List<string>> expected, int episodeCount, int runs = 60, int minOpeners = 5)
{
    var firsts = new HashSet<string>();
    for (var run = 0; run < runs; run++)
    {
        var url = $"/Shows/{seriesId}/Episodes?IsVirtualUnaired=false&IsMissing=false&limit=100&SortBy=Random&UserId=u&Fields=Chapters,MediaSources,Trickplay";
        var queue = await Names(url);
        firsts.Add(queue[0]);
        var problem = StoriesBroken(queue, expected);
        if (problem.Length > 0) { Check(false, $"{label} run {run}: {problem}\n  {string.Join(" | ", queue)}"); return; }
        if (run == 0)
        {
            Check(queue.Count == Math.Min(episodeCount, 100), $"{label}: queue has {queue.Count} items, expected {Math.Min(episodeCount, 100)}");
            Check(lastQuery is not null && !lastQuery.Contains("Random", StringComparison.OrdinalIgnoreCase) && !lastQuery.Contains("limit", StringComparison.OrdinalIgnoreCase)
                  && lastQuery.Contains("Overview", StringComparison.OrdinalIgnoreCase) && lastQuery.Contains("ProviderIds", StringComparison.OrdinalIgnoreCase)
                  && lastQuery.Contains("Trickplay", StringComparison.OrdinalIgnoreCase)
                  && lastQuery.Contains("IsMissing=false", StringComparison.OrdinalIgnoreCase), $"{label}: rewritten query {lastQuery}");
        }
    }

    Check(firsts.Count >= Math.Min(5, minOpeners), $"{label}: order is actually random ({firsts.Count} different openers in {runs} runs)");
    Console.WriteLine($"ok  {label}: {runs} shuffles, all stories intact");
}

await ShuffleSeries("Doctor Who", DoctorWhoId, dwExpected, Series(DoctorWhoId).Count());
// French metadata: no English title left and the specials numbered differently, the built-in stories link through the provider ids.
string French(string english) => FrenchTitle(Series(DoctorWhoId).Single(e => e.Name == english));
var dwFrExpected = dwExpected.Select(g => g.Select(French).ToList()).ToList();
await ShuffleSeries("Doctor Who, French titles", DoctorWhoFrId, dwFrExpected, Series(DoctorWhoFrId).Count());
await ShuffleSeries("South Park", SouthParkId, spExpected, Series(SouthParkId).Count());
await ShuffleSeries("Classic Doctor Who (title heuristic)", ClassicId, classicExpected, Series(ClassicId).Count(), minOpeners: 3);
await ShuffleSeries("Doctor Who (2023)", NewWhoId, newWhoExpected, Series(NewWhoId).Count());
await ShuffleSeries("Doctor Who (2005) with the 2024-25 seasons (old TVDB layout)", OldLayoutId, newWhoExpected, Series(OldLayoutId).Count());
await ShuffleSeries("Test Show (overview + user rule)", TestShowId, tsExpected, Series(TestShowId).Count() - 1);

// No false links in Test Show; excluded episode gone; recently watched pushed back.
{
    var q = await Names($"/Shows/{TestShowId}/Episodes?SortBy=Random&limit=100");
    Check(!q.Contains("Old Friends"), "excluded episode not queued");
    Check(q.Last() == "Downtime", $"recently watched story goes last (got {q.Last()})");
}

// DW: nothing outside the expected stories got glued together.
{
    var res = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={DoctorWhoId}");
    var stories = res!["stories"]!.AsArray().Select(s => s!["episodes"]!.AsArray().Select(e => e!.GetValue<string>()[8..]).ToList()).ToList();
    Check(stories.Count == dwExpected.Count, $"DW preview finds {stories.Count} stories, expected {dwExpected.Count}: "
        + string.Join(" ; ", stories.Select(s => string.Join("/", s))));
    Check(stories.All(s => dwExpected.Any(e => e.SequenceEqual(s))), "DW preview stories match exactly");
    Console.WriteLine($"ok  preview: {res["episodeCount"]} episodes -> {res["storyCount"]} units; unresolved rule entries: {res["unresolvedRuleEntries"]!.AsArray().Count}");

    var fr = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={DoctorWhoFrId}");
    var frStories = fr!["stories"]!.AsArray().Select(s => s!["episodes"]!.AsArray().Select(e => e!.GetValue<string>()[8..]).ToList()).ToList();
    Check(frStories.Count == dwFrExpected.Count && frStories.All(s => dwFrExpected.Any(e => e.SequenceEqual(s))),
        $"French DW preview finds the same stories as the English one: {frStories.Count} of {dwFrExpected.Count}");
    Check(Rules(fr) == Rules(res) && fr["unresolvedRuleEntries"]!.AsArray().Count == res["unresolvedRuleEntries"]!.AsArray().Count,
        $"French DW preview uses the same rule as the English one, with as many unresolved entries ({fr["unresolvedRuleEntries"]!.AsArray().Count})");
    var frSpecial = Series(DoctorWhoFrId).Single(e => e.ProviderIds["Tvdb"] == "306978");
    Check(frSpecial.Code == "S00E04" && Series(DoctorWhoId).Single(e => e.Name == "The Runaway Bride").Code == "S00E02",
        "French copy numbers the specials like the maintainer's server");

    var sp = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={SouthParkId}");
    var spStories = sp!["stories"]!.AsArray().Count;
    Check(spStories == spExpected.Count, $"SP preview finds {spStories} stories, expected {spExpected.Count}");

    var ts = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={TestShowId}");
    Check(ts!["stories"]!.AsArray().Count == tsExpected.Count, "Test Show preview: " + ts["stories"]!.ToJsonString());
    Check(ts["rules"]!.AsArray().Count == 1, "Test Show matched user rule despite (1999) in the name");

    // Built-in rules go by provider id, so each Doctor Who series only gets its own list.
    string Rules(JsonObject? r) => string.Join(", ", r!["rules"]!.AsArray().Select(x => x!.GetValue<string>()));
    Check(Rules(res) == "Doctor Who (2005-) stories", "DW 2005 preview uses only the 2005 rule: " + Rules(res));
    var classic = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={ClassicId}");
    Check(Rules(classic) == "" && classic!["unresolvedRuleEntries"]!.AsArray().Count == 0,
        $"Doctor Who (1963) preview lists no built-in rule (got [{Rules(classic)}], {classic!["unresolvedRuleEntries"]!.AsArray().Count} unresolved)");
    var newWho = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={NewWhoId}");
    Check(Rules(newWho) == "Doctor Who (2023-) stories" && newWho!["unresolvedRuleEntries"]!.AsArray().Count == 0,
        $"Doctor Who (2023) preview uses only the 2023 rule (got [{Rules(newWho)}], {newWho!["unresolvedRuleEntries"]!.AsArray().Count} unresolved)");
    Check(newWho["stories"]!.AsArray().Count == newWhoExpected.Count, "Doctor Who (2023) preview stories: " + newWho["stories"]!.ToJsonString());
    var oldLayout = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={OldLayoutId}");
    Check(Rules(oldLayout) == "Doctor Who (2005-) stories" && oldLayout!["stories"]!.AsArray().Count == newWhoExpected.Count,
        "2024-25 seasons filed under the 2005 series (old TVDB layout) still linked: " + oldLayout!["stories"]!.ToJsonString());
    Check(res["seriesLookupFailed"]!.GetValue<bool>() == false, "preview says the series lookup worked");
}

// Series lookup failing: the shuffle still works (no built-in rules then), with the episode response's own status and headers.
{
    failSeriesLookup = true;
    var r = await http.GetAsync($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=100");
    var body = await r.Content.ReadFromJsonAsync<JsonObject>();
    Check(r.StatusCode == HttpStatusCode.OK && body!["Items"]!.AsArray().Count == Series(DoctorWhoId).Count(), "shuffle still answers when the series lookup fails");
    var failed = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={DoctorWhoId}");
    Check(failed!["seriesLookupFailed"]!.GetValue<bool>() && failed["rules"]!.AsArray().Count == 0, "preview says when the series lookup failed");
    failSeriesLookup = false;
    var before = seriesLookups;
    r = await http.GetAsync($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=100");
    Check(seriesLookups == before + 1 && !r.Headers.Contains("X-Series-Lookup"), "one series lookup per shuffle, its headers not leaked into the answer");
}

// Rule matching by provider id and by name with a year.
{
    var builtInRules = new Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet(
        Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet.Parse(Plugin.BuiltInJson), null, new Jellyfin.Plugin.StoryShuffle.Grouping.RuleDefaults());
    Jellyfin.Plugin.StoryShuffle.Grouping.SeriesInfo Info(int? year, params (string, string)[] ids) => new()
    {
        Year = year,
        ProviderIds = ids.ToDictionary(i => i.Item1, i => i.Item2, StringComparer.OrdinalIgnoreCase),
    };
    Check(builtInRules.Resolve("Docteur Who", "x", Info(2005, ("Tmdb", "57243"))).Sources.SequenceEqual(new[] { "Doctor Who (2005-) stories" })
          && builtInRules.Resolve("Doctor Who (2005)", "x", Info(null, ("tvdb", "78804"))).Sources.Count == 1
          && builtInRules.Resolve("Doctor Who", "x", Info(2005, ("Imdb", "tt0436992"))).Sources.SequenceEqual(new[] { "Doctor Who (2005-) stories" }),
        "built-in DW rule found by TMDB, TVDB or IMDb id, whatever the series is called");
    Check(builtInRules.Resolve("Doctor Who", "x", null).Sources.Count == 0
          && builtInRules.Resolve("Doctor Who (2023)", "x", Info(2024, ("Tvdb", "78804x"), ("Tmdb", "239770"))).Sources.SequenceEqual(new[] { "Doctor Who (2023-) stories" }),
        "built-in rules never match by name");

    var user = new Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet(null,
        Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet.Parse("""[ { "name": "2005", "series": ["Doctor Who (2005)"] }, { "name": "any", "series": ["Doctor Who"] } ]"""),
        new Jellyfin.Plugin.StoryShuffle.Grouping.RuleDefaults());
    string Matched(string name, int? year) => string.Join(",", user.Resolve(name, "x", year is null ? null : Info(year)).Sources);
    Check(Matched("Doctor Who (2023)", null) == "any" && Matched("Doctor Who", 2023) == "any" && Matched("Doctor Who (1963)", 1963) == "any",
        "user rule \"Doctor Who (2005)\" doesn't match another year; \"Doctor Who\" matches every year");
    Check(Matched("Doctor Who (2005)", null) == "2005,any" && Matched("Doctor Who", 2005) == "2005,any" && Matched("Doctor Who", null) == "2005,any",
        "user rule \"Doctor Who (2005)\" matches the 2005 series, by name or production year");

    // Jellyfin often shows the 2023 series as plain "Doctor Who" with production year 2024: the year in a rule's name is
    // compared to that, so naming it needs the provider id (README, "Settings").
    var newWhoRules = new Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet(null,
        Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet.Parse("""[ { "name": "by name", "series": ["Doctor Who (2023)"] }, { "name": "by id", "series": ["tmdb:239770"] } ]"""),
        new Jellyfin.Plugin.StoryShuffle.Grouping.RuleDefaults());
    Check(string.Join(",", newWhoRules.Resolve("Doctor Who", "x", Info(2024, ("Tmdb", "239770"))).Sources) == "by id",
        "\"Doctor Who (2023)\" vs a plain \"Doctor Who\" from 2024: the production year counts, the provider id matches");
}

// Base URL prefix (Jellyfin "/jellyfin" base URL) is kept on the preview re-route.
{
    lookupPrefix = "/jellyfin";
    var r = await http.GetFromJsonAsync<JsonObject>($"/jellyfin/StoryShuffle/Preview?seriesId={DoctorWhoId}");
    Check(r!["seriesLookupFailed"]!.GetValue<bool>() == false && r["rules"]!.AsArray().Any(x => x!.GetValue<string>() == "Doctor Who (2005-) stories"),
        "preview under a base URL looks the series up under it and uses the built-in list");
    var q = await Names($"/jellyfin/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=100");
    Check(q.Count == Series(DoctorWhoId).Count() && StoriesBroken(q, dwExpected).Length == 0, "shuffle under a base URL keeps the built-in stories");
    lookupPrefix = "";
}

// /Users/{id}/Items: the user id is in the path; the lookup passes it on.
{
    var q = await Names($"/Users/u42/Items?ParentId={SouthParkId}&IncludeItemTypes=Episode&Recursive=true&SortBy=Random&Limit=300");
    Check(q.Count == Series(SouthParkId).Count() && StoriesBroken(q, spExpected).Length == 0 && lastLookupUserId == "u42",
        $"/Users/{{id}}/Items shuffle uses the built-in list (lookup user {lastLookupUserId})");
}

// A "word:value" name that isn't a provider id still matches by name.
{
    var csi = new Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet(null,
        Jellyfin.Plugin.StoryShuffle.Grouping.RuleSet.Parse("""[ { "series": ["CSI:Miami"] } ]"""), new Jellyfin.Plugin.StoryShuffle.Grouping.RuleDefaults());
    Check(csi.Resolve("CSI: Miami", "x", null).Sources.Count == 1, "\"CSI:Miami\" still matches the series by name");
}

// Small limit: never splits a story. A first story longer than the limit (Flux, 6 parts) is kept whole on its own.
for (var i = 0; i < 40; i++)
{
    var q = await Names($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=5");
    Check((q.Count <= 5 || dwExpected.Any(g => g.SequenceEqual(q))) && q.Count >= 1 && StoriesBroken(q, dwExpected).Length == 0, $"limit 5 respected without cutting stories: {string.Join(" | ", q)}");
}

// /Items endpoint used by other apps.
{
    var q = await Names($"/Items?ParentId={SouthParkId}&IncludeItemTypes=Episode&Recursive=true&SortBy=Random&Limit=300");
    Check(q.Count == Series(SouthParkId).Count() && StoriesBroken(q, spExpected).Length == 0, "/Items episode shuffle grouped");
}

// Things that must pass through untouched.
{
    var ordered = await Names($"/Shows/{DoctorWhoId}/Episodes?limit=3");
    Check(ordered.SequenceEqual(new[] { "The Christmas Invasion", "The Runaway Bride", "Voyage of the Damned" }), "non-shuffle request untouched");
    var raw = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None }) { BaseAddress = new Uri(baseUrl) };
    raw.DefaultRequestHeaders.Add("Authorization", "MediaBrowser Token=\"x\"");
    raw.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
    var r = await raw.GetAsync($"/Items?ParentId={SouthParkId}&IncludeItemTypes=Movie&SortBy=Random");
    Check(r.Content.Headers.ContentEncoding.Count > 0, "unrelated request still compressed (not intercepted)");

    var anon = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var unauth = await anon.GetAsync($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random");
    Check(unauth.StatusCode == HttpStatusCode.Unauthorized, "auth failures pass through");
    var unauthPreview = await anon.GetAsync($"/StoryShuffle/Preview?seriesId={DoctorWhoId}");
    Check(unauthPreview.StatusCode == HttpStatusCode.Unauthorized, "preview requires auth");
}

// Turning it off restores Jellyfin's own shuffle.
{
    cfg.Enabled = false;
    plugin.UpdateConfiguration(cfg);
    var q = await Names($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=100");
    Check(lastQuery!.Contains("Random"), "disabled plugin leaves request alone");
    cfg.Enabled = true;

    // Broken user JSON: built-in still works and the error is reported.
    cfg.RulesJson = "[ { oops ";
    plugin.UpdateConfiguration(cfg);
    var q2 = await Names($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=100");
    Check(StoriesBroken(q2, dwExpected).Length == 0 && plugin.RulesError is not null, "bad user JSON ignored, built-ins still apply");
}

// ---------------------------------------------------------------- release packaging and plugin repository manifest
{
    // The plugin's own build output, as the release workflow packages it.
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (!Directory.Exists(Path.Combine(root.FullName, "src", "Jellyfin.Plugin.StoryShuffle"))) { root = root.Parent!; }
    var buildDir = Path.Combine(root.FullName, "src", "Jellyfin.Plugin.StoryShuffle", "bin", "Release", "net8.0");
    var builtVersion = typeof(Plugin).Assembly.GetName().Version!;
    var built = builtVersion.ToString();
    // Another, higher version than the build's: a release runs these checks with the DLL stamped from its tag.
    var later = new Version(builtVersion.Major + 1, 0, 0, 0).ToString();
    var rel = Directory.CreateTempSubdirectory("storyshuffle-release").FullName;
    Check(File.Exists(Path.Combine(buildDir, "meta.json")), "meta.json copied next to the built DLL");
    var srcMeta = Path.Combine(buildDir, "meta.json");

    Check(ReleaseTool.ParseVersion("v1.2.3.4") == "1.2.3.4" && ReleaseTool.ParseVersion("1.2.3.4") == "1.2.3.4", "release version taken from the tag");
    Check(Throws(() => ReleaseTool.ParseVersion("v1.2.3")) && Throws(() => ReleaseTool.ParseVersion("v1.2.3.4-beta")), "tags that aren't vX.Y.Z.W are refused");
    Check(Throws(() => ReleaseTool.Package(later, buildDir, srcMeta, rel, null, DateTime.UtcNow)), "DLL built with another version is refused");

    var when = new DateTime(2026, 10, 27, 21, 5, 9, DateTimeKind.Utc);
    var pkg = ReleaseTool.Package("v" + built, buildDir, srcMeta, Path.Combine(rel, "a"), "  Fixed things.\n", when);
    using (var zip = ZipFile.OpenRead(pkg.ZipPath))
    {
        Check(Path.GetFileName(pkg.ZipPath) == $"StoryShuffle_{built}.zip", "zip named StoryShuffle_<version>.zip");
        Check(zip.Entries.Select(e => e.FullName).OrderBy(n => n).SequenceEqual(new[] { "Jellyfin.Plugin.StoryShuffle.dll", "meta.json" }), "zip holds the DLL and meta.json at its root");
        using var ms = zip.GetEntry("meta.json")!.Open();
        var zipMeta = JsonNode.Parse(ms)!;
        Check(zipMeta["version"]!.GetValue<string>() == built && zipMeta["timestamp"]!.GetValue<string>() == "2026-10-27T21:05:09Z"
            && zipMeta["changelog"]!.GetValue<string>() == "Fixed things." && zipMeta["guid"]!.GetValue<string>() == plugin.Id.ToString(), "packaged meta.json stamped with version, timestamp and changelog");
    }

    using (var s = File.OpenRead(pkg.ZipPath))
    {
        Check(pkg.Checksum == Convert.ToHexString(System.Security.Cryptography.MD5.HashData(s)).ToLowerInvariant(), "checksum is the zip's MD5");
    }

    var manifest = Path.Combine(rel, "pages", "manifest.json");
    Check(ReleaseTool.UpdateManifest(manifest, pkg.MetaPath, "https://example.test/a.zip", pkg.Checksum, "BryanSmee") == 1, "manifest created with one version");

    // A later release: same plugin, higher version.
    var nextMeta = JsonNode.Parse(File.ReadAllText(pkg.MetaPath))!;
    nextMeta["version"] = later;
    nextMeta["changelog"] = "Second.";
    var nextPath = Path.Combine(rel, "next-meta.json");
    File.WriteAllText(nextPath, nextMeta.ToJsonString());
    Check(ReleaseTool.UpdateManifest(manifest, nextPath, "https://example.test/b.zip", "abc", "BryanSmee") == 2, "second release adds a version");
    Check(ReleaseTool.UpdateManifest(manifest, nextPath, "https://example.test/b2.zip", "def", "BryanSmee") == 2, "re-releasing a version replaces it");

    var repo = JsonNode.Parse(File.ReadAllText(manifest))!.AsArray();
    var entry = repo.Single()!;
    var versions = entry["versions"]!.AsArray();
    Check(entry["guid"]!.GetValue<string>() == plugin.Id.ToString() && entry["name"]!.GetValue<string>() == "Story Shuffle"
        && entry["owner"]!.GetValue<string>() == "BryanSmee" && entry["category"] is not null && entry["overview"] is not null && entry["description"] is not null, "manifest package fields");
    Check(versions.Select(v => v!["version"]!.GetValue<string>()).SequenceEqual(new[] { later, built }), "manifest keeps older versions, newest first");
    var newest = versions[0]!;
    Check(newest["sourceUrl"]!.GetValue<string>() == "https://example.test/b2.zip" && newest["checksum"]!.GetValue<string>() == "def"
        && newest["changelog"]!.GetValue<string>() == "Second." && newest["targetAbi"]!.GetValue<string>() == "12.0.0.0"
        && newest["timestamp"]!.GetValue<string>() == "2026-10-27T21:05:09Z", "manifest version fields");
}

static bool Throws(Action a)
{
    try { a(); return false; } catch (Exception) { return true; }
}

await app.StopAsync();
Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? $"ALL {passes} CHECKS PASSED" : $"{failures.Count} FAILED, {passes} passed");
return failures.Count == 0 ? 0 : 1;

namespace StoryShuffle.Tests
{
    public class DispatchProxyPaths : DispatchProxy
    {
        private string _root = string.Empty;

        public static IApplicationPaths Create(string root)
        {
            var p = DispatchProxy.Create<IApplicationPaths, DispatchProxyPaths>();
            ((DispatchProxyPaths)(object)p)._root = root;
            return p;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.ReturnType == typeof(string) ? Path.Combine(_root, targetMethod.Name.Replace("get_", string.Empty)) : null;
    }

    public class XmlFileSerializer : IXmlSerializer
    {
        public object DeserializeFromStream(Type type, Stream stream) => new System.Xml.Serialization.XmlSerializer(type).Deserialize(stream)!;

        public void SerializeToStream(object obj, Stream stream) => new System.Xml.Serialization.XmlSerializer(obj.GetType()).Serialize(stream, obj);

        public void SerializeToFile(object obj, string file)
        {
            using var f = File.Create(file);
            SerializeToStream(obj, f);
        }

        public object DeserializeFromFile(Type type, string file)
        {
            using var f = File.OpenRead(file);
            return DeserializeFromStream(type, f);
        }

        public object DeserializeFromBytes(Type type, byte[] buffer) => DeserializeFromStream(type, new MemoryStream(buffer));
    }
}
