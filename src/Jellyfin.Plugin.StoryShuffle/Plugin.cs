using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.StoryShuffle.Configuration;
using Jellyfin.Plugin.StoryShuffle.Grouping;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StoryShuffle;

/// <summary>Story Shuffle: shuffles whole stories so multi-part episodes play together.</summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static readonly Regex StringArray = new(@"\[\s*(?:(?<item>""(?:[^""\\]|\\.)*"")\s*,?\s*)+\]", RegexOptions.CultureInvariant);
    private static readonly Lazy<string> BuiltInJsonLazy = new(LoadBuiltInJson);
    private readonly object _lock = new();
    private string? _cacheKey;
    private RuleSet? _ruleSet;

    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the running instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Gets the shipped rules as JSON text: one array holding the rule of every file under <c>BuiltInRules/</c>.</summary>
    public static string BuiltInJson => BuiltInJsonLazy.Value;

    /// <inheritdoc />
    public override string Name => "Story Shuffle";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("1f90710b-d628-40f1-8aa9-614cba01e68e");

    /// <inheritdoc />
    public override string Description => "Makes the shuffle button keep multi-part episodes together, in order.";

    /// <summary>Gets the error from parsing the user's rules, if any.</summary>
    public string? RulesError { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "StoryShuffle",
                DisplayName = "Story Shuffle",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
            },
        };
    }

    /// <summary>Gets the current rule set, rebuilt whenever the settings change.</summary>
    /// <returns>The rule set.</returns>
    public RuleSet GetRuleSet()
    {
        var c = Configuration;
        var key = string.Join('|', c.UseBuiltInRules, c.TitleHeuristic, c.OverviewHeuristic, c.ExcludeSpecials, c.RulesJson);
        lock (_lock)
        {
            if (_ruleSet is not null && _cacheKey == key)
            {
                return _ruleSet;
            }

            List<SeriesRule> user;
            try
            {
                user = RuleSet.Parse(c.RulesJson);
                RulesError = null;
            }
            catch (JsonException ex)
            {
                user = new List<SeriesRule>();
                RulesError = "Your rules are not valid JSON and are being ignored: " + ex.Message;
            }

            var builtIn = c.UseBuiltInRules ? RuleSet.Parse(BuiltInJson) : new List<SeriesRule>();
            _ruleSet = new RuleSet(builtIn, user, new RuleDefaults(c.TitleHeuristic, c.OverviewHeuristic, c.ExcludeSpecials));
            _cacheKey = key;
            return _ruleSet;
        }
    }

    private static string LoadBuiltInJson()
    {
        var asm = typeof(Plugin).Assembly;
        var rules = new JsonArray();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("BuiltInRules/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is not null)
            {
                rules.Add(JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }));
            }
        }

        // Strict JSON for the settings page and API clients; each story kept on one line.
        var json = rules.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return StringArray.Replace(json, m => "[ " + string.Join(", ", m.Groups["item"].Captures.Select(c => c.Value)) + " ]");
    }
}
