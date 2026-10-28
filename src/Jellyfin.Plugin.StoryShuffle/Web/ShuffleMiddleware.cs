using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.StoryShuffle.Grouping;
using Microsoft.AspNetCore.Http;
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

    private static readonly Regex ShowsEpisodes = new(@"/Shows/(?<id>" + Guid + @")/Episodes/?$", Opts);
    private static readonly Regex ItemsPath = new(@"/(?:Users/[^/]+/)?Items/?$", Opts);
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
        if (cfg.Enabled && HasValue(query, "sortBy", "Random"))
        {
            if (ShowsEpisodes.IsMatch(path))
            {
                await HandleShuffle(ctx, plugin, isItemsEndpoint: false).ConfigureAwait(false);
                return;
            }

            if (cfg.InterceptItemsEndpoint
                && ItemsPath.IsMatch(path)
                && !string.IsNullOrEmpty(First(query, "parentId"))
                && OnlyEpisodes(query))
            {
                await HandleShuffle(ctx, plugin, isItemsEndpoint: true).ConfigureAwait(false);
                return;
            }
        }

        await _next(ctx).ConfigureAwait(false);
    }

    private async Task HandleShuffle(HttpContext ctx, Plugin plugin, bool isItemsEndpoint)
    {
        var original = ctx.Request.Query;
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
            await WriteRaw(ctx, body).ConfigureAwait(false);
            return;
        }

        var cfg = plugin.Configuration;
        byte[] result;
        try
        {
            result = ResponseTransformer.Shuffle(body, limit, plugin.GetRuleSet(), cfg.RecentDays, DateTime.UtcNow, Random.Shared);
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

        await WriteRaw(ctx, result).ConfigureAwait(false);
    }

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

        byte[] result;
        try
        {
            result = ResponseTransformer.Preview(body, plugin.GetRuleSet(), plugin.RulesError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Story Shuffle preview failed");
            result = Encoding.UTF8.GetBytes("{\"error\":\"Preview failed, see the server log.\"}");
        }

        await WriteJson(ctx, 200, result).ConfigureAwait(false);
    }

    private async Task<(int Status, string? ContentType, byte[] Body)> RunBuffered(HttpContext ctx)
    {
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
