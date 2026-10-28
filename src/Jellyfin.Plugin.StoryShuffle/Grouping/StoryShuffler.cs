using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>Shuffles whole stories and flattens them into a play queue.</summary>
public static class StoryShuffler
{
    /// <summary>Shuffles stories; stories played within <paramref name="recentDays"/> go to the back.</summary>
    /// <param name="stories">The stories.</param>
    /// <param name="recentDays">Days a story counts as recently watched (0 = off).</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="rng">Random source.</param>
    /// <returns>Shuffled stories.</returns>
    public static List<Story> Shuffle(IReadOnlyList<Story> stories, int recentDays, DateTime nowUtc, Random rng)
        => Shuffle(stories, recentDays, nowUtc, rng, out _);

    /// <summary>Shuffles stories; stories played within <paramref name="recentDays"/> go to the back.</summary>
    /// <param name="stories">The stories.</param>
    /// <param name="recentDays">Days a story counts as recently watched (0 = off).</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="rng">Random source.</param>
    /// <param name="recentCount">How many stories went to the back.</param>
    /// <returns>Shuffled stories.</returns>
    public static List<Story> Shuffle(IReadOnlyList<Story> stories, int recentDays, DateTime nowUtc, Random rng, out int recentCount)
    {
        var fresh = new List<Story>();
        var recent = new List<Story>();
        var cutoff = nowUtc.AddDays(-Math.Max(0, recentDays));
        foreach (var s in stories)
        {
            var last = s.LastPlayed;
            if (recentDays > 0 && last is not null && ToUtc(last.Value) >= cutoff)
            {
                recent.Add(s);
            }
            else
            {
                fresh.Add(s);
            }
        }

        FisherYates(fresh, rng);
        FisherYates(recent, rng);
        recentCount = recent.Count;
        fresh.AddRange(recent);
        return fresh;
    }

    /// <summary>Flattens stories into a queue without ever cutting a story in half.</summary>
    /// <param name="stories">Shuffled stories.</param>
    /// <param name="limit">Maximum queue length the client asked for, or null.</param>
    /// <returns>The queue.</returns>
    public static List<EpisodeInfo> Flatten(IEnumerable<Story> stories, int? limit)
    {
        var queue = new List<EpisodeInfo>();
        foreach (var s in stories)
        {
            if (limit is > 0 && queue.Count > 0 && queue.Count + s.Episodes.Count > limit.Value)
            {
                if (queue.Count >= limit.Value)
                {
                    break;
                }

                // Too long to fit; try a shorter story instead.
                continue;
            }

            queue.AddRange(s.Episodes);
        }

        return queue;
    }

    private static DateTime ToUtc(DateTime d) => d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();

    private static void FisherYates<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
