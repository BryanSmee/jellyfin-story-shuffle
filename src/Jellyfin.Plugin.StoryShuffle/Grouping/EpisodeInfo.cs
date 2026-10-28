using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>
/// The minimum we need to know about one item in a shuffle response.
/// </summary>
public sealed class EpisodeInfo
{
    /// <summary>Gets the position of the item in the server response (used to map back to JSON).</summary>
    public int Index { get; init; }

    /// <summary>Gets the item id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the item type, e.g. "Episode".</summary>
    public string Type { get; init; } = "Episode";

    /// <summary>Gets the episode title.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the episode overview, if the server sent one.</summary>
    public string? Overview { get; init; }

    /// <summary>Gets the season number (0 = specials).</summary>
    public int? Season { get; init; }

    /// <summary>Gets the episode number within the season.</summary>
    public int? Episode { get; init; }

    /// <summary>Gets the series id.</summary>
    public string SeriesId { get; init; } = string.Empty;

    /// <summary>Gets the series name.</summary>
    public string SeriesName { get; init; } = string.Empty;

    /// <summary>Gets the provider ids (provider name such as "Tvdb" or "Tmdb" to id), provider names compared case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets when the current user last played this item.</summary>
    public DateTime? LastPlayed { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"S{Season:00}E{Episode:00} {Name}";
}
