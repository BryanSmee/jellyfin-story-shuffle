using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.StoryShuffle.Grouping;

/// <summary>
/// Title normalisation and "Part N" parsing.
/// </summary>
public static class TitleTools
{
    /// <summary>
    /// The words part markers are made of, one entry per language, all accent-free (titles lose their accents
    /// before matching). Adding a language is one more entry here and one in <see cref="StoryGrouper"/>.
    /// </summary>
    public static readonly IReadOnlyList<MarkerLanguage> Languages = new MarkerLanguage[]
    {
        new("English", new[] { "part", @"pt\.?" }, new[] { "episode", @"ep\.?" }, new[] { "chapter" }, new[] { "of" },
            new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" }),
        new("French", new[] { "partie" }, new[] { "episode" }, new[] { "chapitre" }, new[] { "sur" },
            new[] { "un|une", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix" }),
        new("German", new[] { "teil" }, new[] { "folge" }, new[] { "kapitel" }, new[] { "von" },
            new[] { "eins", "zwei", "drei", "vier", "funf|fuenf", "sechs", "sieben", "acht", "neun", "zehn" }),
        new("Spanish", new[] { "parte" }, new[] { "episodio" }, new[] { "capitulo" }, new[] { "de" },
            new[] { "uno|una", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez" }),
        new("Italian", new[] { "parte" }, new[] { "episodio" }, new[] { "capitolo" }, new[] { "di" },
            new[] { "uno|una", "due", "tre", "quattro", "cinque", "sei", "sette", "otto", "nove", "dieci" }),
        new("Dutch", new[] { "deel" }, new[] { "aflevering" }, new[] { "hoofdstuk" }, new[] { "van" },
            new[] { "een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen", "tien" }),
        new("Portuguese", new[] { "parte" }, new[] { "episodio" }, new[] { "capitulo" }, new[] { "de" },
            new[] { "um|uma", "dois|duas", "tres", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez" }),
    };

    private static readonly Dictionary<string, int> NumberWords = BuildNumberWords();

    /// <summary>Digits, Roman numerals or a number word from any language: "2", "II", "two", "zwei".</summary>
    internal static readonly string NumberWordPattern = @"(?:\d{1,2}|[ivx]{1,4}|" + Alternation(NumberWords.Keys) + ")";

    /// <summary>"Part", "Teil", "Episode", "Chapitre"... in any language, not followed by another letter.</summary>
    internal static readonly string KeywordPattern = Words(l => l.Parts.Concat(l.Episodes).Concat(l.Chapters));

    /// <summary>"of", "sur", "von"... as in "Part 1 of 2".</summary>
    internal static readonly string OfWordPattern = Words(l => l.Of);

    /// <summary>"/" or one of the "of" words.</summary>
    internal static readonly string OfPattern = "(?:/|" + OfWordPattern + ")";

    private static readonly string PartKeyword = Words(l => l.Parts);
    private static readonly string LeadingKeyword = Words(l => l.Parts.Concat(l.Chapters));
    private static readonly string NumberPattern = "(?<n>" + NumberWordPattern + ")";

    // Number words count only after a keyword ("Teil eins"), so "Le Casse (Un)" stays a title. English ones also count alone, as they always have.
    private static readonly string BareNumberPattern = @"(?<n>\d{1,2}|[ivx]{1,4}|" + Alternation(Languages.Single(l => l.Name == "English").Numbers.SelectMany(Split)) + ")";

    // "Spyfall (1)", "The End of Time (Part Two)", "Foo [2/3]", "Le Jour du Docteur (Partie 1)"
    private static readonly Regex BracketMarker = new(
        @"^(?<base>.+?)\s*[\(\[]\s*(?:" + KeywordPattern + @"\s*" + NumberPattern + "|" + BareNumberPattern + @")(?:\s*" + OfPattern + @"\s*\w+)?\s*[\)\]]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Cartoon Wars Part I", "Spyfall, Part 1", "The End of Time - Part One", "Imaginationland Episode III", "Die Flut – Teil eins"
    private static readonly Regex TrailingMarker = new(
        @"^(?<base>.+?)[\s,:;.\-–—]+" + KeywordPattern + @"\s*" + NumberPattern + @"(?:\s+" + OfPattern + @"\s+\w+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Spyfall: Part 1 - The Beginning"
    private static readonly Regex MiddleMarker = new(
        @"^(?<base>.+?)[\s,:;.\-–—]+" + PartKeyword + @"\s*" + NumberPattern + @"\s*[:\-–—]\s*.+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // "Chapter One: The Halloween Apocalypse" (only stripped for matching, never used to link)
    private static readonly Regex LeadingChapter = new(
        @"^\s*" + LeadingKeyword + @"\s+" + NumberPattern + @"\s*[:\-–—.]\s*(?<rest>.+)$",
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

        var plain = RemoveAccents(value.Replace("&", " and ", StringComparison.Ordinal));
        var sb = new StringBuilder(plain.Length);
        foreach (var c in plain)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Drops accents and keeps everything else: "À suivre…" becomes "A suivre…".</summary>
    /// <param name="value">The text.</param>
    /// <returns>The text without accents.</returns>
    public static string RemoveAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
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

        var t = RemoveAccents(title.Trim());
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

        var t = RemoveAccents(title.Trim());
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

    /// <summary>Parses 2, "two", "zwei", "II".</summary>
    /// <param name="value">The text.</param>
    /// <returns>The number or null.</returns>
    public static int? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = RemoveAccents(value.Trim()).ToLowerInvariant();
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            return i;
        }

        if (NumberWords.TryGetValue(v, out var word))
        {
            return word;
        }

        return ParseRoman(v);
    }

    private static Dictionary<string, int> BuildNumberWords()
    {
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            for (var n = 0; n < language.Numbers.Count; n++)
            {
                foreach (var word in Split(language.Numbers[n]))
                {
                    if (words.TryGetValue(word, out var other) && other != n + 1)
                    {
                        throw new InvalidOperationException($"\"{word}\" is {other} in one language and {n + 1} in {language.Name}.");
                    }

                    words[word] = n + 1;
                }
            }
        }

        return words;
    }

    private static IEnumerable<string> Split(string words) => words.Split('|');

    // Longest first, so "una" is tried before "un".
    private static string Alternation(IEnumerable<string> words)
        => string.Join("|", words.Distinct().OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal));

    // "part" must not match the start of "partie"; a keyword ending in a dot ("Pt.Two") may be followed by anything.
    private static string Words(Func<MarkerLanguage, IEnumerable<string>> pick)
        => "(?:(?:" + Alternation(Languages.SelectMany(pick)) + @")(?:(?<=\.)|(?![a-z])))";

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

/// <summary>The words one language writes part markers with, as accent-free regex fragments.</summary>
/// <param name="Name">The language, in English.</param>
/// <param name="Parts">"Part", "Teil": also used in "Spyfall: Part 1 - The Beginning".</param>
/// <param name="Episodes">"Episode", "Folge".</param>
/// <param name="Chapters">"Chapter", "Kapitel": also stripped from "Chapter One: ..." titles.</param>
/// <param name="Of">"of", "von", as in "Part 1 of 2".</param>
/// <param name="Numbers">The words for 1 to 10, in order; "un|une" when a number has several.</param>
public sealed record MarkerLanguage(
    string Name,
    IReadOnlyList<string> Parts,
    IReadOnlyList<string> Episodes,
    IReadOnlyList<string> Chapters,
    IReadOnlyList<string> Of,
    IReadOnlyList<string> Numbers);

/// <summary>A parsed part marker.</summary>
/// <param name="Base">Normalised title without the marker.</param>
/// <param name="Number">The part number.</param>
public sealed record PartMarker(string Base, int Number);
