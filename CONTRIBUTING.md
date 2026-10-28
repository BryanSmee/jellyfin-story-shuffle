# Contributing to Story Shuffle

Thanks for helping. The [README](README.md) says what the plugin does for users; this file is for everyone who works on it.

## Ways to help

- **Report a wrongly linked story.** Two episodes that play together but shouldn't, or a story that gets split. Add it to the [Bug reports inbox](https://github.com/BryanSmee/jellyfin-story-shuffle/issues/4) or [open an issue](https://github.com/BryanSmee/jellyfin-story-shuffle/issues/new). Include the series, your Jellyfin version, the metadata provider and language of your library, and the *Preview* output for that series (**Dashboard → Plugins → Story Shuffle → Check a series**).
- **Suggest a series** that should ship with a built-in story list: [open an issue](https://github.com/BryanSmee/jellyfin-story-shuffle/issues/new) with the series and its multi-part stories.
- **Send a pull request.** Adding a series is the most common one: see [Adding a series](#adding-a-series).

## Build and test

You need the .NET 8 SDK and nothing else. Run both checks before you push; CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) runs the same two on every pull request.

1. `dotnet build -c Release src/Jellyfin.Plugin.StoryShuffle`: must build without errors.
2. `dotnet run -c Release --project tests/StoryShuffle.Tests`: must print `ALL n CHECKS PASSED` (exit code 0). It loads the Release DLL from step 1, so always rebuild first.

After renaming or deleting a file under `BuiltInRules/`, build with `--no-incremental`: an incremental build keeps the old file embedded in the DLL.

The test program builds Jellyfin's types from their real source, loads the plugin and runs it end to end against a local web server that plays the part of Jellyfin, with a fake library ([`tests/StoryShuffle.Tests/Library.cs`](tests/StoryShuffle.Tests/Library.cs)).

Rules for the build:

- **No NuGet.** [`nuget.config`](nuget.config) clears every feed. Never add a `PackageReference`; use the SDK and the shared frameworks only.
- **[`ref/`](ref)** holds compile-only stand-ins for the few Jellyfin 12 types the plugin uses. Add only the members the plugin uses, with Jellyfin's exact signatures.
- **[`tests/realref/`](tests/realref)** holds Jellyfin 12.4's real source, which the tests compile against instead of `ref/`. Copy from upstream, don't invent.

### Trying a build on your server

Each CI run keeps the built DLL and `meta.json` as an artifact named `StoryShuffle-<commit>`: open the run from the pull request's checks, download the artifact, stop Jellyfin and unzip it into a folder of its own under Jellyfin's `plugins` folder (`plugins/StoryShuffle_dev/`, say), in place of the installed version. Start Jellyfin and reload the web client. A local build works the same way, from `src/Jellyfin.Plugin.StoryShuffle/bin/Release/net8.0/`.

## Adding a series

A built-in list says which episodes of one series form a story. Lists live in [`src/Jellyfin.Plugin.StoryShuffle/BuiltInRules/`](src/Jellyfin.Plugin.StoryShuffle/BuiltInRules), one file per series. Every `*.json` in that folder is embedded in the plugin ([`Jellyfin.Plugin.StoryShuffle.csproj`](src/Jellyfin.Plugin.StoryShuffle/Jellyfin.Plugin.StoryShuffle.csproj)), so a new file needs no code change.

### 1. Create the file

Name it after the series, in lower case with dashes, and add the year when another series has the same name: `BuiltInRules/<series-slug>.json` (`doctor-who-2005.json`, `south-park.json`). It holds **one rule object**, not an array. Comments (`//`) and trailing commas are allowed.

A worked example for a made-up series, *Example Show* (the ids are placeholders: use the real ones, see [Finding the ids](#finding-the-ids)):

```jsonc
// Example Show multi-part stories. Ids from TheTVDB's "Example Show", aired order.
// Each episode is "tvdb:<TheTVDB episode id> | <English title>": the id is tried first, so the list works whatever
// the metadata language (Jellyfin's TMDB provider stores the TVDB id too); the title is the fallback.
{
  "name": "Example Show stories",
  "series": [ "tvdb:123456", "tmdb:7890", "imdb:tt0123456" ],
  "overviewHeuristic": false,
  "groups": [
    [ "tvdb:1000001 | The Long Night (1)", "tvdb:1000002 | The Long Night (2)" ],
    [ "tvdb:1000101 | Into the Storm", "tvdb:1000102 | Eye of the Storm", "tvdb:1000103 | After the Storm" ]
  ]
}
```

`name` is what *Preview* shows under "Rules used". The other fields are below.

### 2. Name the series by provider id, never by name

`series` lists the series' TheTVDB, TMDB and IMDb ids as `tvdb:<id>`, `tmdb:<id>` and `imdb:tt<id>`. Give all three that exist: a library scraped with only one provider still finds the list. Built-in lists never match a series by its name.

The reason is remakes and reboots. Jellyfin has three series called *Doctor Who*: the 1963 series, the 2005 one and the 2023 one. Shown in Jellyfin, the 2023 series is often plain "Doctor Who" with production year 2024, and each library names them differently. A list naming "Doctor Who" would catch all three and link 2005 stories into the 1963 series; the provider ids of each series are the only thing that tells them apart. [`doctor-who-2005.json`](src/Jellyfin.Plugin.StoryShuffle/BuiltInRules/doctor-who-2005.json) and [`doctor-who-2023.json`](src/Jellyfin.Plugin.StoryShuffle/BuiltInRules/doctor-who-2023.json) are the two real examples.

A series that isn't identified yet, with none of those ids, gets no built-in list at all. That's on purpose.

### 3. List the episodes

Each episode is written `"tvdb:<TheTVDB episode id> | <English title>"`. The plugin looks for the TVDB id first, so the list works whatever the library's language and however its provider numbers the seasons; both the TheTVDB plugin and Jellyfin's built-in TMDB provider store that id. The English title is the fallback for episodes that don't have it, and it tells a reviewer which episode is meant. A test checks that every built-in entry has this form.

Don't use `S01E02` codes in a built-in list: providers and libraries number seasons and specials differently.

The fields:

- **`groups`**: the stories, each a list of episodes in play order. A story can span seasons. This is what most lists need, and every built-in list has at least one group (a test checks it).
- **`seasonsAsStory`**: season numbers (as `[3, 4]`) that play as one story from start to end, for fully serialized seasons. Season numbers differ between providers, so prefer `groups` in a built-in list.
- **`neverGroup`**: episodes the title and description heuristics must never link to anything, for an episode they get wrong ("Part 2" in a title that isn't a sequel).
- **`exclude`**: episodes never queued at all.
- **`overviewHeuristic`**: set it to `false` when your list covers every multi-part story of the series. Descriptions like "To be continued" then can't add false links. Leave it out when the list is partial and the descriptions should still find the rest.
- **`titleHeuristic`**, **`excludeSpecials`**, **`enabled`**: also allowed, see the README's [Settings](README.md#settings). Built-in lists rarely need them.

### Finding the ids

- **TheTVDB series id**: on the series page on [thetvdb.com](https://thetvdb.com), listed as "TheTVDB.com Series ID".
- **TheTVDB episode id**: open the episode on thetvdb.com; the number at the end of its address (`…/episodes/295305`) is the id. Use the series' "Aired Order" pages.
- **TMDB series id**: the number in the series' address on [themoviedb.org](https://www.themoviedb.org) (`/tv/57243-doctor-who` is `tmdb:57243`).
- **IMDb series id**: the `tt…` part of the series' address on [imdb.com](https://www.imdb.com).

In Jellyfin, *Edit metadata* on a series or an episode shows the ids it has under "External IDs", which is a quick way to check them against a real library.

### 4. Add a check and update the README

In [`tests/StoryShuffle.Tests/Program.cs`](tests/StoryShuffle.Tests/Program.cs):

- Raise the count in the check *"every file under BuiltInRules/ is loaded, served as strict JSON"* (`builtIn.Count == 3`) by one.
- Add a `Check(...)` that the series finds its list by provider id and that a story is linked by TVDB id, whatever the titles. For *Example Show*, next to the other built-in checks:

  ```csharp
  // Example Show: the built-in list is found by the series' TVDB id and links a story by TVDB episode ids, whatever the titles.
  {
      var rules = new RuleSet(RuleSet.Parse(Plugin.BuiltInJson), null, new RuleDefaults());
      var show = new SeriesInfo { Year = 2010, ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = "123456" } };
      var rule = rules.Resolve("Exemple de série", "x", show);
      EpisodeInfo Ep(int n, string tvdb) => new()
      {
          Index = n, Id = "ex" + n, Name = "Épisode " + n, Season = 1, Episode = n,
          ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tvdb"] = tvdb },
      };
      var stories = StoryGrouper.Group(new List<EpisodeInfo> { Ep(1, "1000001"), Ep(2, "1000002"), Ep(3, "1000050") }, rule);
      Check(rule.Sources.SequenceEqual(new[] { "Example Show stories" })
            && stories.Single(s => s.Episodes.Count > 1).Episodes.Select(e => e.Id).SequenceEqual(new[] { "ex1", "ex2" }),
          "built-in Example Show list found by TVDB id, links The Long Night by episode id");
  }
  ```

  Add a check like "built-in rules never match by name" when the series has a namesake (a remake, a reboot, a spin-off of the same name).

In [`README.md`](README.md), add the series to the built-in lists in "How episodes get linked".

Then run both [checks](#build-and-test).

### 5. Check it on a real library

Install your build on a server that has the series ([Trying a build on your server](#trying-a-build-on-your-server)), then **Dashboard → Plugins → Story Shuffle → Check a series**: pick the series and press *Preview*. Check that:

- "Rules used" names your list;
- every story is there, in play order;
- "rule entries not found in this series" lists only episodes the library doesn't have.

Paste the *Preview* output into the pull request, with the library's metadata provider and language.

## Code changes

- New behaviour gets a `Check(...)` in [`tests/StoryShuffle.Tests/Program.cs`](tests/StoryShuffle.Tests/Program.cs).
- Keep the README's ["How episodes get linked"](README.md#how-episodes-get-linked) and ["Settings"](README.md#settings) in sync with what the code does.
- User-facing text is English.
- The settings page ([`Configuration/configPage.html`](src/Jellyfin.Plugin.StoryShuffle/Configuration/configPage.html)) runs inside Jellyfin's dashboard. For a change to it, put screenshots of the page in the pull request, desktop and phone width.

## Releasing

The version lives in the tag only: the release build writes it into the DLL and the packaged `meta.json`.

1. Tag the commit on `main` with an annotated tag `vX.Y.Z.W`. Its message is the changelog shown in Jellyfin's catalog and on the release:

   ```sh
   git tag -a v1.1.0.0 -m "Short summary of the release" -m "- Longer notes, one per line"
   git push origin v1.1.0.0
   ```

   A lightweight tag works too: the release then gets GitHub's generated notes.
2. `.github/workflows/release.yml` builds and tests that commit, publishes `StoryShuffle_X.Y.Z.W.zip` (the DLL and `meta.json`) as a GitHub release and adds the version, with the zip's MD5, to `manifest.json` on the `gh-pages` branch. Older versions stay listed.

One-time setup:

- **Settings → Pages**: deploy from the `gh-pages` branch, root folder (the first release creates the branch).
- **Settings → Actions → General**: require approval for all outside collaborators, since pull requests from forks run on the self-hosted runner. Read a fork's changes before approving its run: its code runs as the runner's user and can change anything that user can write, including the tools a later release uses.
- The runner needs `git` and the GitHub CLI `gh`. The workflows install the .NET 8 SDK themselves: CI into the runner's tool cache, reused between runs, and each release a fresh copy, so pull request code can't swap the SDK that builds a release. For a stronger split, run releases on a separate runner (its own label in `release.yml`) that never runs pull requests.

`tools/Release` does the packaging and the manifest update (`dotnet run -c Release --project tools/Release -- package|manifest …`); the test program checks it too.
