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
    /// <summary>Takes an ordered episode list response and picks the story-shuffled queue.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="limit">The queue length the client originally asked for.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="recentDays">Recently-watched window in days.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="rng">Random source.</param>
    /// <param name="seriesItems">The series items by id (see <see cref="ReadSeries"/>), if they could be looked up.</param>
    /// <returns>The queue (see <see cref="Pick"/> and <see cref="IdsAt"/>), with what was done.</returns>
    public static ShuffleResult Shuffle(
        byte[] json, int? limit, RuleSet rules, int recentDays, DateTime nowUtc, Random rng, IReadOnlyDictionary<string, SeriesInfo>? seriesItems = null)
    {
        var (_, _, nodes) = Read(json);
        var episodes = nodes.Select(ToEpisode).ToList();

        var stories = new List<Story>();
        var shuffled = new List<ShuffledSeries>();
        foreach (var series in episodes.GroupBy(SeriesKey))
        {
            var first = series.First();
            var info = FindSeries(seriesItems, first.SeriesId);
            var rule = rules.Resolve(first.SeriesName, first.SeriesId, info);
            var before = stories.Count;
            if (!rule.Enabled)
            {
                stories.AddRange(series.Select(e =>
                {
                    var s = new Story();
                    s.Episodes.Add(e);
                    return s;
                }));
            }
            else
            {
                stories.AddRange(StoryGrouper.Group(series.ToList(), rule));
            }

            shuffled.Add(new ShuffledSeries
            {
                Name = first.SeriesName,
                Id = first.SeriesId,
                ProviderIds = info?.ProviderIds,
                Rules = rule.Sources.ToList(),
                Enabled = rule.Enabled,
                EpisodeCount = series.Count(),
                StoryCount = stories.Count - before,
            });
        }

        var queue = StoryShuffler.Flatten(StoryShuffler.Shuffle(stories, recentDays, nowUtc, rng, out var recentCount), limit);

        var result = new ShuffleResult
        {
            Order = queue.Select(e => e.Index).ToList(),
            EpisodeCount = episodes.Count,
            StoryCount = stories.Count,
            RecentCount = recentCount,
            ReturnedCount = queue.Count,
        };
        result.Series.AddRange(shuffled);
        return result;
    }

    /// <summary>Plain shuffle used when grouping fails for any reason.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="limit">Limit.</param>
    /// <param name="rng">Random source.</param>
    /// <returns>The queue, as positions in the response's items.</returns>
    public static List<int> PlainShuffleOrder(byte[] json, int? limit, Random rng)
    {
        var (_, _, nodes) = Read(json);
        var list = Enumerable.Range(0, nodes.Count).ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return limit is > 0 ? list.Take(limit.Value).ToList() : list;
    }

    /// <summary>Keeps the items at the given positions of a response, in that order.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="order">Positions in the response's items (see <see cref="Shuffle"/>).</param>
    /// <returns>The new body.</returns>
    public static byte[] Pick(byte[] json, IReadOnlyList<int> order)
    {
        var (root, itemsKey, nodes) = Read(json);
        return Write(root, itemsKey, order.Select(i => nodes[i]));
    }

    /// <summary>Gives the ids of the items at the given positions of a response.</summary>
    /// <param name="json">Response body.</param>
    /// <param name="order">Positions in the response's items.</param>
    /// <returns>The item ids, in that order.</returns>
    public static List<string> IdsAt(byte[] json, IReadOnlyList<int> order)
    {
        var (_, _, nodes) = Read(json);
        return order.Select(i => ToEpisode(nodes[i], i).Id).ToList();
    }

    /// <summary>
    /// Builds the answer from the full items, asked for by id, in queue order: Jellyfin's <c>/Items?ids=</c> doesn't keep the
    /// order of the ids. The envelope (TotalRecordCount…) comes from the episode list the queue was built from.
    /// </summary>
    /// <param name="envelope">The episode list response the queue was picked from.</param>
    /// <param name="full">The items of the queue, as the client asked for them, any order.</param>
    /// <param name="ids">The queue's item ids, in play order.</param>
    /// <returns>The new body. Ids missing from <paramref name="full"/> are left out.</returns>
    public static byte[] Reorder(byte[] envelope, byte[] full, IReadOnlyList<string> ids)
    {
        var (root, itemsKey, _) = Read(envelope);
        var (_, _, nodes) = Read(full);
        var byId = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            byId.TryAdd(TitleTools.NormalizeId(ToEpisode(nodes[i], i).Id), nodes[i]);
        }

        return Write(root, itemsKey, ids.Select(id => byId.GetValueOrDefault(TitleTools.NormalizeId(id))).OfType<JsonNode>());
    }

    /// <summary>Builds the settings-page preview for one series.</summary>
    /// <param name="json">Ordered episode list response.</param>
    /// <param name="rules">Rules.</param>
    /// <param name="rulesError">Error parsing the user's rules, if any.</param>
    /// <param name="seriesItems">The series items by id (see <see cref="ReadSeries"/>), or null when the lookup failed.</param>
    /// <returns>Preview JSON.</returns>
    public static byte[] Preview(byte[] json, RuleSet rules, string? rulesError, IReadOnlyDictionary<string, SeriesInfo>? seriesItems = null)
    {
        var (_, _, nodes) = Read(json);
        var episodes = nodes.Select(ToEpisode).ToList();
        var first = episodes.FirstOrDefault();
        var rule = rules.Resolve(first?.SeriesName, first?.SeriesId, FindSeries(seriesItems, first?.SeriesId));
        var unresolved = new List<string>();
        var stories = StoryGrouper.Group(episodes, rule, unresolved);

        var result = new JsonObject
        {
            ["seriesName"] = first?.SeriesName,
            ["enabled"] = rule.Enabled,
            ["rules"] = new JsonArray(rule.Sources.Select(s => (JsonNode?)s).ToArray()),
            ["rulesError"] = rulesError,
            ["seriesLookupFailed"] = seriesItems is null && episodes.Count > 0,
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

    /// <summary>Lists the series the episodes in a response belong to.</summary>
    /// <param name="json">Episode list response.</param>
    /// <returns>The distinct series ids.</returns>
    public static List<string> SeriesIds(byte[] json)
    {
        var (_, _, nodes) = Read(json);
        return nodes.Select(ToEpisode)
            .Select(e => e.SeriesId)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Reads series items (asked for with their ProviderIds) into a lookup by series id.</summary>
    /// <param name="json">Item list response.</param>
    /// <returns>The series by id.</returns>
    public static Dictionary<string, SeriesInfo> ReadSeries(byte[] json)
    {
        var (_, _, nodes) = Read(json);
        var result = new Dictionary<string, SeriesInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in nodes.OfType<JsonObject>())
        {
            var info = new SeriesInfo
            {
                Id = GetString(o, "Id") ?? string.Empty,
                Name = GetString(o, "Name") ?? string.Empty,
                Year = GetInt(o, "ProductionYear"),
                ProviderIds = ReadProviderIds(o),
            };
            if (info.Id.Length > 0)
            {
                result[TitleTools.NormalizeId(info.Id)] = info;
            }
        }

        return result;
    }

    private static SeriesInfo? FindSeries(IReadOnlyDictionary<string, SeriesInfo>? series, string? id)
        => series is not null && !string.IsNullOrEmpty(id) && series.TryGetValue(TitleTools.NormalizeId(id), out var info) ? info : null;

    private static string Label(EpisodeInfo e)
        => e.Season is null || e.Episode is null
            ? e.Name
            : string.Create(CultureInfo.InvariantCulture, $"S{e.Season:00}E{e.Episode:00}  {e.Name}");

    private static string SeriesKey(EpisodeInfo e)
        => string.IsNullOrEmpty(e.SeriesId) ? TitleTools.NormalizeSeries(e.SeriesName) : e.SeriesId;

    private static byte[] Write(JsonObject root, string itemsKey, IEnumerable<JsonNode> items)
    {
        root[itemsKey] = new JsonArray(items.ToArray<JsonNode?>());
        var startKey = FindKey(root, "StartIndex");
        if (startKey is not null)
        {
            root[startKey] = 0;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

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
            ProviderIds = o is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : ReadProviderIds(o),
            LastPlayed = lastPlayed,
        };
    }

    private static Dictionary<string, string> ReadProviderIds(JsonObject o)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Get(o, "ProviderIds") is JsonObject p)
        {
            foreach (var kv in p)
            {
                if (kv.Value is JsonValue v && v.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id))
                {
                    ids[kv.Key] = id.Trim();
                }
            }
        }

        return ids;
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
