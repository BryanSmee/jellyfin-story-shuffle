using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>A run of episodes that must play together, in order.</summary>
public sealed class Story
{
    /// <summary>Gets the episodes in play order.</summary>
    public List<EpisodeInfo> Episodes { get; } = new();

    /// <summary>Gets why these episodes were linked (rule, title, overview, season).</summary>
    public SortedSet<string> Reasons { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the most recent time any part was played.</summary>
    public DateTime? LastPlayed => Episodes.Max(e => e.LastPlayed);
}

/// <summary>Turns a flat episode list into stories.</summary>
public static class StoryGrouper
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const string Num = @"(?:\d{1,2}|one|two|three|four|five|six|i|ii|iii|iv|v|vi)";

    // "Part two of two", "part 1 of 3", "episode II of III"
    private static readonly Regex PartOf = new(
        @"\b(?:part|pt\.?|episode|chapter)\s*(?<n>" + Num + @")\s+(?:of|/)\s+(?<m>" + Num + @")\b", Opts);

    // Overview of the *later* episode points back.
    private static readonly Regex LinksBack = new(
        @"^\W*(?:the\s+)?(?:conclusion|concluding\s+(?:part|episode|chapter)|second\s+(?:part|half)|final\s+part)\b"
        + @"|\b(?:continued|continuing|continues)\s+from\s+(?:the\s+)?(?:previous|last)\s+(?:episode|week)\b"
        + @"|\bconclusion\s+(?:of|to)\s+(?:the|a|this)\s+(?:two|three|four|2|3|4)[-\s]part(?:er)?\b"
        + @"|\b(?:second|final|last|concluding)\s+(?:part|half|installment|instalment|episode|chapter)\s+of\s+(?:the|a|this)\s+(?:two|three|four|2|3|4)[-\s]part(?:er)?\b",
        Opts);

    // Overview of the *earlier* episode points forward.
    private static readonly Regex LinksForward = new(
        @"\bto\s+be\s+continued\b"
        + @"|\b(?:first|opening)\s+(?:part|half|installment|instalment|episode|chapter)\s+of\s+(?:the|a|this)\s+(?:two|three|four|2|3|4)[-\s]part(?:er)?\b",
        Opts);

    /// <summary>Groups episodes into stories.</summary>
    /// <param name="items">The items from the server, any order.</param>
    /// <param name="rule">The resolved rule for the series.</param>
    /// <param name="unresolved">Optional list that receives rule entries not found in this series.</param>
    /// <returns>Stories in broadcast order of their first episode.</returns>
    public static List<Story> Group(IReadOnlyList<EpisodeInfo> items, ResolvedRule rule, List<string>? unresolved = null)
    {
        var excludeKeys = rule.Exclude.Select(TitleTools.Normalize).Where(k => k.Length > 0).ToHashSet();
        var eps = items
            .Where(e => !(rule.ExcludeSpecials && e.Season == 0 && IsEpisode(e)))
            .Where(e => !MatchesAny(e, rule.Exclude, excludeKeys))
            .OrderBy(e => IsEpisode(e) ? 0 : 1)
            .ThenBy(e => e.Season is null or 0 ? 1 : 0)
            .ThenBy(e => e.Season ?? 0)
            .ThenBy(e => e.Episode ?? int.MaxValue)
            .ThenBy(e => e.Index)
            .ToList();

        var uf = new UnionFind(eps.Count);
        var explicitPos = new Dictionary<int, (int Group, int Pos)>();

        // 1. Explicit stories from rules.
        for (var g = 0; g < rule.Groups.Count; g++)
        {
            var used = new HashSet<int>();
            var resolved = new List<int>();
            foreach (var reference in rule.Groups[g])
            {
                var idx = Find(eps, reference, used);
                if (idx is null)
                {
                    unresolved?.Add(reference);
                    continue;
                }

                used.Add(idx.Value);
                resolved.Add(idx.Value);
            }

            if (resolved.Count < 2)
            {
                continue;
            }

            for (var p = 0; p < resolved.Count; p++)
            {
                explicitPos.TryAdd(resolved[p], (g, p));
                if (p > 0)
                {
                    uf.Union(resolved[p - 1], resolved[p], "rule");
                }
            }
        }

        // 2. Fully serialized seasons.
        foreach (var season in rule.SeasonsAsStory)
        {
            var members = Enumerable.Range(0, eps.Count).Where(i => IsEpisode(eps[i]) && eps[i].Season == season).ToList();
            for (var p = 1; p < members.Count; p++)
            {
                uf.Union(members[p - 1], members[p], "season");
            }
        }

        // 3. Heuristics on neighbours in broadcast order.
        var neverKeys = rule.NeverGroup.Select(TitleTools.Normalize).Where(k => k.Length > 0).ToHashSet();
        for (var i = 1; i < eps.Count; i++)
        {
            var a = eps[i - 1];
            var b = eps[i];
            if (!IsEpisode(a) || !IsEpisode(b) || (a.Season == 0) != (b.Season == 0))
            {
                continue;
            }

            if (MatchesAny(a, rule.NeverGroup, neverKeys) || MatchesAny(b, rule.NeverGroup, neverKeys))
            {
                continue;
            }

            if (rule.TitleHeuristic)
            {
                var pa = TitleTools.ParsePart(a.Name);
                var pb = TitleTools.ParsePart(b.Name);
                if (pa is not null && pb is not null && pa.Base == pb.Base && pb.Number == pa.Number + 1)
                {
                    uf.Union(i - 1, i, "title");
                    continue;
                }
            }

            if (rule.OverviewHeuristic && a.Season == b.Season && OverviewLinks(a.Overview, b.Overview))
            {
                uf.Union(i - 1, i, "overview");
            }
        }

        // Build stories.
        var byRoot = new Dictionary<int, List<int>>();
        for (var i = 0; i < eps.Count; i++)
        {
            var root = uf.Find(i);
            if (!byRoot.TryGetValue(root, out var list))
            {
                byRoot[root] = list = new List<int>();
            }

            list.Add(i);
        }

        var stories = new List<(int First, Story Story)>();
        foreach (var (root, members) in byRoot)
        {
            var groupIds = members.Select(m => explicitPos.TryGetValue(m, out var pos) ? pos.Group : -1).Distinct().ToList();
            List<int> ordered = groupIds.Count == 1 && groupIds[0] >= 0
                ? members.OrderBy(m => explicitPos[m].Pos).ToList()
                : members.OrderBy(m => m).ToList();

            var story = new Story();
            story.Episodes.AddRange(ordered.Select(m => eps[m]));
            foreach (var r in uf.Reasons(root))
            {
                story.Reasons.Add(r);
            }

            stories.Add((members.Min(), story));
        }

        return stories.OrderBy(s => s.First).Select(s => s.Story).ToList();
    }

