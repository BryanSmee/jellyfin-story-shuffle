using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StoryShuffle.Configuration;

/// <summary>Plugin settings.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets a value indicating whether the shuffle button is taken over.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether generic "random episodes under a parent" requests are handled too
    /// (used by some third-party and TV clients).
    /// </summary>
    public bool InterceptItemsEndpoint { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the shipped Doctor Who / South Park lists are used.</summary>
    public bool UseBuiltInRules { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether "Part 1 / (2)" titles are linked by default.</summary>
    public bool TitleHeuristic { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether "part two of two" overviews are linked by default.</summary>
    public bool OverviewHeuristic { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether specials are left out by default.</summary>
    public bool ExcludeSpecials { get; set; }

    /// <summary>Gets or sets how many days a watched story is pushed to the back of the queue (0 = off).</summary>
    public int RecentDays { get; set; } = 14;

    /// <summary>
    /// Gets or sets a value indicating whether every shuffle and passed-through shuffle request is logged in detail at
    /// Information level (otherwise those lines are Debug).
    /// </summary>
    public bool DetailedLog { get; set; }

    /// <summary>Gets or sets the user's own per-series rules as a JSON array.</summary>
    public string RulesJson { get; set; } = "[]";
}
