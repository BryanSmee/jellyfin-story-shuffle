using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.StoryShuffle.Grouping;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.StoryShuffle.Web;

/// <summary>
/// Intercepts the "random episodes of this series" requests that the shuffle button sends, asks Jellyfin for the
/// episodes in order instead, and returns them shuffled story by story.
/// </summary>
public class ShuffleMiddleware
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const string Guid = @"[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}";

    private static readonly Regex ShowsEpisodes = new(@"^(?<prefix>.*?)/Shows/(?<id>" + Guid + @")/Episodes/?$", Opts);
    private static readonly Regex ItemsPath = new(@"^(?<prefix>.*?)/(?:Users/(?<user>[^/]+)/)?Items/?$", Opts);
    private static readonly Regex PreviewPath = new(@"^(?<prefix>.*?)/StoryShuffle/Preview/?$", Opts);
    private static readonly Regex BuiltInPath = new(@"/StoryShuffle/BuiltInRules/?$", Opts);
    private static readonly Regex GuidOnly = new("^" + Guid + "$", Opts);

    private readonly RequestDelegate _next;
    private readonly ILogger<ShuffleMiddleware> _logger;

    /// <summary>Initializes a new instance of the <see cref="ShuffleMiddleware"/> class.</summary>
    /// <param name="next">Next middleware.</param>
    /// <param name="logger">Logger.</param>
    public ShuffleMiddleware(RequestDelegate next, ILogger<ShuffleMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Handles a request.</summary>
    /// <param name="ctx">The context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext ctx)
    {
        var plugin = Plugin.Instance;
        if (plugin is null || !HttpMethods.IsGet(ctx.Request.Method))
        {
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        var path = ctx.Request.PathBase.Value + ctx.Request.Path.Value;

        if (BuiltInPath.IsMatch(path))
        {
            await WriteJson(ctx, 200, Encoding.UTF8.GetBytes(Plugin.BuiltInJson)).ConfigureAwait(false);
            return;
        }

        var preview = PreviewPath.Match(path);
        if (preview.Success)
        {
            await HandlePreview(ctx, plugin, preview.Groups["prefix"].Value).ConfigureAwait(false);
            return;
        }

        var cfg = plugin.Configuration;
        var query = ctx.Request.Query;
        if (!HasValue(query, "sortBy", "Random"))
        {
            // Only an episode list the plugin would have taken over had it asked for random order. Checked only when the
            // line would be written, as every request comes here.
            var level = cfg.DetailedLog ? LogLevel.Information : LogLevel.Debug;
            if (cfg.Enabled && _logger.IsEnabled(level) && WouldHandle(path, query, cfg))
            {
                _logger.Log(level, "Story Shuffle passed a request to {Path} through: not a random-order request (sortBy isn't Random)", Safe(path));
            }
        }
        else
        {
            var shows = ShowsEpisodes.Match(path);
            var items = ItemsPath.Match(path);
            string? reason = null;
            if ((shows.Success || items.Success) && !cfg.Enabled)
            {
                reason = "story shuffle is turned off in the settings";
            }
            else if (shows.Success)
            {
                await HandleShuffle(ctx, plugin, path, shows.Groups["prefix"].Value, isItemsEndpoint: false).ConfigureAwait(false);
                return;
            }
            else if (items.Success)
            {
                if (!cfg.InterceptItemsEndpoint)
                {
                    reason = "\"Also handle shuffle requests from other apps\" is turned off";
                }
                else if (string.IsNullOrEmpty(First(query, "parentId")))
                {
                    reason = "no parentId";
                }
                else if (!OnlyEpisodes(query))
                {
                    reason = "not episodes only (includeItemTypes doesn't ask for episodes alone)";
                }
                else
                {
                    await HandleShuffle(ctx, plugin, path, items.Groups["prefix"].Value, isItemsEndpoint: true, items.Groups["user"].Value).ConfigureAwait(false);
                    return;
                }
            }

            if (reason is not null)
            {
                LogDetail(plugin, "Story Shuffle passed a random-order request to {Path} through: {Reason}", Safe(path), reason);
            }
        }

        await _next(ctx).ConfigureAwait(false);
    }

    private async Task HandleShuffle(HttpContext ctx, Plugin plugin, string path, string prefix, bool isItemsEndpoint, string? pathUserId = null)
    {
        var started = Stopwatch.GetTimestamp();
        var original = ctx.Request.Query;
        var userId = First(original, "userId") ?? (string.IsNullOrEmpty(pathUserId) ? null : pathUserId);
        int? limit = int.TryParse(First(original, "limit"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;

        var rewritten = new List<KeyValuePair<string, string?>>();
        foreach (var kv in original)
        {
            if (Is(kv.Key, "sortBy") || Is(kv.Key, "sortOrder") || Is(kv.Key, "limit") || Is(kv.Key, "startIndex")
                || Is(kv.Key, "startItemId") || Is(kv.Key, "fields"))
            {
                continue;
            }

            foreach (var v in kv.Value)
            {
                rewritten.Add(new(kv.Key, v));
            }
        }

        rewritten.Add(new("fields", MergeFields(original)));
        if (isItemsEndpoint)
        {
            rewritten.Add(new("sortBy", "SeriesSortName,ParentIndexNumber,IndexNumber"));
            rewritten.Add(new("sortOrder", "Ascending"));
        }

        ctx.Request.QueryString = QueryString.Create(rewritten);

        var (status, contentType, body) = await RunBuffered(ctx).ConfigureAwait(false);
        if (status != 200 || body.Length == 0 || contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            LogDetail(
                plugin,
                "Story Shuffle passed a shuffle of {Path} through: Jellyfin answered HTTP {Status} with {Length} bytes of {ContentType}",
                Safe(path),
                status,
                body.Length,
                Safe(contentType ?? "no content type"));
            await WriteRaw(ctx, body).ConfigureAwait(false);
            return;
        }

        var cfg = plugin.Configuration;
        var series = await LookUpSeries(ctx, prefix, body, userId).ConfigureAwait(false);
        byte[] result;
        ShuffleResult? shuffled = null;
        try
        {
            shuffled = ResponseTransformer.Shuffle(body, limit, plugin.GetRuleSet(), cfg.RecentDays, DateTime.UtcNow, Random.Shared, series);
            result = shuffled.Body;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story Shuffle could not group this shuffle; falling back to a normal shuffle");
            try
            {
                result = ResponseTransformer.PlainShuffle(body, limit, Random.Shared);
            }
            catch (Exception ex2)
            {
                _logger.LogWarning(ex2, "Story Shuffle could not read the response");
                result = body;
            }
        }

        if (shuffled is not null)
        {
            LogShuffle(plugin, path, limit, shuffled, series is null, Stopwatch.GetElapsedTime(started));
        }

        await WriteRaw(ctx, result).ConfigureAwait(false);
    }

    private void LogShuffle(Plugin plugin, string path, int? limit, ShuffleResult r, bool lookupFailed, TimeSpan elapsed)
    {
        var detailed = plugin.Configuration.DetailedLog;
        if (!detailed)
        {
            var names = r.Series.Count == 0 ? "(no episodes)" : string.Join(", ", r.Series.Select(s => s.Name.Length > 0 ? s.Name : "(no series)"));
            _logger.LogInformation(
                "Story Shuffle: {Series}, {Episodes} episodes → {Stories} stories, returned {Returned}", names, r.EpisodeCount, r.StoryCount, r.ReturnedCount);
            if (!_logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }
        }

        _logger.Log(
            detailed ? LogLevel.Information : LogLevel.Debug,
            "Story Shuffle: {Series}; {Episodes} episodes → {Stories} stories, {Recent}, returned {Returned} (limit {Limit}) in {Elapsed} ms for {Path}",
            r.Series.Count == 0 ? "(no episodes)" : string.Join("; ", r.Series.Select(s => Describe(s, lookupFailed, withCounts: r.Series.Count > 1))),
            r.EpisodeCount,
            r.StoryCount,
            plugin.Configuration.RecentDays > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{r.RecentCount} watched in the last {plugin.Configuration.RecentDays} days pushed back")
                : "recently watched not pushed back (0 days)",
            r.ReturnedCount,
            limit?.ToString(CultureInfo.InvariantCulture) ?? "none",
            (long)elapsed.TotalMilliseconds,
            Safe(path));
    }

    private static string Describe(ShuffledSeries s, bool lookupFailed, bool withCounts)
    {
        var ids = new List<string> { "id " + (s.Id.Length > 0 ? s.Id : "none") };
        if (s.ProviderIds is not null)
        {
            ids.AddRange(s.ProviderIds.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + " " + kv.Value));
        }
        else if (lookupFailed)
        {
            ids.Add("provider ids not looked up");
        }

        var rules = !s.Enabled ? "story shuffle off for this series"
            : s.Rules.Count == 0 ? "no rules matched"
            : "rules " + string.Join(", ", s.Rules);
        var counts = withCounts ? string.Create(CultureInfo.InvariantCulture, $", {s.EpisodeCount} episodes → {s.StoryCount} stories") : string.Empty;
        return $"{(s.Name.Length > 0 ? s.Name : "(no series)")} [{string.Join(", ", ids)}] {rules}{counts}";
    }

    /// <summary>
    /// Makes request text safe to log: it arrives before Jellyfin checks who is asking, so control characters (fake log
    /// lines) and the characters some log viewers show as a line break or use to reorder text are replaced, and the
    /// length is capped.
    /// </summary>
    private static string Safe(string text)
    {
        const int Max = 200;
        var length = Math.Min(text.Length, Max);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        var chars = text.Substring(0, length).Select(c => Unsafe(c) ? '?' : c).ToArray();
        return new string(chars) + (length < text.Length ? "…" : string.Empty);
    }

    private static bool Unsafe(char c)
        => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Format;

    /// <summary>Logs a line of the detailed shuffle log: Information with "Log every shuffle in detail" on, else Debug.</summary>
    private void LogDetail(Plugin plugin, string message, params object?[] args)
        => _logger.Log(plugin.Configuration.DetailedLog ? LogLevel.Information : LogLevel.Debug, message, args);

    private async Task HandlePreview(HttpContext ctx, Plugin plugin, string prefix)
    {
        var seriesId = First(ctx.Request.Query, "seriesId");
        if (seriesId is null || !GuidOnly.IsMatch(seriesId))
        {
            await WriteJson(ctx, 400, Encoding.UTF8.GetBytes("{\"error\":\"seriesId is required\"}")).ConfigureAwait(false);
            return;
        }

        // Re-route to Jellyfin's own episode list; it does the authentication and permission checks.
        ctx.Request.PathBase = PathString.Empty;
        ctx.Request.Path = new PathString(prefix + "/Shows/" + seriesId + "/Episodes");
        ctx.Request.QueryString = QueryString.Create(new[]
        {
            new KeyValuePair<string, string?>("isMissing", "false"),
            new KeyValuePair<string, string?>("fields", "Overview,ProviderIds"),
            new KeyValuePair<string, string?>("enableImages", "false"),
            new KeyValuePair<string, string?>("enableUserData", "false"),
        });

        var (status, _, body) = await RunBuffered(ctx).ConfigureAwait(false);
        if (status != 200)
        {
            await WriteRaw(ctx, body).ConfigureAwait(false);
            return;
        }

        var series = await LookUpSeries(ctx, prefix, body, null).ConfigureAwait(false);
        byte[] result;
        try
        {
            result = ResponseTransformer.Preview(body, plugin.GetRuleSet(), plugin.RulesError, series);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story Shuffle preview failed");
            result = Encoding.UTF8.GetBytes("{\"error\":\"Preview failed, see the server log.\"}");
        }

        await WriteJson(ctx, 200, result).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks Jellyfin for the series items of an episode list, for their provider ids: episodes don't carry them.
    /// The request goes through Jellyfin's pipeline again, as the same user. The response so far is put back afterwards.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, SeriesInfo>?> LookUpSeries(HttpContext ctx, string prefix, byte[] episodes, string? userId)
    {
        List<string> ids;
        try
        {
            ids = ResponseTransformer.SeriesIds(episodes);
        }
        catch (Exception)
        {
            return null;
        }

        if (ids.Count == 0)
        {
            return new Dictionary<string, SeriesInfo>();
        }

        var request = ctx.Request;
        var response = ctx.Response;
        var (pathBase, path, queryString) = (request.PathBase, request.Path, request.QueryString);
        var status = response.StatusCode;
        var headers = response.Headers.ToList();
        try
        {
            var query = new List<KeyValuePair<string, string?>>
            {
                new("ids", string.Join(',', ids)),
                new("fields", "ProviderIds"),
                new("enableImages", "false"),
                new("enableUserData", "false"),
            };
            if (!string.IsNullOrEmpty(userId))
            {
                query.Add(new("userId", userId));
            }

            request.PathBase = PathString.Empty;
            request.Path = new PathString(prefix + "/Items");
            request.QueryString = QueryString.Create(query);
            response.StatusCode = 200;
            response.Headers.Clear();

            var (lookupStatus, _, body) = await RunBuffered(ctx).ConfigureAwait(false);
            if (lookupStatus != 200)
            {
                _logger.LogWarning("Story Shuffle could not look up the series (HTTP {Status}); only rules by name or Jellyfin id apply", lookupStatus);
                return null;
            }

            return ResponseTransformer.ReadSeries(body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story Shuffle could not look up the series; only rules by name or Jellyfin id apply");
            return null;
        }
        finally
        {
            (request.PathBase, request.Path, request.QueryString) = (pathBase, path, queryString);
            response.StatusCode = status;
            response.Headers.Clear();
            foreach (var kv in headers)
            {
                response.Headers[kv.Key] = kv.Value;
            }
        }
    }

    private async Task<(int Status, string? ContentType, byte[] Body)> RunBuffered(HttpContext ctx)
    {
        // A request that already went through routing keeps its endpoint: clear it so the new path gets routed.
        ctx.SetEndpoint(null);
        ctx.Request.RouteValues = new RouteValueDictionary();

        // Ask for an uncompressed, non-cached answer so the body can be read and rewritten.
        ctx.Request.Headers.Remove(HeaderNames.AcceptEncoding);
        ctx.Request.Headers.Remove(HeaderNames.IfNoneMatch);
        ctx.Request.Headers.Remove(HeaderNames.IfModifiedSince);

        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await _next(ctx).ConfigureAwait(false);
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }

        return (ctx.Response.StatusCode, ctx.Response.ContentType, buffer.ToArray());
    }

    private static async Task WriteRaw(HttpContext ctx, byte[] body)
    {
        if (ctx.Response.HasStarted)
        {
            return;
        }

        ctx.Response.Headers.Remove(HeaderNames.ContentEncoding);
        ctx.Response.Headers.Remove(HeaderNames.ETag);
        ctx.Response.ContentLength = body.Length;
        await ctx.Response.Body.WriteAsync(body).ConfigureAwait(false);
    }

    private static async Task WriteJson(HttpContext ctx, int status, byte[] body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.ContentLength = body.Length;
        await ctx.Response.Body.WriteAsync(body).ConfigureAwait(false);
    }

    private static string MergeFields(IQueryCollection query)
    {
        var fields = new List<string>();
        foreach (var kv in query.Where(kv => Is(kv.Key, "fields")))
        {
            foreach (var v in kv.Value)
            {
                fields.AddRange((v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        // Overview feeds the description heuristic, ProviderIds the "tvdb:…" / "tmdb:…" rule entries.
        fields.Add("Overview");
        fields.Add("ProviderIds");

        return string.Join(',', fields.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool WouldHandle(string path, IQueryCollection query, Configuration.PluginConfiguration cfg)
        => ShowsEpisodes.IsMatch(path)
            || (cfg.InterceptItemsEndpoint && ItemsPath.IsMatch(path) && !string.IsNullOrEmpty(First(query, "parentId")) && OnlyEpisodes(query));

    private static bool OnlyEpisodes(IQueryCollection query)
    {
        var types = Values(query, "includeItemTypes").ToList();
        return types.Count > 0 && types.All(t => Is(t, "Episode"));
    }

    private static bool HasValue(IQueryCollection query, string key, string value)
        => Values(query, key).Any(v => Is(v, value));

    private static IEnumerable<string> Values(IQueryCollection query, string key)
        => query.Where(kv => Is(kv.Key, key))
            .SelectMany(kv => kv.Value)
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string? First(IQueryCollection query, string key)
        => query.Where(kv => Is(kv.Key, key)).SelectMany(kv => kv.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