    /// <summary>Checks whether two neighbouring overviews say they belong together.</summary>
    /// <param name="first">Overview of the earlier episode.</param>
    /// <param name="second">Overview of the later episode.</param>
    /// <returns>True if linked.</returns>
    public static bool OverviewLinks(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(second))
        {
            var m = PartOf.Match(second);
            if (m.Success && TitleTools.ParseNumber(m.Groups["n"].Value) is > 1)
            {
                return true;
            }

            if (LinksBack.IsMatch(second))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(first))
        {
            var m = PartOf.Match(first);
            if (m.Success
                && TitleTools.ParseNumber(m.Groups["n"].Value) is int n
                && TitleTools.ParseNumber(m.Groups["m"].Value) is int total
                && n < total)
            {
                return true;
            }

            if (LinksForward.IsMatch(first))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEpisode(EpisodeInfo e) => string.Equals(e.Type, "Episode", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAny(EpisodeInfo e, List<string> refs, HashSet<string> keys)
    {
        if (refs.Count == 0)
        {
            return false;
        }

        foreach (var r in refs)
        {
            if (TitleTools.TryParseCode(r, out var s, out var ep) && e.Season == s && e.Episode == ep)
            {
                return true;
            }
        }

        var name = TitleTools.Normalize(e.Name);
        return keys.Contains(name) || keys.Contains(TitleTools.StripAndNormalize(e.Name));
    }

    private static int? Find(List<EpisodeInfo> eps, string reference, HashSet<int> used)
    {
        if (TitleTools.TryParseCode(reference, out var season, out var episode))
        {
            for (var i = 0; i < eps.Count; i++)
            {
                if (!used.Contains(i) && IsEpisode(eps[i]) && eps[i].Season == season && eps[i].Episode == episode)
                {
                    return i;
                }
            }

            return null;
        }

        var exact = TitleTools.Normalize(reference);
        var stripped = TitleTools.StripAndNormalize(reference);
        if (exact.Length == 0)
        {
            return null;
        }

        // Pass 1: exact title. Pass 2: ignoring part markers / "Chapter N:". Pass 3: title ends with the reference.
        for (var pass = 1; pass <= 3; pass++)
        {
            for (var i = 0; i < eps.Count; i++)
            {
                if (used.Contains(i) || !IsEpisode(eps[i]))
                {
                    continue;
                }

                var hit = pass switch
                {
                    1 => TitleTools.Normalize(eps[i].Name) == exact,
                    2 => stripped.Length > 0 && TitleTools.StripAndNormalize(eps[i].Name) == stripped,
                    _ => exact.Length >= 8 && TitleTools.Normalize(eps[i].Name).EndsWith(exact, StringComparison.Ordinal),
                };
                if (hit)
                {
                    return i;
                }
            }
        }

        return null;
    }

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly Dictionary<int, HashSet<string>> _reasons = new();

        public UnionFind(int n)
        {
            _parent = Enumerable.Range(0, n).ToArray();
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }

            return x;
        }

        public void Union(int a, int b, string reason)
        {
            var ra = Find(a);
            var rb = Find(b);
            var set = Take(ra);
            if (ra != rb)
            {
                set.UnionWith(Take(rb));
                _reasons.Remove(rb);
                _parent[rb] = ra;
            }

            set.Add(reason);
            _reasons[ra] = set;
        }

        public IEnumerable<string> Reasons(int root)
            => _reasons.TryGetValue(root, out var s) ? s : Enumerable.Empty<string>();

        private HashSet<string> Take(int root)
            => _reasons.TryGetValue(root, out var s) ? s : new HashSet<string>(StringComparer.Ordinal);
    }
}
