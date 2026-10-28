using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>
/// What we know about a series item itself (episodes don't carry their series' provider ids).
/// </summary>
public sealed class SeriesInfo
{
    /// <summary>Gets the series id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the series name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the year the series started, if known.</summary>
    public int? Year { get; init; }

    /// <summary>Gets the provider ids, e.g. "Tvdb" → "78804" (provider names compared case-insensitively).</summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
