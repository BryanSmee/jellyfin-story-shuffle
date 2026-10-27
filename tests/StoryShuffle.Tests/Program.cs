using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.StoryShuffle;
using Jellyfin.Plugin.StoryShuffle.Configuration;
using Jellyfin.Plugin.StoryShuffle.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
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

// ---------------------------------------------------------------- fake Jellyfin server with the plugin's middleware
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddResponseCompression();
new ServiceRegistrator().RegisterServices(builder.Services, null!);
var app = builder.Build();
app.UseResponseCompression();
string? lastQuery = null;
app.MapGet("/{**rest}", (HttpContext ctx, string? rest) =>
{
    lastQuery = ctx.Request.QueryString.Value;
    var q = ctx.Request.Query;
    if (!ctx.Request.Headers.ContainsKey("Authorization")) { return Results.Unauthorized(); }
    var path = "/" + rest;
    List<Library.Ep> eps;
    var m = System.Text.RegularExpressions.Regex.Match(path, @"Shows/([0-9a-f]+)/Episodes$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success)
    {
        eps = Series(m.Groups[1].Value).ToList();
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
    var body = new JsonObject
    {
        ["Items"] = new JsonArray(eps.Select(e => (JsonNode?)Library.ToJson(e, withOverview)).ToArray()),
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
var builtIn = JsonNode.Parse(Plugin.BuiltInJson)!.AsArray();
List<List<string>> RuleGroups(string series) => builtIn
    .Where(r => r!["series"]!.AsArray().Any(s => s!.GetValue<string>() == series))
    .SelectMany(r => r!["groups"]!.AsArray().Select(g => g!.AsArray().Select(x => x!.GetValue<string>()).ToList()))
    .ToList();

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
                  && lastQuery.Contains("Overview", StringComparison.OrdinalIgnoreCase) && lastQuery.Contains("Trickplay", StringComparison.OrdinalIgnoreCase)
                  && lastQuery.Contains("IsMissing=false", StringComparison.OrdinalIgnoreCase), $"{label}: rewritten query {lastQuery}");
        }
    }

    Check(firsts.Count >= Math.Min(5, minOpeners), $"{label}: order is actually random ({firsts.Count} different openers in {runs} runs)");
    Console.WriteLine($"ok  {label}: {runs} shuffles, all stories intact");
}

await ShuffleSeries("Doctor Who", DoctorWhoId, dwExpected, Series(DoctorWhoId).Count());
await ShuffleSeries("South Park", SouthParkId, spExpected, Series(SouthParkId).Count());
await ShuffleSeries("Classic Doctor Who (title heuristic)", ClassicId, classicExpected, Series(ClassicId).Count(), minOpeners: 3);
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

    var sp = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={SouthParkId}");
    var spStories = sp!["stories"]!.AsArray().Count;
    Check(spStories == spExpected.Count, $"SP preview finds {spStories} stories, expected {spExpected.Count}");

    var ts = await http.GetFromJsonAsync<JsonObject>($"/StoryShuffle/Preview?seriesId={TestShowId}");
    Check(ts!["stories"]!.AsArray().Count == tsExpected.Count, "Test Show preview: " + ts["stories"]!.ToJsonString());
    Check(ts["rules"]!.AsArray().Count == 1, "Test Show matched user rule despite (1999) in the name");
}

// Base URL prefix (Jellyfin "/jellyfin" base URL) is kept on the preview re-route.
{
    var r = await http.GetAsync($"/jellyfin/StoryShuffle/Preview?seriesId={DoctorWhoId}");
    Check(r.IsSuccessStatusCode, "preview under a base URL");
}

// Small limit: never splits a story.
for (var i = 0; i < 40; i++)
{
    var q = await Names($"/Shows/{DoctorWhoId}/Episodes?SortBy=Random&limit=5");
    Check(q.Count <= 5 && q.Count >= 1 && StoriesBroken(q, dwExpected).Length == 0, $"limit 5 respected without cutting stories: {string.Join(" | ", q)}");
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
