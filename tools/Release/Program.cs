using System;
using System.Collections.Generic;
using System.IO;
using StoryShuffle.Release;

// dotnet run -c Release --project tools/Release -- package  --version V --build-dir DIR --meta FILE --out DIR [--changelog-file FILE]
// dotnet run -c Release --project tools/Release -- manifest --manifest FILE --meta FILE --source-url URL --checksum MD5 [--owner NAME]
// Prints key=value lines meant for $GITHUB_OUTPUT.
try
{
    if (args.Length == 0) { throw new ArgumentException("usage: package|manifest --option value ..."); }
    var opts = new Dictionary<string, string>();
    for (var i = 1; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) { throw new ArgumentException("bad argument: " + args[i]); }
        opts[args[i][2..]] = args[i + 1];
    }

    string Need(string key) => opts.TryGetValue(key, out var v) && v.Length > 0 ? v : throw new ArgumentException("missing --" + key);

    switch (args[0])
    {
        case "package":
            var changelog = opts.TryGetValue("changelog-file", out var cf) ? File.ReadAllText(cf) : null;
            var p = ReleaseTool.Package(Need("version"), Need("build-dir"), Need("meta"), Need("out"), changelog, DateTime.UtcNow);
            Console.WriteLine("zip=" + p.ZipPath);
            Console.WriteLine("meta=" + p.MetaPath);
            Console.WriteLine("checksum=" + p.Checksum);
            Console.WriteLine("timestamp=" + p.Timestamp);
            break;
        case "manifest":
            var count = ReleaseTool.UpdateManifest(Need("manifest"), Need("meta"), Need("source-url"), Need("checksum"), opts.GetValueOrDefault("owner"));
            Console.WriteLine("versions=" + count);
            break;
        default:
            throw new ArgumentException("unknown command: " + args[0]);
    }

    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or InvalidOperationException)
{
    Console.Error.WriteLine("error: " + e.Message);
    return 1;
}
