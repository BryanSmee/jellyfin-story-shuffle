using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>A story-shuffled response, with what was done to it (for the server log).</summary>
public sealed class ShuffleResult
{
    /// <summary>Gets the new response body.</summary>
    public byte[] Body { get; init; } = Array.Empty<byte>();

    /// <summary>Gets the series in the response, in the order they first appear.</summary>
    public List<ShuffledSeries> Series { get; } = new();

    /// <summary>Gets the number of episodes Jellyfin returned.</summary>
    public int EpisodeCount { get; init; }

    /// <summary>Gets the number of stories they became (excluded episodes left out).</summary>
    public int StoryCount { get; init; }

    /// <summary>Gets the number of stories pushed to the back because they were watched recently.</summary>
    public int RecentCount { get; init; }

    /// <summary>Gets the number of episodes in the returned queue.</summary>
    public int ReturnedCount { get; init; }
}

/// <summary>One series of a shuffled response.</summary>
public sealed class ShuffledSeries
{
    /// <summary>Gets the series name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the Jellyfin series id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the series' provider ids, or null when the series couldn't be looked up.</summary>
    public IReadOnlyDictionary<string, string>? ProviderIds { get; init; }

    /// <summary>Gets the names of the rules that applied.</summary>
    public IReadOnlyList<string> Rules { get; init; } = Array.Empty<string>();

    /// <summary>Gets a value indicating whether story shuffle was on for this series.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the number of episodes of this series.</summary>
    public int EpisodeCount { get; init; }

    /// <summary>Gets the number of stories they became.</summary>
    public int StoryCount { get; init; }
}
