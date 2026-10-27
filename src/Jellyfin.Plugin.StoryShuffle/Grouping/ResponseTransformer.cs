using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>Reads and rewrites Jellyfin item-list JSON (QueryResult&lt;BaseItemDto&gt;).</summary>
public static class ResponseTransformer
{
    /// <summary>Takes an ordered episode list response and returns it story-shuffled.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="limit">The queue length the client originally asked for.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="recentDays">Recently-watched window in days.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="rng">Random source.</param>
    /// <returns>The new body.</returns>
    public static byte[] Shuffle(byte[] json, int? limit, RuleSet rules, int recentDays, DateTime nowUtc, Random rng)
    {
        var (root, itemsKey, nodes) = Read(json);
        var episodes = nodes.Select(ToEpisode).ToList();

        var stories = new List<Story>();
        foreach (var series in episodes.GroupBy(SeriesKey))
        {
            var first = series.First();
            var rule = rules.Resolve(first.SeriesName, first.SeriesId);
            if (!rule.Enabled)
            {
                stories.AddRange(series.Select(e =>
                {
                    var s = new Story();
                    s.Episodes.Add(e);
                    return s;
                }));
                continue;
            }

            stories.AddRange(StoryGrouper.Group(series.ToList(), rule));
        }

        var queue = StoryShuffler.Flatten(StoryShuffler.Shuffle(stories, recentDays, nowUtc, rng), limit);

        root[itemsKey] = new JsonArray(queue.Select(e => nodes[e.Index]).ToArray<JsonNode?>());
        var startKey = FindKey(root, "StartIndex");
        if (startKey is not null)
        {
            root[startKey] = 0;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    /// <summary>Plain shuffle used when grouping fails for any reason.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="limit">Limit.</param>
    /// <param name="rng">Random source.</param>
    /// <returns>The new body.</returns>
    public static byte[] PlainShuffle(byte[] json, int? limit, Random rng)
    {
        var (root, itemsKey, nodes) = Read(json);
        var list = nodes.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        if (limit is > 0)
        {
            list = list.Take(limit.Value).ToList();
        }

        root[itemsKey] = new JsonArray(list.ToArray<JsonNode?>());
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    /// <summary>Builds the settings-page preview for one series.</summary>
    /// <param name="json">Ordered episode list response.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="rulesError">Error parsing the user's rules, if any.</param>
    /// <returns>Preview JSON.</returns>
    public static byte[] Preview(byte[] json, RuleSet rules, string? rulesError)
    {
        var (_, _, nodes) = Read(json);
        var episodes = nodes.Select(ToEpisode).ToList();
        var first = episodes.FirstOrDefault();
        var rule = rules.Resolve(first?.SeriesName, first?.SeriesId);
        var unresolved = new List<string>();
        var stories = StoryGrouper.Group(episodes, rule, unresolved);

        var result = new JsonObject
        {
            ["seriesName"] = first?.SeriesName,
            ["enabled"] = rule.Enabled,
            ["rules"] = new JsonArray(rule.Sources.Select(s => (JsonNode?)s).ToArray()),
            ["rulesError"] = rulesError,
            ["episodeCount"] = episodes.Count,
            ["storyCount"] = stories.Count,
            ["stories"] = new JsonArray(stories
                .Where(s => s.Episodes.Count > 1)
                .Select(s => (JsonNode?)new JsonObject
                {
                    ["reasons"] = new JsonArray(s.Reasons.Select(r => (JsonNode?)r).ToArray()),
                    ["episodes"] = new JsonArray(s.Episodes.Select(e => (JsonNode?)Label(e)).ToArray()),
                })
                .ToArray()),
            ["unresolvedRuleEntries"] = new JsonArray(unresolved.Distinct().Select(u => (JsonNode?)u).ToArray()),
        };
        return JsonSerializer.SerializeToUtf8Bytes(result);
    }

    private static string Label(EpisodeInfo e)
        => e.Season is null || e.Episode is null
            ? e.Name
            : string.Create(CultureInfo.InvariantCulture, $"S{e.Season:00}E{e.Episode:00}  {e.Name}");

    private static string SeriesKey(EpisodeInfo e)
        => string.IsNullOrEmpty(e.SeriesId) ? TitleTools.NormalizeSeries(e.SeriesName) : e.SeriesId;

    private static (JsonObject Root, string ItemsKey, List<JsonNode> Nodes) Read(byte[] json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Response is not a JSON object.");
        var itemsKey = FindKey(root, "Items") ?? throw new JsonException("Response has no Items.");
        var arr = root[itemsKey] as JsonArray ?? throw new JsonException("Items is not an array.");
        var nodes = arr.Where(n => n is not null).Select(n => n!).ToList();
        arr.Clear();
        return (root, itemsKey, nodes);
    }

    private static EpisodeInfo ToEpisode(JsonNode node, int index)
    {
        var o = node as JsonObject;
        DateTime? lastPlayed = null;
        if (o is not null && Get(o, "UserData") is JsonObject ud && GetString(ud, "LastPlayedDate") is { } lp
            && DateTime.TryParse(lp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            lastPlayed = parsed;
        }

        return new EpisodeInfo
        {
            Index = index,
            Id = o is null ? string.Empty : GetString(o, "Id") ?? string.Empty,
            Type = o is null ? string.Empty : GetString(o, "Type") ?? string.Empty,
            Name = o is null ? string.Empty : GetString(o, "Name") ?? string.Empty,
            Overview = o is null ? null : GetString(o, "Overview"),
            Season = o is null ? null : GetInt(o, "ParentIndexNumber"),
            Episode = o is null ? null : GetInt(o, "IndexNumber"),
            SeriesId = o is null ? string.Empty : GetString(o, "SeriesId") ?? string.Empty,
            SeriesName = o is null ? string.Empty : GetString(o, "SeriesName") ?? string.Empty,
            LastPlayed = lastPlayed,
        };
    }

    private static string? FindKey(JsonObject o, string name)
    {
        foreach (var kv in o)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Key;
            }
        }

        return null;
    }

    private static JsonNode? Get(JsonObject o, string name)
    {
        var key = FindKey(o, name);
        return key is null ? null : o[key];
    }

    private static string? GetString(JsonObject o, string name)
        => Get(o, name) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? GetInt(JsonObject o, string name)
    {
        if (Get(o, name) is not JsonValue v)
        {
            return null;
        }

        if (v.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (v.TryGetValue<long>(out var l))
        {
            return (int)l;
        }

        return v.TryGetValue<JsonElement>(out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var e) ? e : null;
    }
}
