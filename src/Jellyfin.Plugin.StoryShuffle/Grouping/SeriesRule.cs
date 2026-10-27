using System.Collections.Generic;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>
/// Per-series behaviour. Every property is optional; anything left null falls back to the global defaults.
/// Several rules can match one series (built-in + yours); they are merged, yours last.
/// </summary>
public sealed class SeriesRule
{
    /// <summary>Gets or sets a label shown in the preview.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets series names (year suffix ignored) or Jellyfin series ids this rule applies to.</summary>
    public List<string> Series { get; set; } = new();

    /// <summary>Gets or sets whether story shuffle is used for this series at all.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Gets or sets whether "Part 1 / (2) / Episode II" style titles are linked.</summary>
    public bool? TitleHeuristic { get; set; }

    /// <summary>Gets or sets whether overviews like "Part two of two" or "Conclusion of..." are linked.</summary>
    public bool? OverviewHeuristic { get; set; }

    /// <summary>Gets or sets whether season 0 (specials) is left out of the shuffle.</summary>
    public bool? ExcludeSpecials { get; set; }

    /// <summary>Gets or sets whether the built-in rules for this series are ignored (user rules only).</summary>
    public bool? ReplaceBuiltIn { get; set; }

    /// <summary>
    /// Gets or sets explicit stories. Each entry is a list of episode titles or codes like "S06E01", in play order.
    /// </summary>
    public List<List<string>> Groups { get; set; } = new();

    /// <summary>Gets or sets season numbers that play as one continuous story (fully serialized seasons).</summary>
    public List<int> SeasonsAsStory { get; set; } = new();

    /// <summary>Gets or sets titles or codes that heuristics must never link to anything.</summary>
    public List<string> NeverGroup { get; set; } = new();

    /// <summary>Gets or sets titles or codes that are never queued.</summary>
    public List<string> Exclude { get; set; } = new();
}
