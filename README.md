# Story Shuffle for Jellyfin 12

Makes the normal **Shuffle** button on a series or season keep multi-part stories together. A random story is picked, then every part plays in order, then the next random story starts.

No new buttons and nothing to change in your apps. The plugin answers the "give me random episodes of this series" request that the shuffle button sends, so it works in the web client and the Android app. It also works in most other apps that ask the server for the shuffle.

## Install

### From Jellyfin's catalog (recommended)

1. **Dashboard → Plugins → Repositories**: add a repository named `Story Shuffle` with the URL
   `https://bryansmee.github.io/jellyfin-story-shuffle/manifest.json`.
2. **Catalog**: install *Story Shuffle*, then restart Jellyfin. New versions show up there as updates.
3. On each device, reload the web client once. On Android, close and reopen the app.

### By hand

1. Stop Jellyfin.
2. Download `StoryShuffle_<version>.zip` from the [latest release](https://github.com/BryanSmee/jellyfin-story-shuffle/releases/latest) and unzip it into Jellyfin's `plugins` folder, in a folder of its own: you end up with `plugins/StoryShuffle_<version>/Jellyfin.Plugin.StoryShuffle.dll` and `meta.json` next to it.
   - Docker: `/config/plugins/`
   - Linux package: `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Start Jellyfin. **Dashboard → Plugins** should list *Story Shuffle* as Active, with that version.
4. On each device, reload the web client once. On Android, close and reopen the app.

Uninstall by deleting the folder and restarting.

## How episodes get linked

For each series, in this order:

1. **Story lists.** Doctor Who (2005 onwards, including Flux and the 2024–25 seasons) and South Park ship built in. You can add your own.
2. **Titles.** "Part 1 / Part 2", "(1) / (2)", "Episode I / II" and similar on neighbouring episodes. This also covers classic Doctor Who serials named like "Spearhead from Space (1)…(4)".
3. **Descriptions.** "Part two of two", "Conclusion of the two-part…", "To be continued", "Continued from last week"… This is off for Doctor Who and South Park, because their lists are already exact.

Stories you watched in the last 14 days go to the back of the queue. Change the number in the settings, or set 0 to turn it off.

## Settings

**Dashboard → Plugins → Story Shuffle**

- **Check a series.** Pick a series and press *Preview* to see exactly which episodes will play together and why. Use it after adding a show.
- **Your series rules.** A JSON list that extends the built-in ones. Episodes can be given by title or as `S06E01`:

```json
[
  { "series": ["Star Trek: The Next Generation"],
    "groups": [["The Best of Both Worlds (1)", "The Best of Both Worlds (2)"]] },

  { "series": ["South Park"], "seasonsAsStory": [20] },

  { "series": ["Some Show"], "neverGroup": ["Title wrongly linked"], "exclude": ["Clip Show"] }
]
```

Every field: `series` (names, year suffix ignored, or Jellyfin ids), `groups`, `seasonsAsStory`, `neverGroup`, `exclude`, `titleHeuristic`, `overviewHeuristic`, `excludeSpecials`, `enabled` (false = normal shuffle for that show), `replaceBuiltIn`.

## Limits

- The web and Android apps load 100 episodes per shuffle. That's Jellyfin's own limit; the plugin fills it with whole stories.
- Apps that download the whole episode list and shuffle it on the device won't be affected. Infuse is one of these.
- Early 1960s Doctor Who episodes with individual titles (e.g. "The Cave of Skulls") aren't detected. Add them as a rule if you have them.

## Building

`dotnet build -c Release` in `src/Jellyfin.Plugin.StoryShuffle`. It needs no NuGet access: the `ref/` folder holds compile-only stand-ins for the few Jellyfin types the plugin uses. `tests/` builds those types from Jellyfin 12.4's real source and runs the plugin end to end against a local web server: `dotnet run -c Release` in `tests/StoryShuffle.Tests`.

GitHub Actions runs the same build and tests on every pull request and push to `main` (`.github/workflows/ci.yml`, on the self-hosted runner). Each run keeps the built DLL and `meta.json` as an artifact you can unzip into `plugins/` to try a pull request.

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
- **Settings → Actions → General**: require approval for all outside collaborators, since pull requests from forks run on the self-hosted runner.
- The runner needs `git` and the GitHub CLI `gh`. The workflows install the .NET 8 SDK into the runner's tool cache themselves.

`tools/Release` does the packaging and the manifest update (`dotnet run -c Release --project tools/Release -- package|manifest …`); the test program checks it too.
