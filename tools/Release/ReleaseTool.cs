using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StoryShuffle.Release
{
    /// <summary>What <see cref="ReleaseTool.Package"/> produced.</summary>
    public sealed record PackageResult(string ZipPath, string MetaPath, string Checksum, string Timestamp);

    /// <summary>Release packaging and Jellyfin plugin repository manifest, kept out of shell so it runs anywhere dotnet does.</summary>
    public static class ReleaseTool
    {
        public const string DllName = "Jellyfin.Plugin.StoryShuffle.dll";

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>Accepts "1.2.3.4" or "v1.2.3.4"; returns "1.2.3.4".</summary>
        public static string ParseVersion(string tagOrVersion)
        {
            var v = tagOrVersion.StartsWith('v') ? tagOrVersion[1..] : tagOrVersion;
            if (!Regex.IsMatch(v, @"^\d+\.\d+\.\d+\.\d+$"))
            {
                throw new ArgumentException($"version must look like 1.2.3.4 (tag v1.2.3.4), got '{tagOrVersion}'");
            }

            return Version.Parse(v).ToString();
        }

        /// <summary>
        /// Writes outDir/meta.json (version, timestamp and changelog stamped) and outDir/StoryShuffle_{version}.zip
        /// with the DLL and that meta.json at its root. Refuses a DLL whose assembly version isn't <paramref name="version"/>.
        /// </summary>
        public static PackageResult Package(string version, string buildDir, string metaPath, string outDir, string? changelog, DateTime utcNow)
        {
            version = ParseVersion(version);
            var dll = Path.Combine(buildDir, DllName);
            if (!File.Exists(dll)) { throw new IOException("not found: " + dll); }
            var built = AssemblyName.GetAssemblyName(dll).Version?.ToString();
            if (built != version)
            {
                throw new InvalidOperationException($"{DllName} has assembly version {built}, expected {version} (build with -p:AssemblyVersion={version})");
            }

            var meta = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
            var timestamp = utcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            meta["version"] = version;
            meta["timestamp"] = timestamp;
            if (!string.IsNullOrWhiteSpace(changelog)) { meta["changelog"] = changelog.Trim(); }

            Directory.CreateDirectory(outDir);
            var outMeta = Path.Combine(outDir, "meta.json");
            File.WriteAllText(outMeta, meta.ToJsonString(Indented) + "\n");

            var zip = Path.Combine(outDir, $"StoryShuffle_{version}.zip");
            File.Delete(zip);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(dll, DllName);
                archive.CreateEntryFromFile(outMeta, "meta.json");
            }

            string checksum;
            using (var s = File.OpenRead(zip))
            {
                checksum = Convert.ToHexString(MD5.HashData(s)).ToLowerInvariant();
            }

            return new PackageResult(Path.GetFullPath(zip), Path.GetFullPath(outMeta), checksum, timestamp);
        }

        /// <summary>
        /// Adds the version described by a packaged meta.json to a Jellyfin plugin repository manifest
        /// (created if missing). Other versions are kept; the same version is replaced. Returns the number of versions.
        /// </summary>
        public static int UpdateManifest(string manifestPath, string metaPath, string sourceUrl, string checksum, string? owner)
        {
            var meta = JsonNode.Parse(File.ReadAllText(metaPath))!.AsObject();
            string Meta(string key) => meta[key]?.GetValue<string>() ?? throw new InvalidOperationException($"meta.json has no '{key}'");
            var guid = Meta("guid");
            var version = ParseVersion(Meta("version"));

            var text = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : string.Empty;
            var manifest = string.IsNullOrWhiteSpace(text) ? new JsonArray() : JsonNode.Parse(text)!.AsArray();
            var package = manifest.OfType<JsonObject>()
                .FirstOrDefault(p => string.Equals(p["guid"]?.GetValue<string>(), guid, StringComparison.OrdinalIgnoreCase));
            if (package is null)
            {
                package = new JsonObject { ["guid"] = guid };
                manifest.Add(package);
            }

            package["name"] = Meta("name");
            package["overview"] = Meta("overview");
            package["description"] = Meta("description");
            package["owner"] = string.IsNullOrEmpty(owner) ? Meta("owner") : owner;
            package["category"] = Meta("category");

            var versions = (package["versions"] as JsonArray)?.OfType<JsonObject>()
                .Where(v => v["version"]?.GetValue<string>() != version)
                .Select(v => v.DeepClone().AsObject())
                .ToList() ?? new();
            versions.Add(new JsonObject
            {
                ["version"] = version,
                ["changelog"] = Meta("changelog"),
                ["targetAbi"] = Meta("targetAbi"),
                ["sourceUrl"] = sourceUrl,
                ["checksum"] = checksum,
                ["timestamp"] = Meta("timestamp"),
            });
            package["versions"] = new JsonArray(versions
                .OrderByDescending(v => Version.TryParse(v["version"]?.GetValue<string>(), out var parsed) ? parsed : new Version())
                .Select(v => (JsonNode?)v)
                .ToArray());

            var dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(manifestPath, manifest.ToJsonString(Indented) + "\n");
            return versions.Count;
        }
    }
}
