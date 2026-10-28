using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>Global defaults applied when no rule says otherwise.</summary>
/// <param name="TitleHeuristic">Link "Part N" titles.</param>
/// <param name="OverviewHeuristic">Link "part two of two" overviews.</param>
/// <param name="ExcludeSpecials">Leave out season 0.</param>
public sealed record RuleDefaults(bool TitleHeuristic = true, bool OverviewHeuristic = true, bool ExcludeSpecials = false);

/// <summary>The effective settings for one series after merging all matching rules.</summary>
public sealed class ResolvedRule
{
    /// <summary>Gets or sets a value indicating whether story shuffle is on for this series.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether title heuristics are on.</summary>
    public bool TitleHeuristic { get; set; }

    /// <summary>Gets or sets a value indicating whether overview heuristics are on.</summary>
    public bool OverviewHeuristic { get; set; }

    /// <summary>Gets or sets a value indicating whether specials are excluded.</summary>
    public bool ExcludeSpecials { get; set; }

    /// <summary>Gets the explicit stories.</summary>
    public List<List<string>> Groups { get; } = new();

    /// <summary>Gets the serialized seasons.</summary>
    public HashSet<int> SeasonsAsStory { get; } = new();

    /// <summary>Gets the titles heuristics never touch.</summary>
    public List<string> NeverGroup { get; } = new();

    /// <summary>Gets the titles never queued.</summary>
    public List<string> Exclude { get; } = new();

    /// <summary>Gets the names of the rules that matched.</summary>
    public List<string> Sources { get; } = new();
}

/// <summary>Built-in plus user rules, resolved per series.</summary>
public sealed class RuleSet
{
    /// <summary>JSON options used for rule files (case-insensitive, comments and trailing commas allowed).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly IReadOnlyList<SeriesRule> _builtIn;
    private readonly IReadOnlyList<SeriesRule> _user;
    private readonly RuleDefaults _defaults;

    /// <summary>Initializes a new instance of the <see cref="RuleSet"/> class.</summary>
    /// <param name="builtIn">Rules shipped with the plugin.</param>
    /// <param name="user">Rules from the plugin settings.</param>
    /// <param name="defaults">Global defaults.</param>
    public RuleSet(IEnumerable<SeriesRule>? builtIn, IEnumerable<SeriesRule>? user, RuleDefaults defaults)
    {
        _builtIn = builtIn?.ToList() ?? new List<SeriesRule>();
        _user = user?.ToList() ?? new List<SeriesRule>();
        _defaults = defaults;
    }

    /// <summary>Parses a JSON array of rules.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The rules (empty for blank input).</returns>
    /// <exception cref="JsonException">The JSON is invalid.</exception>
    public static List<SeriesRule> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<SeriesRule>();
        }

        return JsonSerializer.Deserialize<List<SeriesRule>>(json, JsonOptions) ?? new List<SeriesRule>();
    }

    /// <summary>Resolves the effective rule for a series.</summary>
    /// <param name="seriesName">The series name.</param>
    /// <param name="seriesId">The series id.</param>
    /// <param name="series">The series item (provider ids, year), if it could be looked up.</param>
    /// <returns>The merged rule.</returns>
    public ResolvedRule Resolve(string? seriesName, string? seriesId, SeriesInfo? series = null)
    {
        var user = _user.Where(r => Matches(r, seriesName, seriesId, series)).ToList();
        var replace = user.Any(r => r.ReplaceBuiltIn == true);
        var all = (replace ? Enumerable.Empty<SeriesRule>() : _builtIn.Where(r => Matches(r, seriesName, seriesId, series))).Concat(user);

        var resolved = new ResolvedRule
        {
            TitleHeuristic = _defaults.TitleHeuristic,
            OverviewHeuristic = _defaults.OverviewHeuristic,
            ExcludeSpecials = _defaults.ExcludeSpecials,
        };

        foreach (var r in all)
        {
            resolved.Sources.Add(r.Name ?? string.Join(", ", r.Series));
            resolved.Enabled = r.Enabled ?? resolved.Enabled;
            resolved.TitleHeuristic = r.TitleHeuristic ?? resolved.TitleHeuristic;
            resolved.OverviewHeuristic = r.OverviewHeuristic ?? resolved.OverviewHeuristic;
            resolved.ExcludeSpecials = r.ExcludeSpecials ?? resolved.ExcludeSpecials;
            if (r.Groups is not null)
            {
                resolved.Groups.AddRange(r.Groups.Where(g => g is { Count: > 1 }));
            }

            if (r.SeasonsAsStory is not null)
            {
                resolved.SeasonsAsStory.UnionWith(r.SeasonsAsStory);
            }

            if (r.NeverGroup is not null)
            {
                resolved.NeverGroup.AddRange(r.NeverGroup);
            }

            if (r.Exclude is not null)
            {
                resolved.Exclude.AddRange(r.Exclude);
            }
        }

        return resolved;
    }

    private static bool Matches(SeriesRule rule, string? seriesName, string? seriesId, SeriesInfo? series)
    {
        if (rule.Series is null)
        {
            return false;
        }

        var name = TitleTools.NormalizeSeries(seriesName);
        var year = TitleTools.SeriesYear(seriesName) ?? series?.Year;
        var id = TitleTools.NormalizeId(seriesId);
        foreach (var s in rule.Series)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            // A provider id like "tvdb:78804". When it doesn't match, "CSI:Miami" may still be a name, so carry on.
            if (series is not null && TitleTools.MatchesProviderId(series.ProviderIds, s))
            {
                return true;
            }

            if (id.Length > 0 && TitleTools.NormalizeId(s) == id)
            {
                return true;
            }

            // "Doctor Who" matches every year; "Doctor Who (2005)" only a series from 2005 (or of unknown year).
            var ruleYear = TitleTools.SeriesYear(s);
            if (name.Length > 0 && TitleTools.NormalizeSeries(s) == name && (ruleYear is null || year is null || ruleYear == year))
            {
                return true;
            }
        }

        return false;
    }
}
