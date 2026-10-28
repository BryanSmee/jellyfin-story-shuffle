using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>
/// Title normalisation and "Part N" parsing.
/// </summary>
public static class TitleTools
{
    private const string NumberPattern = @"(?<n>\d{1,2}|[ivx]{1,4}|one|two|three|four|five|six|seven|eight|nine|ten)";
    private const string Keyword = @"(?:part|pt\.?|episode|ep\.?|chapter)";

    // "Spyfall (1)", "The End of Time (Part Two)", "Foo [2/3]"
    private static readonly Regex BracketMarker = new(
        @"^(?<base>.+?)\s*[\(\[]\s*(?:" + Keyword + @"\s*)?" + NumberPattern + @"(?:\s*(?:of|/)\s*\w+)?\s*[\)\]]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Cartoon Wars Part I", "Spyfall, Part 1", "The End of Time - Part One", "Imaginationland Episode III"
    private static readonly Regex TrailingMarker = new(
        @"^(?<base>.+?)[\s,:;.\-–—]+" + Keyword + @"\s*" + NumberPattern + @"(?:\s+of\s+\w+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Spyfall: Part 1 - The Beginning"
    private static readonly Regex MiddleMarker = new(
        @"^(?<base>.+?)[\s,:;.\-–—]+(?:part|pt\.?)\s*" + NumberPattern + @"\s*[:\-–—]\s*.+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Chapter One: The Halloween Apocalypse" (only stripped for matching, never used to link)
    private static readonly Regex LeadingChapter = new(
        @"^\s*(?:chapter|part)\s+" + NumberPattern + @"\s*[:\-–—.]\s*(?<rest>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EpisodeCode = new(
        @"^\s*s(?<s>\d{1,3})\s*e(?<e>\d{1,4})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "tvdb:12345", "tmdb:67890": a provider name, a colon, an id. "Star Trek: Picard" has a space, so it never looks like one.
    private static readonly Regex ProviderRef = new(
        @"^\s*(?<provider>[a-z][a-z0-9]*):(?<id>[^\s:|]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YearSuffix = new(
        @"\s*\((?<year>\d{4})\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Lower-cases, drops accents, turns "&amp;" into "and" and removes everything that is not a letter or digit.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The normalised key.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Normalises a series name, ignoring a "(2005)" style year suffix.</summary>
    /// <param name="value">The series name.</param>
    /// <returns>The normalised key.</returns>
    public static string NormalizeSeries(string? value)
        => Normalize(value is null ? null : YearSuffix.Replace(value, string.Empty));

    /// <summary>Tells whether a rule entry such as "tvdb:12345" names one of these provider ids.</summary>
    /// <param name="providerIds">The item's provider ids (provider names compared case-insensitively).</param>
    /// <param name="reference">The rule entry: a provider name, a colon, an id.</param>
    /// <returns>True when the item has that id.</returns>
    public static bool MatchesProviderId(IReadOnlyDictionary<string, string> providerIds, string reference)
    {
        var m = ProviderRef.Match(reference);
        return m.Success
            && providerIds.TryGetValue(m.Groups["provider"].Value, out var id)
            && string.Equals(id?.Trim(), m.Groups["id"].Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Normalises a Jellyfin item id: no dashes, lower case.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The normalised id.</returns>
    public static string NormalizeId(string? id)
        => (id ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();

    /// <summary>Reads the year from a "Doctor Who (2005)" style series name.</summary>
    /// <param name="value">The series name.</param>
    /// <returns>The year, or null when the name has none.</returns>
    public static int? SeriesYear(string? value)
    {
        var m = value is null ? null : YearSuffix.Match(value);
        return m is { Success: true } ? int.Parse(m.Groups["year"].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Normalises a title with any part marker or leading "Chapter N:" removed.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The normalised key.</returns>
    public static string StripAndNormalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var t = title.Trim();
        var lead = LeadingChapter.Match(t);
        if (lead.Success)
        {
            t = lead.Groups["rest"].Value;
        }

        var part = ParsePart(t);
        return Normalize(part?.Base ?? t);
    }

    /// <summary>Parses a "Part N" marker from a title.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The base title and part number, or null.</returns>
    public static PartMarker? ParsePart(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var t = title.Trim();
        foreach (var regex in new[] { BracketMarker, TrailingMarker, MiddleMarker })
        {
            var m = regex.Match(t);
            if (!m.Success)
            {
                continue;
            }

            var n = ParseNumber(m.Groups["n"].Value);
            var baseKey = Normalize(m.Groups["base"].Value);
            if (n is > 0 && baseKey.Length > 0)
            {
                return new PartMarker(baseKey, n.Value);
            }
        }

        return null;
    }

    /// <summary>Parses "S06E01" style codes.</summary>
    /// <param name="value">The text.</param>
    /// <param name="season">The season.</param>
    /// <param name="episode">The episode.</param>
    /// <returns>True if it was a code.</returns>
    public static bool TryParseCode(string? value, out int season, out int episode)
    {
        season = 0;
        episode = 0;
        if (value is null)
        {
            return false;
        }

        var m = EpisodeCode.Match(value);
        if (!m.Success)
        {
            return false;
        }

        season = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
        episode = int.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Parses 2, "two", "II".</summary>
    /// <param name="value">The text.</param>
    /// <returns>The number or null.</returns>
    public static int? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim().ToLowerInvariant();
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }

        switch (v)
        {
            case "one": return 1;
            case "two": return 2;
            case "three": return 3;
            case "four": return 4;
            case "five": return 5;
            case "six": return 6;
            case "seven": return 7;
            case "eight": return 8;
            case "nine": return 9;
            case "ten": return 10;
        }

        return ParseRoman(v);
    }

    private static int? ParseRoman(string v)
    {
        int total = 0, prev = 0;
        for (var i = v.Length - 1; i >= 0; i--)
        {
            var val = v[i] switch
            {
                'i' => 1,
                'v' => 5,
                'x' => 10,
                _ => 0
            };
            if (val == 0)
            {
                return null;
            }

            total += val < prev ? -val : val;
            prev = Math.Max(prev, val);
        }

        return total > 0 ? total : null;
    }
}

/// <summary>A parsed part marker.</summary>
/// <param name="Base">Normalised title without the marker.</param>
/// <param name="Number">The part number.</param>
public sealed record PartMarker(string Base, int Number);
